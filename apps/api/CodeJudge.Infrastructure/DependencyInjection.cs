using Azure.Storage.Queues;
using CodeJudge.Application.Abstractions;
using CodeJudge.Infrastructure.Messaging;
using CodeJudge.Infrastructure.Persistence;
using CodeJudge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CodeJudge.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CodeJudgeDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                // Neon's free tier autosuspends after a few minutes idle, so the first
                // connection after a quiet period can fail while the compute wakes.
                // Retrying is the difference between a cold demo and a 500.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            }));

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IProblemRepository, ProblemRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();

        return services;
    }

    /// <summary>
    /// Registers the submission queue. Separate from AddInfrastructure because the judge
    /// needs the read side without a MediatR pipeline, and the API needs the write side
    /// without a dequeue loop.
    /// </summary>
    public static IServiceCollection AddSubmissionQueue(
        this IServiceCollection services, QueueOptions options)
    {
        // QueueClient is thread-safe and designed to be long-lived, so a singleton avoids
        // rebuilding its HTTP pipeline (and re-authenticating) on every request.
        services.AddSingleton(_ => QueueClientFactory.Create(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISubmissionQueue, StorageSubmissionQueue>();
        services.AddSingleton<SubmissionQueueReader>();

        return services;
    }
}
