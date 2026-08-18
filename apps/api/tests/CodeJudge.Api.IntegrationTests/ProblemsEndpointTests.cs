using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeJudge.Application.Common;
using CodeJudge.Application.Problems;
using CodeJudge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CodeJudge.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class ProblemsEndpointTests(CodeJudgeApiFactory factory)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ListRequiresAuthentication()
    {
        var response = await factory.CreateClient()
            .GetAsync("/api/problems", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DetailRequiresAuthentication()
    {
        var response = await factory.CreateClient()
            .GetAsync("/api/problems/two-sum", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthIsAnonymous()
    {
        var response = await factory.CreateClient()
            .GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListReturnsSeededProblems()
    {
        var response = await factory.CreateAuthenticatedClient()
            .GetFromJsonAsync<PagedResult<ProblemSummaryDto>>(
                "/api/problems", Json, TestContext.Current.CancellationToken);

        response.ShouldNotBeNull();
        response.TotalCount.ShouldBe(3);
        response.Items.Select(p => p.Slug)
            .ShouldBe(["two-sum", "valid-parentheses", "reverse-linked-list"], ignoreOrder: true);
    }

    [Fact]
    public async Task DetailReturnsTheProblem()
    {
        var problem = await factory.CreateAuthenticatedClient()
            .GetFromJsonAsync<ProblemDetailDto>(
                "/api/problems/two-sum", Json, TestContext.Current.CancellationToken);

        problem.ShouldNotBeNull();
        problem.Title.ShouldBe("Two Sum");
        problem.StarterCode.ShouldContain("class Solution");
    }

    /// <summary>
    /// The same guarantee as the unit test, but asserted on the actual bytes on the wire,
    /// after real serialization through the real pipeline.
    /// </summary>
    [Fact]
    public async Task DetailResponseLeaksNeitherHarnessNorHiddenCases()
    {
        var body = await factory.CreateAuthenticatedClient()
            .GetStringAsync("/api/problems/two-sum", TestContext.Current.CancellationToken);

        body.ShouldNotContain("Harness");
        body.ShouldNotContain("JsonSerializer");

        // Ordinals 3 and 4 of Two Sum are hidden.
        body.ShouldNotContain("[3,3]");
        body.ShouldNotContain("[-1,-2,-3,-4,-5]");

        // The visible ones are supposed to be there.
        body.ShouldContain("[2,7,11,15]");
    }

    [Fact]
    public async Task UnknownSlugIs404()
    {
        var response = await factory.CreateAuthenticatedClient()
            .GetAsync("/api/problems/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidSlugIsRejectedAsValidationProblem()
    {
        var response = await factory.CreateAuthenticatedClient()
            .GetAsync("/api/problems/NOT_A_SLUG", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("validation errors");
    }

    [Fact]
    public async Task InvalidPagingIsRejected()
    {
        var response = await factory.CreateAuthenticatedClient()
            .GetAsync("/api/problems?page=0&pageSize=500", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// There is no sign-up step: the first authenticated request creates the row. The
    /// identity is the (tenant, object) pair, never the object id alone.
    /// </summary>
    [Fact]
    public async Task FirstAuthenticatedRequestProvisionsTheUser()
    {
        await factory.CreateAuthenticatedClient()
            .GetAsync("/api/problems", TestContext.Current.CancellationToken);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodeJudgeDbContext>();

        var user = await db.Users.SingleOrDefaultAsync(
            u => u.EntraTenantId == CodeJudgeApiFactory.TestTenantId
                 && u.EntraObjectId == CodeJudgeApiFactory.TestObjectId,
            TestContext.Current.CancellationToken);

        user.ShouldNotBeNull();
        user.DisplayName.ShouldBe("Test User");
    }

    [Fact]
    public async Task RepeatedRequestsDoNotCreateDuplicateUsers()
    {
        var client = factory.CreateAuthenticatedClient();
        await client.GetAsync("/api/problems", TestContext.Current.CancellationToken);
        await client.GetAsync("/api/problems", TestContext.Current.CancellationToken);
        await client.GetAsync("/api/problems", TestContext.Current.CancellationToken);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CodeJudgeDbContext>();

        var count = await db.Users.CountAsync(
            u => u.EntraObjectId == CodeJudgeApiFactory.TestObjectId,
            TestContext.Current.CancellationToken);

        count.ShouldBe(1);
    }
}

/// <summary>
/// One container for the whole suite. Per-class would start Postgres once per test class,
/// and the suite would spend most of its time waiting on Docker.
/// </summary>
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<CodeJudgeApiFactory>;
