using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;

namespace CodeJudge.Infrastructure.Messaging;

/// <summary>One claimed message, plus what the judge needs to release or delete it.</summary>
public sealed record ClaimedSubmission(
    Guid SubmissionId,
    string MessageId,
    string PopReceipt,
    long DequeueCount);

/// <summary>
/// The receive side of the queue, used by the judge.
///
/// Note what KEDA does and does not do: it triggers a job execution based on queue
/// <em>depth</em>, and never hands the message to the container. The job must claim and
/// then delete the message itself. Skip the delete and the message becomes visible again,
/// KEDA sees depth, and the job is triggered forever.
/// </summary>
public sealed class SubmissionQueueReader(QueueClient client, ILogger<SubmissionQueueReader> logger)
{
    /// <summary>
    /// Comfortably longer than a submission can take (90 s budget), so a message cannot
    /// reappear and be judged a second time while the first execution is still working.
    /// </summary>
    public static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Beyond this many attempts a message is poison and gets a terminal verdict.</summary>
    public const int MaxDequeueCount = 3;

    public async Task EnsureQueueExistsAsync(CancellationToken ct = default) =>
        await client.CreateIfNotExistsAsync(cancellationToken: ct);

    /// <summary>Claims one message, or null when the queue is empty.</summary>
    public async Task<ClaimedSubmission?> ClaimNextAsync(CancellationToken ct = default)
    {
        QueueMessage? message = await client.ReceiveMessageAsync(VisibilityTimeout, ct);

        if (message is null)
        {
            return null;
        }

        var parsed = SubmissionQueueMessage.TryDeserialize(message.MessageText);

        if (parsed is null)
        {
            // Unparseable and therefore permanently unprocessable. Leaving it would block
            // nothing (later messages are still delivered) but would keep KEDA triggering
            // executions with no work to do, so drop it now.
            logger.LogError(
                "Discarding unparseable queue message {MessageId}: {Body}",
                message.MessageId, message.MessageText);

            await DeleteAsync(message.MessageId, message.PopReceipt, ct);
            return null;
        }

        return new ClaimedSubmission(
            parsed.SubmissionId, message.MessageId, message.PopReceipt, message.DequeueCount);
    }

    public async Task DeleteAsync(string messageId, string popReceipt, CancellationToken ct = default) =>
        await client.DeleteMessageAsync(messageId, popReceipt, ct);

    public async Task<int> ApproximateDepthAsync(CancellationToken ct = default)
    {
        var properties = await client.GetPropertiesAsync(ct);
        return properties.Value.ApproximateMessagesCount;
    }
}
