namespace CodeJudge.Domain.Entities;

public class TestCase
{
    public Guid Id { get; set; }

    public Guid ProblemId { get; set; }

    /// <summary>Execution order. Also what <c>failed_case_ordinal</c> refers to.</summary>
    public int Ordinal { get; set; }

    /// <summary>Fed to the harness on stdin, verbatim.</summary>
    public required string Input { get; set; }

    public required string ExpectedOutput { get; set; }

    /// <summary>
    /// Hidden cases are never returned by the API. A visible case is shown on the problem
    /// page as a worked example, and is the only kind whose input may appear in a failure
    /// message.
    /// </summary>
    public bool IsHidden { get; set; } = true;

    public Problem? Problem { get; set; }
}
