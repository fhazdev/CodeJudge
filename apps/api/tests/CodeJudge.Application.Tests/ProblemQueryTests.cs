using CodeJudge.Application.Abstractions;
using CodeJudge.Application.Problems;
using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;
using NSubstitute;

namespace CodeJudge.Application.Tests;

public sealed class ListProblemsQueryHandlerTests
{
    private static Problem Stub(string slug) => new()
    {
        Slug = slug,
        Title = slug,
        Difficulty = Difficulty.Easy,
        StatementMd = "",
        StarterCode = "",
        HarnessCode = ""
    };

    [Fact]
    public async Task TranslatesPageAndSizeIntoSkipAndTake()
    {
        var problems = Substitute.For<IProblemRepository>();
        problems.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Stub("two-sum")]);
        problems.CountAsync(Arg.Any<CancellationToken>()).Returns(41);

        var handler = new ListProblemsQueryHandler(problems);

        await handler.Handle(new ListProblemsQuery(Page: 3, PageSize: 20), TestContext.Current.CancellationToken);

        // Page 3 of 20 starts at 40, not 60. Off-by-one here is the classic way a listing
        // silently skips a page of results.
        await problems.Received(1).ListAsync(40, 20, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportsPagingMetadata()
    {
        var problems = Substitute.For<IProblemRepository>();
        problems.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Stub("a"), Stub("b")]);
        problems.CountAsync(Arg.Any<CancellationToken>()).Returns(41);

        var result = await new ListProblemsQueryHandler(problems)
            .Handle(new ListProblemsQuery(Page: 1, PageSize: 20), TestContext.Current.CancellationToken);

        result.TotalCount.ShouldBe(41);
        result.TotalPages.ShouldBe(3);
        result.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task LastPageHasNoNextPage()
    {
        var problems = Substitute.For<IProblemRepository>();
        problems.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Stub("a")]);
        problems.CountAsync(Arg.Any<CancellationToken>()).Returns(41);

        var result = await new ListProblemsQueryHandler(problems)
            .Handle(new ListProblemsQuery(Page: 3, PageSize: 20), TestContext.Current.CancellationToken);

        result.HasNextPage.ShouldBeFalse();
    }
}

public sealed class GetProblemBySlugQueryHandlerTests
{
    [Fact]
    public async Task ReturnsNullWhenNoProblemMatches()
    {
        var problems = Substitute.For<IProblemRepository>();
        problems.GetBySlugAsync("nope", Arg.Any<CancellationToken>()).Returns((Problem?)null);

        var result = await new GetProblemBySlugQueryHandler(problems)
            .Handle(new GetProblemBySlugQuery("nope"), TestContext.Current.CancellationToken);

        // Null, not an exception. A mistyped URL is an ordinary event, and the controller
        // turns this into a 404.
        result.ShouldBeNull();
    }
}

public sealed class ProblemValidatorTests
{
    [Theory]
    [InlineData("two-sum", true)]
    [InlineData("reverse-linked-list", true)]
    [InlineData("a1", true)]
    [InlineData("Two-Sum", false)]
    [InlineData("two--sum", false)]
    [InlineData("-two-sum", false)]
    [InlineData("two_sum", false)]
    [InlineData("", false)]
    public void SlugPatternAcceptsOnlyKebabCase(string slug, bool expected) =>
        new GetProblemBySlugQueryValidator()
            .Validate(new GetProblemBySlugQuery(slug))
            .IsValid.ShouldBe(expected);

    [Theory]
    [InlineData(1, 20, true)]
    [InlineData(1, 100, true)]
    [InlineData(0, 20, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 101, false)]
    public void PagingBoundsAreEnforced(int page, int pageSize, bool expected) =>
        new ListProblemsQueryValidator()
            .Validate(new ListProblemsQuery(page, pageSize))
            .IsValid.ShouldBe(expected);
}
