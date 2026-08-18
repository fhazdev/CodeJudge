using CodeJudge.Domain.Enums;
using CodeJudge.Infrastructure.Persistence;
using CodeJudge.Infrastructure.Persistence.Seed;
using CodeJudge.Judge.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Phase 0 entry point: a local CLI, no queue and no Azure.
//
// The queue consumer loop that replaces this arrives in phase 2. Keeping the judge
// runnable from a terminal against docker compose is what lets the riskiest component be
// proven before any infrastructure exists.
//
//   dotnet run --project apps/judge/CodeJudge.Judge -- seed
//   dotnet run --project apps/judge/CodeJudge.Judge -- judge --problem two-sum --file sol.cs
//   dotnet run --project apps/judge/CodeJudge.Judge -- problems

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
        CodeJudge judge (phase 0 local CLI)

          seed                                   apply migrations and upsert seed problems
          problems                               list the seeded problems
          judge --problem <slug> --file <path>   judge a file against a problem

        Connection string comes from CODEJUDGE_CONNECTION, defaulting to the
        docker compose Postgres. Start it with: docker compose up -d
        """);
    return 1;
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
