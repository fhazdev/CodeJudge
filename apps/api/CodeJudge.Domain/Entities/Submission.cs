using CodeJudge.Domain.Enums;

namespace CodeJudge.Domain.Entities;

public class Submission
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ProblemId { get; set; }

    /// <summary>Only "csharp" in v1. Present so adding a language is not a schema change.</summary>
    public string Language { get; set; } = "csharp";

    public required string Code { get; set; }

    public SubmissionStatus Status { get; set; } = SubmissionStatus.Queued;

    /// <summary>Slowest test case, in milliseconds. Null until judged.</summary>
    public int? RuntimeMs { get; set; }

    public int? MemoryKb { get; set; }

    /// <summary>Ordinal of the case that produced a non-Accepted verdict, if any.</summary>
    public int? FailedCaseOrdinal { get; set; }

    /// <summary>Compiler diagnostics or captured stderr, truncated. Shown to the user.</summary>
    public string? StderrExcerpt { get; set; }

    /// <summary>
    /// Queue dequeue count, mirrored here so a poison message is visible in the data
    /// rather than only in queue metadata.
    /// </summary>
    public int AttemptCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public User? User { get; set; }

    public Problem? Problem { get; set; }
}
