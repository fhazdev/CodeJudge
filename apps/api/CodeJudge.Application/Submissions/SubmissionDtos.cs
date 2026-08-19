using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;

namespace CodeJudge.Application.Submissions;

public sealed record SubmissionDto(
    Guid Id,
    string ProblemSlug,
    string Language,
    SubmissionStatus Status,
    bool IsTerminal,
    int? RuntimeMs,
    int? MemoryKb,
    int? FailedCaseOrdinal,
    string? StderrExcerpt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Listing shape: no code, no diagnostics, just enough for a history table.</summary>
public sealed record SubmissionSummaryDto(
    Guid Id,
    string ProblemSlug,
    SubmissionStatus Status,
    int? RuntimeMs,
    DateTimeOffset CreatedAt);

public static class SubmissionMapping
{
    public static SubmissionDto ToDto(this Submission submission, string problemSlug) =>
        new(
            submission.Id,
            problemSlug,
            submission.Language,
            submission.Status,
            // Computed server-side rather than left to each client to re-derive from the
            // status enum. The SPA polls until this is true, and duplicating the list of
            // terminal states in TypeScript is how the two drift apart.
            submission.Status.IsTerminal(),
            submission.RuntimeMs,
            submission.MemoryKb,
            submission.FailedCaseOrdinal,
            submission.StderrExcerpt,
            submission.CreatedAt,
            submission.CompletedAt);

    public static SubmissionSummaryDto ToSummary(this Submission submission, string problemSlug) =>
        new(submission.Id, problemSlug, submission.Status, submission.RuntimeMs, submission.CreatedAt);
}
