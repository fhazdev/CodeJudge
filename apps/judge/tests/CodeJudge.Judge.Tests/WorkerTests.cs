using Azure.Storage.Queues;
using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;
using CodeJudge.Infrastructure.Messaging;
using CodeJudge.Infrastructure.Persistence;
using CodeJudge.Infrastructure.Persistence.Seed;
using CodeJudge.Judge.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

namespace CodeJudge.Judge.Tests;

/// <summary>
/// The worker against a real Postgres and a real queue.
///
/// Its interesting behaviour is all in what it refuses to do: judge a submission twice,
/// retry a poison message forever, or delete a message before the verdict is durable.
/// None of that is reachable through the verdict matrix, and all of it fails silently in
/// production if it is wrong.
///
/// This is the only class in the project that needs Docker.
/// </summary>
public sealed class WorkerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("codejudge").WithUsername("codejudge").WithPassword("testing").Build();

    // --skipApiVersionCheck for the same reason docker-compose.yml passes it: the Azure
    // SDK negotiates a newer REST API version than the emulator recognises, and Azurite
    // rejects the request outright rather than falling back.
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(
            "mcr.microsoft.com/azure-storage/azurite:latest")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public QueueClient QueueClient { get; private set; } = null!;

    private string _postgresConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync());

        _postgresConnectionString = _postgres.GetConnectionString();

        QueueClient = new QueueClient(_azurite.GetConnectionString(), "submissions");
        await QueueClient.CreateIfNotExistsAsync();

        await using var db = CreateDbContext();
        await DatabaseSeeder.MigrateAndSeedAsync(db);
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    public CodeJudgeDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CodeJudgeDbContext>()
            .UseNpgsql(_postgresConnectionString)
            .Options);

    public SubmissionWorker CreateWorker(CodeJudgeDbContext db) =>
        new(
            new SubmissionQueueReader(QueueClient, NullLogger<SubmissionQueueReader>.Instance),
            db,
            JudgeFixture.Create(),
            TimeProvider.System,
            NullLogger<SubmissionWorker>.Instance);
}

public sealed class WorkerTests(WorkerFixture fixture) : IClassFixture<WorkerFixture>
{
    private const string CorrectTwoSum = @"
public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        for (var i = 0; i < nums.Length; i++)
            for (var j = i + 1; j < nums.Length; j++)
                if (nums[i] + nums[j] == target) return new[] { i, j };
        return new int[0];
    }
}";

    private async Task<Guid> SeedSubmissionAsync(
        string code, SubmissionStatus status = SubmissionStatus.Queued)
    {
        await using var db = fixture.CreateDbContext();

        var problem = await db.Problems.FirstAsync(p => p.Slug == "two-sum");

        var user = await db.Users.FirstOrDefaultAsync();
        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                EntraTenantId = "test",
                EntraObjectId = "worker-tests",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(user);
        }

        var submission = new Submission
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ProblemId = problem.Id,
            Code = code,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Submissions.Add(submission);
        await db.SaveChangesAsync();

        return submission.Id;
    }

    private async Task EnqueueAsync(Guid submissionId) =>
        await fixture.QueueClient.SendMessageAsync(
            new SubmissionQueueMessage(submissionId, DateTimeOffset.UtcNow).Serialize());

    private async Task<int> QueueDepthAsync() =>
        (await fixture.QueueClient.GetPropertiesAsync()).Value.ApproximateMessagesCount;

    [Fact]
    public async Task EmptyQueueIsNoWork()
    {
        await using var db = fixture.CreateDbContext();

        var outcome = await fixture.CreateWorker(db)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        outcome.ShouldBe(SubmissionWorker.Outcome.NoWork);
    }

    [Fact]
    public async Task JudgesASubmissionAndDeletesItsMessage()
    {
        var id = await SeedSubmissionAsync(CorrectTwoSum);
        await EnqueueAsync(id);

        await using (var db = fixture.CreateDbContext())
        {
            var outcome = await fixture.CreateWorker(db)
                .ProcessNextAsync(TestContext.Current.CancellationToken);

            outcome.ShouldBe(SubmissionWorker.Outcome.Judged);
        }

        await using var verify = fixture.CreateDbContext();
        var submission = await verify.Submissions.FirstAsync(
            s => s.Id == id, TestContext.Current.CancellationToken);

        submission.Status.ShouldBe(SubmissionStatus.Accepted);
        submission.CompletedAt.ShouldNotBeNull();

        // Leave the message behind and KEDA sees depth forever, re-triggering a job that
        // has nothing left to do.
        (await QueueDepthAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AlreadyJudgedSubmissionIsNotJudgedAgain()
    {
        // Reachable in production: the verdict was written but the delete did not land,
        // so the message became visible again.
        var id = await SeedSubmissionAsync(CorrectTwoSum, SubmissionStatus.WrongAnswer);
        await EnqueueAsync(id);

        await using (var db = fixture.CreateDbContext())
        {
            var outcome = await fixture.CreateWorker(db)
                .ProcessNextAsync(TestContext.Current.CancellationToken);

            outcome.ShouldBe(SubmissionWorker.Outcome.Discarded);
        }

        await using var verify = fixture.CreateDbContext();
        var submission = await verify.Submissions.FirstAsync(
            s => s.Id == id, TestContext.Current.CancellationToken);

        // The earlier verdict survives. Re-judging could overwrite what the user is
        // already looking at.
        submission.Status.ShouldBe(SubmissionStatus.WrongAnswer);
        (await QueueDepthAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task MessageForADeletedSubmissionIsDiscarded()
    {
        await EnqueueAsync(Guid.CreateVersion7());

        await using var db = fixture.CreateDbContext();
        var outcome = await fixture.CreateWorker(db)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        outcome.ShouldBe(SubmissionWorker.Outcome.Discarded);
        (await QueueDepthAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UnparseableMessageIsDiscardedRatherThanRetriedForever()
    {
        await fixture.QueueClient.SendMessageAsync(
            "this is not json", cancellationToken: TestContext.Current.CancellationToken);

        await using var db = fixture.CreateDbContext();
        var outcome = await fixture.CreateWorker(db)
            .ProcessNextAsync(TestContext.Current.CancellationToken);

        outcome.ShouldBe(SubmissionWorker.Outcome.NoWork);
        (await QueueDepthAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CompileErrorIsARealVerdictNotAWorkerFailure()
    {
        var id = await SeedSubmissionAsync("public class Solution { this is not C# }");
        await EnqueueAsync(id);

        await using (var db = fixture.CreateDbContext())
        {
            await fixture.CreateWorker(db).ProcessNextAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = fixture.CreateDbContext();
        var submission = await verify.Submissions.FirstAsync(
            s => s.Id == id, TestContext.Current.CancellationToken);

        submission.Status.ShouldBe(SubmissionStatus.CompileError);
        submission.StderrExcerpt.ShouldNotBeNullOrWhiteSpace();

        (await QueueDepthAsync()).ShouldBe(0);
    }
}
