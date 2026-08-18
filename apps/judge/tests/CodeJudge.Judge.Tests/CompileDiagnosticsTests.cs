using CodeJudge.Domain.Enums;
using CodeJudge.Judge.Execution;

namespace CodeJudge.Judge.Tests;

public sealed class CompileDiagnosticsTests(JudgeFixture fixture) : IClassFixture<JudgeFixture>
{
    /// <summary>
    /// The risk-table item. The harness is compiled as a separate syntax tree rather than
    /// concatenated onto the submission, so Roslyn reports errors against the user's own
    /// line numbers. Concatenate the two and every diagnostic here shifts by the length of
    /// the harness, silently pointing at the wrong line.
    /// </summary>
    [Fact]
    public async Task ErrorLineNumbersReferToTheSubmissionNotTheHarness()
    {
        const string codeWithErrorOnLineFive =
            """
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    int x = "not an int";
                    return new int[0];
                }
            }
            """;

        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(), codeWithErrorOnLineFive, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.CompileError);
        result.StderrExcerpt.ShouldNotBeNull();

        // Two Sum's harness is a dozen lines long. If it were prepended, this would be
        // reported somewhere in the high teens instead.
        result.StderrExcerpt.ShouldContain($"{CompilationService.SubmissionPath}(5,");
    }

    [Fact]
    public async Task SignatureMismatchExplainsItselfInsteadOfShowingARawCS1061()
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read("wrong-signature.cs"),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.CompileError);
        result.StderrExcerpt.ShouldNotBeNull();
        result.StderrExcerpt.ShouldContain("does not match the expected signature");
    }

    [Fact]
    public async Task CompileTimeoutIsReportedAsCompileErrorNotInternalError()
    {
        // No submission reliably makes Roslyn hang in under a second, so the cap is moved
        // rather than the input. What is under test is the handling, not the pathology.
        var judge = JudgeFixture.Create(new JudgeOptions
        {
            CompileTimeout = TimeSpan.FromMilliseconds(1)
        });

        var result = await judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read("correct.cs"),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.CompileError);
        result.StderrExcerpt.ShouldNotBeNull();
        result.StderrExcerpt.ShouldContain("timed out");
    }

    [Fact]
    public async Task SubmissionCannotReferenceTheJudgesOwnDependencies()
    {
        // The judge process has EF Core and Npgsql loaded. The reference allowlist keeps
        // them out of the submission's compilation, so this must not compile.
        const string reachesForEfCore =
            """
            using Microsoft.EntityFrameworkCore;

            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    var options = new DbContextOptionsBuilder();
                    return new int[0];
                }
            }
            """;

        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(), reachesForEfCore, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.CompileError);
    }
}
