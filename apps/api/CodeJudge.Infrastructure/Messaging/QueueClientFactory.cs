using Azure.Identity;
using Azure.Storage.Queues;

namespace CodeJudge.Infrastructure.Messaging;

public sealed class QueueOptions
{
    /// <summary>
    /// Azurite connection string for local development. "UseDevelopmentStorage=true" is
    /// the well-known shorthand, and its embedded key is Microsoft's published emulator
    /// key, identical on every machine. It is not a secret and never reaches Azure.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Full queue URI, used in Azure with a managed identity instead of a key.
    /// Takes precedence over <see cref="ConnectionString"/> when both are present.
    /// </summary>
    public string? QueueUri { get; init; }

    public string QueueName { get; init; } = "submissions";
}

public static class QueueClientFactory
{
    /// <summary>
    /// Two authentication paths on purpose. Locally there is no Entra identity to borrow,
    /// so Azurite's shared key is the only option; in Azure a shared key would be a stored
    /// credential to leak and rotate, so the managed identity is used instead. The choice
    /// is made by which setting is present, never by an environment name.
    /// </summary>
    public static QueueClient Create(QueueOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.QueueUri))
        {
            return new QueueClient(new Uri(options.QueueUri), new DefaultAzureCredential());
        }

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new QueueClient(options.ConnectionString, options.QueueName);
        }

        throw new InvalidOperationException(
            "No queue configured. Set Queue:QueueUri (Azure) or Queue:ConnectionString (Azurite).");
    }
}
