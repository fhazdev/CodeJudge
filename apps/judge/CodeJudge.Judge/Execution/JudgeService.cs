using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CodeJudge.Judge.Execution;

/// <summary>
/// Compile once, run every test case, return the first verdict that is not Accepted.
/// This type holds no database or queue dependency on purpose: it takes a problem and some
/// code and returns a verdict, which is what makes the verdict matrix in the test project
/// possible without Postgres or Azure.
/// </summary>
public sealed class JudgeService(
    CompilationService compiler,
    SandboxRunner sandbox,
    JudgeOptions options,
    ILogger<JudgeService> logger)
{
    public async Task<JudgeResult> JudgeAsync(
        Problem problem,
        string submissionCode,
        CancellationToken cancellationToken = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.SubmissionBudget);

        var compilation = await compiler.CompileAsync(
            problem.HarnessCode, submissionCode, budget.Token);

        if (!compilation.Success)
        {
            logger.LogInformation("Submission failed to compile for problem {Slug}", problem.Slug);
            return new JudgeResult(SubmissionStatus.CompileError, StderrExcerpt: compilation.Diagnostics);
        }

        var workingDirectory = Directory.CreateTempSubdirectory("codejudge-");
        try
        {
            var assemblyPath = Path.Combine(workingDirectory.FullName, "Submission.dll");
            await File.WriteAllBytesAsync(assemblyPath, compilation.Assembly!, cancellationToken);

            return await RunCasesAsync(problem, assemblyPath, budget.Token, cancellationToken);
        }
        finally
        {
            TryDelete(workingDirectory);
        }
    }

    private async Task<JudgeResult> RunCasesAsync(
        Problem problem,
        string assemblyPath,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {
        var cases = problem.TestCases.OrderBy(c => c.Ordinal).ToList();
        if (cases.Count == 0)
        {
            logger.LogError("Problem {Slug} has no test cases", problem.Slug);
            return new JudgeResult(
                SubmissionStatus.InternalError, StderrExcerpt: "Problem has no test cases.");
        }

        var timeLimit = TimeSpan.FromMilliseconds(problem.TimeLimitMs);
        var slowestMs = 0L;
        var peakBytes = 0L;

        foreach (var testCase in cases)
        {
            if (budgetToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // The whole-submission budget ran out. This is still a real verdict, and
                // we are still alive to write it, which is the point of enforcing it here
                // rather than letting the job's replicaTimeout fire.
                return new JudgeResult(
                    SubmissionStatus.TimeLimitExceeded,
                    RuntimeMs: (int)slowestMs,
                    MemoryKb: (int)(peakBytes / 1024),
                    FailedCaseOrdinal: testCase.Ordinal,
                    StderrExcerpt:
                        $"Exceeded the overall submission budget of " +
                        $"{options.SubmissionBudget.TotalSeconds:0} seconds.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var execution = await sandbox.RunAsync(
                assemblyPath, testCase.Input, timeLimit, problem.MemoryLimitKb, cancellationToken);

            slowestMs = Math.Max(slowestMs, execution.ElapsedMs);
            peakBytes = Math.Max(peakBytes, execution.PeakWorkingSetBytes);

            var verdict = Evaluate(execution, testCase);
            if (verdict is null)
            {
                continue;
            }

            return new JudgeResult(
                verdict.Value,
                RuntimeMs: (int)slowestMs,
                MemoryKb: (int)(peakBytes / 1024),
                FailedCaseOrdinal: testCase.Ordinal,
                StderrExcerpt: DescribeFailure(verdict.Value, execution, testCase));
        }

        return new JudgeResult(
            SubmissionStatus.Accepted,
            RuntimeMs: (int)slowestMs,
            MemoryKb: (int)(peakBytes / 1024));
    }

    /// <summary>Returns null when the case passed.</summary>
    private static SubmissionStatus? Evaluate(CaseExecution execution, TestCase testCase)
    {
        // Order matters. A submission that is killed for time will also have produced no
        // output, and reporting that as WrongAnswer would be actively misleading.
        if (execution.MemoryExceeded)
        {
            return SubmissionStatus.MemoryLimitExceeded;
        }

        if (execution.TimedOut)
        {
            return SubmissionStatus.TimeLimitExceeded;
        }

        // Exit code 2 is the runner failing to start, which is our bug, not the user's.
        if (execution.ExitCode == 2)
        {
            return SubmissionStatus.InternalError;
        }

        if (execution.ExitCode != 0)
        {
            return SubmissionStatus.RuntimeError;
        }

        return OutputComparer.Matches(testCase.ExpectedOutput, execution.StandardOutput)
            ? null
            : SubmissionStatus.WrongAnswer;
    }

    private string DescribeFailure(SubmissionStatus status, CaseExecution execution, TestCase testCase)
    {
        var detail = status switch
        {
            SubmissionStatus.TimeLimitExceeded =>
                $"Test case {testCase.Ordinal} did not finish within {execution.ElapsedMs} ms.",

            SubmissionStatus.MemoryLimitExceeded =>
                $"Test case {testCase.Ordinal} exceeded the memory limit " +
                $"(peak {execution.PeakWorkingSetBytes / 1024 / 1024} MB).",

            SubmissionStatus.WrongAnswer => BuildWrongAnswerDetail(execution, testCase),

            _ => execution.StandardError
        };

        return Truncate(detail);
    }

    private static string BuildWrongAnswerDetail(CaseExecution execution, TestCase testCase)
    {
        // Hidden cases leak nothing but the ordinal. Showing the input for a visible case
        // is the whole reason visible cases exist.
        if (testCase.IsHidden)
        {
            return $"Wrong answer on hidden test case {testCase.Ordinal}.";
        }

        return string.Join(Environment.NewLine,
            $"Wrong answer on test case {testCase.Ordinal}.",
            $"Input:    {OutputComparer.Normalize(testCase.Input).Replace("\n", " ⏎ ")}",
            $"Expected: {OutputComparer.Normalize(testCase.ExpectedOutput)}",
            $"Actual:   {OutputComparer.Normalize(execution.StandardOutput)}");
    }

    private string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= options.MaxStderrLength
            ? value
            : value[..options.MaxStderrLength] + "\n… truncated";
    }

    private void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException ex)
        {
            // A killed process can still hold a file handle briefly. The container is
            // torn down after the run anyway, so this is a tidiness failure, not a leak.
            logger.LogDebug(ex, "Could not clean up {Directory}", directory.FullName);
        }
    }
}
