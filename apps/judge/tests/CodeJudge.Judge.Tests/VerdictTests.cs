using CodeJudge.Domain.Enums;

namespace CodeJudge.Judge.Tests;

/// <summary>
/// The highest-value suite in the project. The judge is the component that can be subtly
/// and silently wrong, and every one of these rows is a verdict a user will eventually see.
///
/// The TimeLimitExceeded and MemoryLimitExceeded rows genuinely spawn a child process and
/// kill it, so they take a second or two each. That cost is the point: these are the tests
/// that could not exist at all under in-process Roslyn scripting.
/// </summary>
public sealed class VerdictTests(JudgeFixture fixture) : IClassFixture<JudgeFixture>
{
    [Theory]
    [InlineData("correct.cs", SubmissionStatus.Accepted)]
    [InlineData("off-by-one.cs", SubmissionStatus.WrongAnswer)]
    [InlineData("infinite-loop.cs", SubmissionStatus.TimeLimitExceeded)]
    [InlineData("missing-brace.cs", SubmissionStatus.CompileError)]
    [InlineData("wrong-signature.cs", SubmissionStatus.CompileError)]
    [InlineData("null-deref.cs", SubmissionStatus.RuntimeError)]
    public async Task ProducesExpectedVerdict(string fixtureFile, SubmissionStatus expected)
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read(fixtureFile),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(expected);
    }

    [Fact]
    public async Task AllocationBombIsMemoryLimitExceeded()
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read("alloc-bomb.cs"),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.MemoryLimitExceeded);
    }

    [Fact]
    public async Task AcceptedSubmissionReportsRuntimeAndFailsNoCase()
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read("correct.cs"),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.Accepted);
        result.FailedCaseOrdinal.ShouldBeNull();
        result.RuntimeMs.ShouldNotBeNull();
        result.RuntimeMs!.Value.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task WrongAnswerReportsTheCaseThatFailed()
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read("off-by-one.cs"),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.WrongAnswer);

        // Short-circuits on the first failure, and case 1 already fails.
        result.FailedCaseOrdinal.ShouldBe(1);
    }

    [Fact]
    public async Task HiddenCaseFailureDoesNotLeakItsInput()
    {
        // Ordinals 3 and 4 of Two Sum are hidden. A submission that only mishandles
        // duplicate values passes the visible cases and fails a hidden one.
        const string passesVisibleCasesOnly =
            """
            public class Solution
            {
                public int[] TwoSum(int[] nums, int target)
                {
                    for (var i = 0; i < nums.Length; i++)
                    {
                        for (var j = i + 1; j < nums.Length; j++)
                        {
                            // Refuses to pair equal values, so [3,3] finds nothing.
                            if (nums[i] != nums[j] && nums[i] + nums[j] == target)
                            {
                                return new[] { i, j };
                            }
                        }
                    }
                    return new int[0];
                }
            }
            """;

        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(), passesVisibleCasesOnly, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.WrongAnswer);
        result.FailedCaseOrdinal.ShouldBe(3);

        // The ordinal is public; the input is not.
        result.StderrExcerpt.ShouldNotBeNull();
        result.StderrExcerpt.ShouldNotContain("[3,3]");
        result.StderrExcerpt.ShouldContain("hidden");
    }

    [Fact]
    public async Task VisibleCaseFailureShowsExpectedAndActual()
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSum(),
            Fixtures.Read("off-by-one.cs"),
            TestContext.Current.CancellationToken);

        result.StderrExcerpt.ShouldNotBeNull();
        result.StderrExcerpt.ShouldContain("Expected: [0,1]");
        result.StderrExcerpt.ShouldContain("Actual:   [1,2]");
    }

    [Fact]
    public async Task TrailingWhitespaceAndBlankLinesDoNotFailASubmission()
    {
        var result = await fixture.Judge.JudgeAsync(
            TestProblems.TwoSumFirstCaseOnly(),
            Fixtures.Read("trailing-whitespace.cs"),
            TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.Accepted);
    }

    [Theory]
    [InlineData("valid-parentheses")]
    [InlineData("reverse-linked-list")]
    public async Task SeededReferenceSolutionsAreAccepted(string slug)
    {
        var (problem, solution) = slug switch
        {
            "valid-parentheses" => (TestProblems.ValidParentheses(), ValidParenthesesSolution),
            _ => (TestProblems.ReverseLinkedList(), ReverseLinkedListSolution)
        };

        var result = await fixture.Judge.JudgeAsync(
            problem, solution, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SubmissionStatus.Accepted);
    }

    private const string ValidParenthesesSolution =
        """
        public class Solution
        {
            public bool IsValid(string s)
            {
                var stack = new System.Collections.Generic.Stack<char>();
                foreach (var c in s)
                {
                    if (c == '(' || c == '[' || c == '{')
                    {
                        stack.Push(c);
                        continue;
                    }

                    if (stack.Count == 0) return false;

                    var open = stack.Pop();
                    if (c == ')' && open != '(') return false;
                    if (c == ']' && open != '[') return false;
                    if (c == '}' && open != '{') return false;
                }

                return stack.Count == 0;
            }
        }
        """;

    // References ListNode, a type it never declares. The harness supplies it, which is
    // the whole argument for the harness model in section 4 of the build plan.
    private const string ReverseLinkedListSolution =
        """
        public class Solution
        {
            public ListNode ReverseList(ListNode head)
            {
                ListNode previous = null;
                while (head != null)
                {
                    var next = head.next;
                    head.next = previous;
                    previous = head;
                    head = next;
                }
                return previous;
            }
        }
        """;
}
