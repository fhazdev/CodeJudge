using CodeJudge.Application.Abstractions;
using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;
using CodeJudge.Infrastructure;
using CodeJudge.Infrastructure.Messaging;
using CodeJudge.Infrastructure.Persistence;
using CodeJudge.Infrastructure.Persistence.Seed;
using CodeJudge.Judge.Execution;
using CodeJudge.Judge.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The judge, as a CLI.
//
// `worker` is the real entry point: in Azure it runs as a Container Apps Job, one
// execution per submission. The other commands exist because being able to judge a file
// straight from a terminal, with no queue and no cloud, is what let the riskiest part of
// this system be proven before any infrastructure existed.
//
//   dotnet run --project apps/judge/CodeJudge.Judge -- seed
//   dotnet run --project apps/judge/CodeJudge.Judge -- judge --problem two-sum --file sol.cs
//   dotnet run --project apps/judge/CodeJudge.Judge -- problems
//   dotnet run --project apps/judge/CodeJudge.Judge -- worker
//   dotnet run --project apps/judge/CodeJudge.Judge -- worker --once
//   dotnet run --project apps/judge/CodeJudge.Judge -- submit --problem two-sum --file sol.cs
//   dotnet run --project apps/judge/CodeJudge.Judge -- submissions

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

var connectionString =
    Environment.GetEnvironmentVariable("CODEJUDGE_CONNECTION")
    ?? DesignTimeDbContextFactory.LocalConnectionString;

builder.Services.AddDbContext<CodeJudgeDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddSingleton(new JudgeOptions());
builder.Services.AddSingleton<CompilationService>();
builder.Services.AddSingleton(sp => new SandboxRunner(sp.GetRequiredService<JudgeOptions>()));
builder.Services.AddSingleton<JudgeService>();

builder.Services.AddSubmissionQueue(new QueueOptions
{
    QueueUri = Environment.GetEnvironmentVariable("CODEJUDGE_QUEUE_URI"),
    ConnectionString = Environment.GetEnvironmentVariable("CODEJUDGE_QUEUE_CONNECTION")
                       ?? "UseDevelopmentStorage=true",
    QueueName = Environment.GetEnvironmentVariable("CODEJUDGE_QUEUE_NAME") ?? "submissions"
});

builder.Services.AddScoped<SubmissionWorker>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var services = scope.ServiceProvider;

var command = args.FirstOrDefault()?.ToLowerInvariant();

try
{
    return command switch
    {
        "seed" => await SeedAsync(services),
        "problems" => await ListProblemsAsync(services),
        "judge" => await JudgeAsync(services, args),
        "worker" => await WorkerAsync(host, args),
        "submit" => await SubmitAsync(services, args),
        "submissions" => await ListSubmissionsAsync(services),
        _ => Usage()
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int Usage()
{
    Console.WriteLine("""
        CodeJudge judge

          seed                                   apply migrations and upsert seed problems
          problems                               list the seeded problems
          judge --problem <slug> --file <path>   judge a file against a problem
          worker [--once]                        consume the submission queue
          submit --problem <slug> --file <path>  queue a submission (no API, no auth)
          submissions                            recent submissions and their verdicts

        Postgres comes from CODEJUDGE_CONNECTION and the queue from
        CODEJUDGE_QUEUE_URI (Azure) or CODEJUDGE_QUEUE_CONNECTION (Azurite),
        all defaulting to docker compose. Start it with: docker compose up -d
        """);
    return 1;
}

/// <summary>
/// Two shapes for the same work.
///
/// --once claims at most one message and exits, which is exactly what a Container Apps
/// Job execution does: KEDA starts a container per unit of work and the container is torn
/// down afterwards. The default loop exists because polling by hand during development is
/// tedious, and it is not how this runs in Azure.
/// </summary>
static async Task<int> WorkerAsync(IHost host, string[] args)
{
    var once = args.Contains("--once");
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("worker");

    using var stopping = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        // Finish the submission in flight rather than abandoning it mid-judge, which
        // would leave the row stuck in Running until the message redelivers.
        e.Cancel = true;
        logger.LogInformation("Shutdown requested; finishing current submission");
        stopping.Cancel();
    };

    // The queue is created on demand so a fresh checkout needs no provisioning step.
    using (var scope = host.Services.CreateScope())
    {
        await scope.ServiceProvider
            .GetRequiredService<SubmissionQueueReader>()
            .EnsureQueueExistsAsync();
    }

    if (once)
    {
        using var scope = host.Services.CreateScope();
        var outcome = await scope.ServiceProvider
            .GetRequiredService<SubmissionWorker>()
            .ProcessNextAsync();

        Console.WriteLine(outcome);
        return 0;
    }

    logger.LogInformation("Worker started. Ctrl+C to stop.");

    var idleDelay = TimeSpan.FromSeconds(1);

    while (!stopping.IsCancellationRequested)
    {
        // A new scope per message: the DbContext is scoped, and reusing one across a
        // long-lived loop would accumulate tracked entities for every submission ever
        // judged by this process.
        using var scope = host.Services.CreateScope();

        SubmissionWorker.Outcome outcome;
        try
        {
            outcome = await scope.ServiceProvider
                .GetRequiredService<SubmissionWorker>()
                .ProcessNextAsync(stopping.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            // One poisonous submission must not kill the loop. The message stays
            // invisible until its visibility timeout lapses, then redelivers, and the
            // dequeue-count check eventually retires it.
            logger.LogError(ex, "Worker iteration failed; continuing");
            outcome = SubmissionWorker.Outcome.NoWork;
        }

        if (outcome == SubmissionWorker.Outcome.NoWork)
        {
            try
            {
                await Task.Delay(idleDelay, stopping.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    logger.LogInformation("Worker stopped.");
    return 0;
}

/// <summary>
/// Creates and enqueues a submission the way the API does, without needing a bearer token.
///
/// Exists so the queue-to-verdict half of the pipeline can be exercised from a terminal.
/// Acquiring a real Entra token needs an interactive browser sign-in, which would make the
/// one part of the system that has no automated coverage also the hardest to try by hand.
/// </summary>
static async Task<int> SubmitAsync(IServiceProvider services, string[] args)
{
    var slug = ArgumentValue(args, "--problem");
    var file = ArgumentValue(args, "--file");

    if (slug is null || file is null)
    {
        Console.Error.WriteLine("submit requires --problem <slug> and --file <path>");
        return 1;
    }

    if (!File.Exists(file))
    {
        Console.Error.WriteLine($"no such file: {file}");
        return 1;
    }

    var db = services.GetRequiredService<CodeJudgeDbContext>();

    var problem = await db.Problems.FirstOrDefaultAsync(p => p.Slug == slug);
    if (problem is null)
    {
        Console.Error.WriteLine($"no problem with slug '{slug}'");
        return 1;
    }

    // A stable local identity, so repeated runs accumulate under one user rather than
    // creating a new row each time.
    const string localTenantId = "local";
    const string localObjectId = "local-dev-user";

    var user = await db.Users.FirstOrDefaultAsync(
        u => u.EntraTenantId == localTenantId && u.EntraObjectId == localObjectId);

    if (user is null)
    {
        user = new User
        {
            Id = Guid.CreateVersion7(),
            EntraTenantId = localTenantId,
            EntraObjectId = localObjectId,
            DisplayName = "Local Development",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
    }

    var submission = new Submission
    {
        Id = Guid.CreateVersion7(),
        UserId = user.Id,
        ProblemId = problem.Id,
        Language = "csharp",
        Code = await File.ReadAllTextAsync(file),
        Status = SubmissionStatus.Queued,
        CreatedAt = DateTimeOffset.UtcNow
    };

    db.Submissions.Add(submission);
    await db.SaveChangesAsync();

    await services.GetRequiredService<ISubmissionQueue>().EnqueueAsync(submission.Id);

    Console.WriteLine(submission.Id);
    return 0;
}

static async Task<int> ListSubmissionsAsync(IServiceProvider services)
{
    var db = services.GetRequiredService<CodeJudgeDbContext>();

    var submissions = await db.Submissions
        .Include(s => s.Problem)
        .OrderByDescending(s => s.CreatedAt)
        .Take(15)
        .Select(s => new { s.Id, Slug = s.Problem!.Slug, s.Status, s.RuntimeMs, s.FailedCaseOrdinal })
        .ToListAsync();

    if (submissions.Count == 0)
    {
        Console.WriteLine("No submissions yet.");
        return 0;
    }

    foreach (var s in submissions)
    {
        var caseText = s.FailedCaseOrdinal is null ? "" : $"case #{s.FailedCaseOrdinal}";
        Console.WriteLine($"{s.Id}  {s.Slug,-22} {s.Status,-20} {s.RuntimeMs,6} ms  {caseText}");
    }

    return 0;
}

static async Task<int> SeedAsync(IServiceProvider services)
{
    var db = services.GetRequiredService<CodeJudgeDbContext>();
    await DatabaseSeeder.MigrateAndSeedAsync(db);

    var count = await db.Problems.CountAsync();
    Console.WriteLine($"Migrated and seeded. {count} problem(s) in the database.");
    return 0;
}

static async Task<int> ListProblemsAsync(IServiceProvider services)
{
    var db = services.GetRequiredService<CodeJudgeDbContext>();

    var problems = await db.Problems
        .OrderBy(p => p.Title)
        .Select(p => new { p.Slug, p.Title, p.Difficulty, Cases = p.TestCases.Count })
        .ToListAsync();

    if (problems.Count == 0)
    {
        Console.WriteLine("No problems. Run: judge seed");
        return 0;
    }

    foreach (var problem in problems)
    {
        Console.WriteLine($"{problem.Slug,-24} {problem.Difficulty,-8} {problem.Cases} cases   {problem.Title}");
    }

    return 0;
}

static async Task<int> JudgeAsync(IServiceProvider services, string[] args)
{
    var slug = ArgumentValue(args, "--problem");
    var file = ArgumentValue(args, "--file");

    if (slug is null || file is null)
    {
        Console.Error.WriteLine("judge requires --problem <slug> and --file <path>");
        return 1;
    }

    if (!File.Exists(file))
    {
        Console.Error.WriteLine($"no such file: {file}");
        return 1;
    }

    var db = services.GetRequiredService<CodeJudgeDbContext>();
    var problem = await db.Problems
        .Include(p => p.TestCases)
        .FirstOrDefaultAsync(p => p.Slug == slug);

    if (problem is null)
    {
        Console.Error.WriteLine($"no problem with slug '{slug}'. Run: judge problems");
        return 1;
    }

    var code = await File.ReadAllTextAsync(file);
    var judge = services.GetRequiredService<JudgeService>();

    var result = await judge.JudgeAsync(problem, code);

    Console.WriteLine();
    Console.WriteLine($"  Verdict:  {result.Status}");
    if (result.RuntimeMs is not null) Console.WriteLine($"  Runtime:  {result.RuntimeMs} ms");
    if (result.MemoryKb is not null) Console.WriteLine($"  Memory:   {result.MemoryKb / 1024} MB");
    if (result.FailedCaseOrdinal is not null) Console.WriteLine($"  Case:     #{result.FailedCaseOrdinal}");

    if (!string.IsNullOrWhiteSpace(result.StderrExcerpt))
    {
        Console.WriteLine();
        Console.WriteLine(result.StderrExcerpt);
    }

    Console.WriteLine();
    return result.Status == SubmissionStatus.Accepted ? 0 : 1;
}

static string? ArgumentValue(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
