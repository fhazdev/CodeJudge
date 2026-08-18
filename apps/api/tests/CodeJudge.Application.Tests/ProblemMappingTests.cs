using CodeJudge.Application.Problems;
using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;

namespace CodeJudge.Application.Tests;

/// <summary>
/// These look trivial, and they are the ones most worth having. A careless object mapper
/// added later is exactly how hidden test cases end up in an API response, and nothing
/// about that failure is loud: the endpoint keeps returning 200 and the problem page keeps
/// rendering. Only a test notices.
/// </summary>
public sealed class ProblemMappingTests
{
    private static Problem BuildProblem() => new()
    {
        Id = Guid.CreateVersion7(),
        Slug = "two-sum",
        Title = "Two Sum",
        Difficulty = Difficulty.Easy,
        StatementMd = "Given an array…",
        ConstraintsMd = "2 <= n",
        StarterCode = "public class Solution { }",
        HarnessCode = "internal static class Harness { static void Main() { } }",
        TimeLimitMs = 2_000,
        MemoryLimitKb = 262_144,
        TestCases =
        [
            new TestCase { Id = Guid.CreateVersion7(), Ordinal = 1, IsHidden = false, Input = "[2,7]\n9",  ExpectedOutput = "[0,1]" },
            new TestCase { Id = Guid.CreateVersion7(), Ordinal = 2, IsHidden = true,  Input = "[3,3]\n6",  ExpectedOutput = "[0,1]" },
            new TestCase { Id = Guid.CreateVersion7(), Ordinal = 3, IsHidden = true,  Input = "[-1,-2]\n-3", ExpectedOutput = "[0,1]" }
        ]
    };

    [Fact]
    public void DetailNeverCarriesHarnessCode()
    {
        var problem = BuildProblem();

        var dto = problem.ToDetail();

        // Not just "the property is absent": serialize the whole DTO and prove the harness
        // text appears nowhere in it. A future field that happens to include it would fail.
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        json.ShouldNotContain("Harness");
        json.ShouldNotContain("Main");
    }

    [Fact]
    public void DetailExposesOnlyVisibleTestCases()
    {
        var problem = BuildProblem();

        var dto = problem.ToDetail();

        dto.Examples.Count.ShouldBe(1);
        dto.Examples[0].Ordinal.ShouldBe(1);

        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        json.ShouldNotContain("[3,3]");
        json.ShouldNotContain("[-1,-2]");
    }

    [Fact]
    public void DetailKeepsTheFieldsTheEditorNeeds()
    {
        var dto = BuildProblem().ToDetail();

        dto.Slug.ShouldBe("two-sum");
        dto.StarterCode.ShouldBe("public class Solution { }");
        dto.TimeLimitMs.ShouldBe(2_000);
    }

    [Fact]
    public void SummaryIsJustTheListingFields()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(BuildProblem().ToSummary());

        json.ShouldNotContain("StarterCode");
        json.ShouldNotContain("Harness");
        json.ShouldContain("two-sum");
    }
}
