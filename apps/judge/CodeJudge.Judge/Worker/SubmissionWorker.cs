using CodeJudge.Domain.Enums;
using CodeJudge.Infrastructure.Messaging;
using CodeJudge.Infrastructure.Persistence;
using CodeJudge.Judge.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CodeJudge.Judge.Worker;

/// <summary>
/// Claims one submission from the queue, judges it, writes the verdict back, and deletes
/// the message.
///
/// Shaped around one message per call rather than an internal loop, because that is what
/// a Container Apps Job execution is: KEDA starts a container per unit of work and the
/// container exits. The local `worker` command wraps this in a poll loop purely for
/// convenience during development.
/// </summary>
public sealed class SubmissionWorker(
    SubmissionQueueReader queue,
    CodeJudgeDbContext db,
    JudgeService judge,
    TimeProvider timeProvider,
    ILogger<SubmissionWorker> logger)
{
    public enum Outcome
    {
        /// <summary>Queue was empty.</summary>
        NoWork,

        /// <summary>A submission was judged and a verdict written.</summary>
        Judged,

        /// <summary>A message was discarded without producing a normal verdict.</summary>
        Discarded
    }

    public async Task<Outcome> ProcessNextAsync(CancellationToken ct = default)
    {
        var claimed = await queue.ClaimNextAsync(ct);
        if (claimed is null)
        {
            return Outcome.NoWork;
        }

        // Redelivery means a previous attempt claimed this and never deleted it: the judge
        // crashed, was killed, or the container was evicted. Retrying a couple of times
        // covers transient causes; past that, something about this submission reliably
        // breaks us, and retrying forever would burn the free tier on it.
        if (claimed.DequeueCount > SubmissionQueueReader.MaxDequeueCount)
        {
            logger.LogError(
                "Submission {SubmissionId} exceeded {Max} attempts; marking InternalError",
                claimed.SubmissionId, SubmissionQueueReader.MaxDequeueCount);

            await FailAsync(
                claimed.SubmissionId,
                "This submission could not be judged after several attempts.",
                ct);

            await queue.DeleteAsync(claimed.MessageId, claimed.PopReceipt, ct);
            return Outcome.Discarded;
        }

        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == claimed.SubmissionId, ct);

        if (submission is null)
        {
            // The row was deleted between enqueue and dequeue. There is nothing to judge
            // and nothing to report, so drop the message rather than retry it forever.
            logger.LogWarning(
                "Queue referenced submission {SubmissionId}, which no longer exists",
                claimed.SubmissionId);

            await queue.DeleteAsync(claimed.MessageId, claimed.PopReceipt, ct);
            return Outcome.Discarded;
        }

        if (submission.Status.IsTerminal())
        {
            // Already judged. Reachable when a verdict was written but the delete did not
            // land, so the message became visible again. Judging twice would be wasteful
            // and could overwrite a verdict the user is already looking at.
            logger.LogInformation(
                "Submission {SubmissionId} is already {Status}; discarding duplicate message",
                submission.Id, submission.Status);

            await queue.DeleteAsync(claimed.MessageId, claimed.PopReceipt, ct);
            return Outcome.Discarded;
        }

        var problem = await db.Problems
            .Include(p => p.TestCases)
            .FirstOrDefaultAsync(p => p.Id == submission.ProblemId, ct);

        if (problem is null)
        {
            await FailAsync(submission.Id, "The problem for this submission no longer exists.", ct);
            await queue.DeleteAsync(claimed.MessageId, claimed.PopReceipt, ct);
            return Outcome.Discarded;
        }

        submission.Status = SubmissionStatus.Running;
        submission.AttemptCount = (int)claimed.DequeueCount;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Judging submission {SubmissionId} against {Slug}", submission.Id, problem.Slug);

        var result = await judge.JudgeAsync(problem, submission.Code, ct);

        submission.Status = result.Status;
        submission.RuntimeMs = result.RuntimeMs;
        submission.MemoryKb = result.MemoryKb;
        submission.FailedCaseOrdinal = result.FailedCaseOrdinal;
        submission.StderrExcerpt = result.StderrExcerpt;
        submission.CompletedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Submission {SubmissionId} verdict {Status} in {RuntimeMs} ms",
            submission.Id, result.Status, result.RuntimeMs);

        // Last, and only after the verdict is durable. Deleting earlier would mean a crash
        // mid-judge loses the work entirely, with nothing left to redeliver.
        await queue.DeleteAsync(claimed.MessageId, claimed.PopReceipt, ct);

        return Outcome.Judged;
    }

    private async Task FailAsync(Guid submissionId, string message, CancellationToken ct)
    {
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null)
        {
            return;
        }

        submission.Status = SubmissionStatus.InternalError;
        submission.StderrExcerpt = message;
        submission.CompletedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}
