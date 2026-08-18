using CodeJudge.Application.Abstractions;
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

        return services;
    }
}
