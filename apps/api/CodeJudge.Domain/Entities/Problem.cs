using CodeJudge.Domain.Enums;

namespace CodeJudge.Domain.Entities;

/// <summary>
/// A problem carries not just its statement but the knowledge of how to invoke a
/// solution to it: <see cref="HarnessCode"/> is compiled alongside the submission and
/// supplies the entry point. See section 4 of the build plan.
/// </summary>
public class Problem
{
    public Guid Id { get; set; }

    /// <summary>URL segment, e.g. "two-sum". Unique.</summary>
    public required string Slug { get; set; }

    public required string Title { get; set; }

    public Difficulty Difficulty { get; set; }

    /// <summary>Markdown, rendered by the SPA.</summary>
    public required string StatementMd { get; set; }

    public string? ConstraintsMd { get; set; }

    /// <summary>What loads into the Monaco editor when the problem is opened.</summary>
    public required string StarterCode { get; set; }

    /// <summary>
    /// C# source containing the entry point. Reads a test case from stdin, calls into the
    /// user's Solution class, writes the result to stdout. Never exposed over the API:
    /// it reveals the exact expected signature and, indirectly, the shape of hidden cases.
    /// </summary>
    public required string HarnessCode { get; set; }

    /// <summary>Per test case wall-clock budget. The innermost timeout (section 5).</summary>
    public int TimeLimitMs { get; set; } = 2_000;

    public int MemoryLimitKb { get; set; } = 262_144;

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<TestCase> TestCases { get; set; } = [];
}
