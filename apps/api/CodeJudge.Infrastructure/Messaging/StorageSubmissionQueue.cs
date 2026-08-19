using Azure.Storage.Queues;
using CodeJudge.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace CodeJudge.Infrastructure.Messaging;

public sealed class StorageSubmissionQueue(
    QueueClient client,
    TimeProvider timeProvider,
    ILogger<StorageSubmissionQueue> logger) : ISubmissionQueue
{
    public async Task EnqueueAsync(Guid submissionId, CancellationToken ct = default)
    {
        // Creating on demand keeps local development to a single `docker compose up`,
        // with no separate queue-provisioning step. In Azure the queue is Terraform-managed
        // and this is a no-op.
        await client.CreateIfNotExistsAsync(cancellationToken: ct);

        var message = new SubmissionQueueMessage(submissionId, timeProvider.GetUtcNow());
        await client.SendMessageAsync(message.Serialize(), cancellationToken: ct);

        logger.LogInformation("Enqueued submission {SubmissionId}", submissionId);
    }
}
