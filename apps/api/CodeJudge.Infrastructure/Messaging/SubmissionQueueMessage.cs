using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeJudge.Infrastructure.Messaging;

/// <summary>
/// The contract between the API and the judge.
///
/// Deliberately just an id. Queue messages cap at 64 KB and submitted code routinely runs
/// to several, so the code stays in the database. It also means a message stays valid if
/// the row is updated between enqueue and dequeue.
/// </summary>
public sealed record SubmissionQueueMessage(
    [property: JsonPropertyName("submissionId")] Guid SubmissionId,
    [property: JsonPropertyName("enqueuedAt")] DateTimeOffset EnqueuedAt)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    /// <summary>Returns null for anything unparseable, which the caller treats as poison.</summary>
    public static SubmissionQueueMessage? TryDeserialize(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<SubmissionQueueMessage>(body, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
