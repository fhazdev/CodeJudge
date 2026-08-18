using CodeJudge.Domain.Entities;
using CodeJudge.Infrastructure.Persistence.Seed;
using CodeJudge.Judge.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeJudge.Judge.Tests;

/// <summary>
/// Builds a JudgeService with no database and no queue. That this is possible at all is a
/// property of the design: JudgeService takes a Problem and some code and returns a
/// verdict, so the riskiest component in the system is testable without infrastructure.
/// </summary>
public sealed class JudgeFixture
{
    public JudgeService Judge { get; } = Create();

    public static JudgeService Create(JudgeOptions? options = null)
    {
        options ??= new JudgeOptions();

        return new JudgeService(
            new CompilationService(options),
            new SandboxRunner(options),
            options,
            NullLogger<JudgeService>.Instance);
    }
}

public static class TestProblems
{
    public static Problem TwoSum() => Seeded("two-sum");

    /// <summary>
    /// Two Sum trimmed to its first case. For submissions that print a fixed answer,
    /// which would pass case 1 and then fail case 2 for reasons the test does not care
    /// about.
    /// </summary>
    public static Problem TwoSumFirstCaseOnly()
    {
        var problem = Seeded("two-sum");
        problem.TestCases = problem.TestCases.OrderBy(c => c.Ordinal).Take(1).ToList();
        return problem;
    }

    public static Problem ValidParentheses() => Seeded("valid-parentheses");

    public static Problem ReverseLinkedList() => Seeded("reverse-linked-list");

    private static Problem Seeded(string slug) =>
        SeedData.Problems().Single(p => p.Slug == slug);
}

public static class Fixtures
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>
    /// Submissions live as real .cs files rather than string literals so they stay
    /// readable and syntax-highlighted. Several of them do not compile on purpose, which
    /// is why the csproj excludes this folder from compilation.
    /// </summary>
    public static string Read(string fileName)
    {
        var path = Path.Combine(Root, fileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Missing test fixture '{fileName}'. Looked in {Root}.", path);
        }

        return File.ReadAllText(path);
    }
}
