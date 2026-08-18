using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;

namespace CodeJudge.Application.Problems;

public sealed record ProblemSummaryDto(
    string Slug,
    string Title,
    Difficulty Difficulty);

/// <summary>
/// A worked example: only ever built from a test case with <c>IsHidden == false</c>.
/// </summary>
public sealed record ProblemExampleDto(
    int Ordinal,
    string Input,
    string ExpectedOutput);

public sealed record ProblemDetailDto(
    string Slug,
    string Title,
    Difficulty Difficulty,
    string StatementMd,
    string? ConstraintsMd,
    string StarterCode,
    int TimeLimitMs,
    int MemoryLimitKb,
    IReadOnlyList<ProblemExampleDto> Examples);

public static class ProblemMapping
{
    public static ProblemSummaryDto ToSummary(this Problem problem) =>
        new(problem.Slug, problem.Title, problem.Difficulty);

    /// <summary>
    /// The single place a Problem becomes something the outside world can see.
    ///
    /// Two fields are deliberately absent and must stay absent: HarnessCode, which reveals
    /// the exact expected signature and the shape of every case, and any test case with
    /// IsHidden set. Adding a convenient object mapper here would be the obvious way to
    /// leak both by accident, which is why the projection is written out by hand and
    /// covered by a test.
    /// </summary>
    public static ProblemDetailDto ToDetail(this Problem problem) =>
        new(
            problem.Slug,
            problem.Title,
            problem.Difficulty,
            problem.StatementMd,
            problem.ConstraintsMd,
            problem.StarterCode,
            problem.TimeLimitMs,
            problem.MemoryLimitKb,
            problem.TestCases
                .Where(testCase => !testCase.IsHidden)
                .OrderBy(testCase => testCase.Ordinal)
                .Select(testCase => new ProblemExampleDto(
                    testCase.Ordinal, testCase.Input, testCase.ExpectedOutput))
                .ToList());
}
