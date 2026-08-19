using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CodeJudge.Application.Abstractions;
using CodeJudge.Infrastructure.Persistence;
using CodeJudge.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CodeJudge.Api.IntegrationTests;

/// <summary>
/// Boots the real API against a real Postgres in a container.
///
/// Authentication is the one thing stubbed. Validating a genuine Entra token would mean
/// acquiring one, which needs a real interactive sign-in and turns a unit-speed test suite
/// into something nobody runs. What is under test here is routing, authorization,
/// serialization and data access; token validation itself is Microsoft.Identity.Web's job
/// and is verified once, by hand, against a real account.
/// </summary>
public sealed class CodeJudgeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
    public const string TestObjectId = "00000000-1111-2222-3333-444444444444";

    // Same image as docker-compose, so tests and local development agree on the engine.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("codejudge")
        .WithUsername("codejudge")
        .WithPassword("testing")
        .Build();

    /// <summary>Ids handed to the queue, in order, across the whole suite.</summary>
    public RecordingSubmissionQueue Queue { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodeJudgeDbContext>();
        await DatabaseSeeder.MigrateAndSeedAsync(db);
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>A client whose requests arrive authenticated.</summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Test");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            // Replace the DbContext registration with one pointed at the container.
            services.RemoveAll<DbContextOptions<CodeJudgeDbContext>>();
            services.RemoveAll<CodeJudgeDbContext>();

            services.AddDbContext<CodeJudgeDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Swap Entra for a handler that authenticates anything presenting the "Test"
            // scheme. Everything downstream (RequiredScope, claim reading, user
            // provisioning) still runs exactly as it does in production.
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // A fake queue rather than Azurite. These tests are about the HTTP contract:
            // status codes, the Location header, ownership scoping. Standing up a storage
            // emulator to prove those would make the suite slower and no more truthful.
            // The real queue round trip is covered end to end by running the worker.
            services.RemoveAll<ISubmissionQueue>();
            services.AddSingleton<ISubmissionQueue>(Queue);

            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        });
    }
}

public sealed class RecordingSubmissionQueue : ISubmissionQueue
{
    private readonly ConcurrentQueue<Guid> _enqueued = new();

    public IReadOnlyCollection<Guid> Enqueued => _enqueued;

    public Task EnqueueAsync(Guid submissionId, CancellationToken ct = default)
    {
        _enqueued.Enqueue(submissionId);
        return Task.CompletedTask;
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // No header means anonymous, which is what lets the 401 tests be meaningful.
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // A personal Microsoft account's claim shape: fixed MSA tenant, an oid, the scope
        // the SPA requested, and no email guarantee.
        Claim[] claims =
        [
            new("tid", CodeJudgeApiFactory.TestTenantId),
            new("oid", CodeJudgeApiFactory.TestObjectId),
            new("scp", "access_as_user"),
            new("name", "Test User"),
            new("preferred_username", "test@example.com")
        ];

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(
            AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
