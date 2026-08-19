using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeJudge.Application.Submissions;
using CodeJudge.Domain.Enums;

namespace CodeJudge.Api.IntegrationTests;

[Collection(nameof(ApiCollection))]
public sealed class SubmissionsEndpointTests(CodeJudgeApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string ValidSolution = "public class Solution { public int[] TwoSum(int[] n, int t) => new int[0]; }";

    private static object Body(string slug, string code, string language = "csharp") =>
        new { problemSlug = slug, language, code };

    [Fact]
    public async Task CreateRequiresAuthentication()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/submissions", Body("two-sum", ValidSolution), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// 202, not 201: the row exists but the work it represents has not happened. The
    /// Location header is what the SPA polls, rather than building the URL itself.
    /// </summary>
    [Fact]
    public async Task CreateReturns202WithAPollableLocation()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions", Body("two-sum", ValidSolution), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();

        var created = await response.Content.ReadFromJsonAsync<SubmissionDto>(
            Json, TestContext.Current.CancellationToken);

        created.ShouldNotBeNull();
        created.Status.ShouldBe(SubmissionStatus.Queued);
        created.IsTerminal.ShouldBeFalse();

        // The Location header must actually resolve, not merely be present.
        var polled = await factory.CreateAuthenticatedClient()
            .GetAsync(response.Headers.Location, TestContext.Current.CancellationToken);

        polled.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubmittingToAnUnknownProblemIs404()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions", Body("no-such-problem", ValidSolution),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EmptyCodeIsRejected()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions", Body("two-sum", "   "), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OversizedCodeIsRejected()
    {
        var huge = new string('x', CreateSubmissionCommandValidator.MaxCodeLength + 1);

        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions", Body("two-sum", huge), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// An unsupported language would enqueue work the judge cannot do, leaving the
    /// submission Queued forever with nothing coming to resolve it.
    /// </summary>
    [Fact]
    public async Task UnsupportedLanguageIsRejected()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions", Body("two-sum", "print('hi')", language: "python"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownSubmissionIs404()
    {
        var response = await factory.CreateAuthenticatedClient()
            .GetAsync($"/api/submissions/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListReturnsTheCallersSubmissions()
    {
        var client = factory.CreateAuthenticatedClient();

        await client.PostAsJsonAsync("/api/submissions", Body("two-sum", ValidSolution),
            TestContext.Current.CancellationToken);

        var page = await client.GetFromJsonAsync<Application.Common.PagedResult<SubmissionSummaryDto>>(
            "/api/submissions?problemSlug=two-sum", Json, TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.TotalCount.ShouldBeGreaterThan(0);
        page.Items.ShouldAllBe(s => s.ProblemSlug == "two-sum");
    }

    /// <summary>Filtering by a problem that does not exist is an empty page, not an error.</summary>
    [Fact]
    public async Task ListFilteredByUnknownProblemIsEmpty()
    {
        var page = await factory.CreateAuthenticatedClient()
            .GetFromJsonAsync<Application.Common.PagedResult<SubmissionSummaryDto>>(
                "/api/submissions?problemSlug=no-such-problem", Json,
                TestContext.Current.CancellationToken);

        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }
}

[Collection(nameof(ApiCollection))]
public sealed class SubmissionEnqueueTests(CodeJudgeApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Exactly once. Enqueueing twice would have the judge run the same submission
    /// concurrently with itself; enqueueing zero times leaves the row Queued forever with
    /// nothing coming to resolve it.
    /// </summary>
    [Fact]
    public async Task CreatingASubmissionEnqueuesItExactlyOnce()
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions",
            new { problemSlug = "valid-parentheses", language = "csharp", code = "public class Solution { public bool IsValid(string s) => true; }" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var created = await response.Content.ReadFromJsonAsync<SubmissionDto>(
            Json, TestContext.Current.CancellationToken);

        created.ShouldNotBeNull();
        factory.Queue.Enqueued.Count(id => id == created.Id).ShouldBe(1);
    }

    /// <summary>A rejected submission must not reach the queue at all.</summary>
    [Fact]
    public async Task RejectedSubmissionsAreNeverEnqueued()
    {
        var before = factory.Queue.Enqueued.Count;

        await factory.CreateAuthenticatedClient().PostAsJsonAsync(
            "/api/submissions",
            new { problemSlug = "two-sum", language = "python", code = "print(1)" },
            TestContext.Current.CancellationToken);

        factory.Queue.Enqueued.Count.ShouldBe(before);
    }
}
