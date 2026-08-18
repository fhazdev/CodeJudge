using CodeJudge.Domain.Entities;
using CodeJudge.Domain.Enums;

namespace CodeJudge.Infrastructure.Persistence.Seed;

/// <summary>
/// Seed problems, expressed as code rather than the JSON the build plan first sketched.
/// Every field here except the metadata is multi-line C# or Markdown, and raw string
/// literals carry that with no escaping at all, which JSON cannot. Ids are fixed literals
/// so re-seeding is idempotent.
/// </summary>
public static class SeedData
{
    public static IReadOnlyList<Problem> Problems() =>
    [
        TwoSum(),
        ValidParentheses(),
        ReverseLinkedList()
    ];

    private static Problem TwoSum()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        return new Problem
        {
            Id = id,
            Slug = "two-sum",
            Title = "Two Sum",
            Difficulty = Difficulty.Easy,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StatementMd =
                """
                Given an array of integers `nums` and an integer `target`, return the
                indices of the two numbers such that they add up to `target`.

                You may assume that each input has **exactly one solution**, and you may
                not use the same element twice. Return the indices in ascending order.
                """,
            ConstraintsMd =
                """
                - `2 <= nums.Length <= 10^4`
                - `-10^9 <= nums[i] <= 10^9`
                - Exactly one valid answer exists.
                """,
            StarterCode =
                """
                public class Solution
                {
                    public int[] TwoSum(int[] nums, int target)
                    {
                        // Return the indices of the two numbers adding up to target.
                        return new int[0];
                    }
                }
                """,
            HarnessCode =
                """
                using System;
                using System.Text.Json;

                internal static class Harness
                {
                    private static void Main()
                    {
                        var nums = JsonSerializer.Deserialize<int[]>(Console.ReadLine());
                        var target = int.Parse(Console.ReadLine());
                        var result = new Solution().TwoSum(nums, target);
                        Console.WriteLine(JsonSerializer.Serialize(result));
                    }
                }
                """,
            TestCases =
            [
                new TestCase { Id = Guid.Parse("11111111-0000-0000-0000-000000000001"), ProblemId = id, Ordinal = 1, IsHidden = false, Input = "[2,7,11,15]\n9",      ExpectedOutput = "[0,1]" },
                new TestCase { Id = Guid.Parse("11111111-0000-0000-0000-000000000002"), ProblemId = id, Ordinal = 2, IsHidden = false, Input = "[3,2,4]\n6",          ExpectedOutput = "[1,2]" },
                new TestCase { Id = Guid.Parse("11111111-0000-0000-0000-000000000003"), ProblemId = id, Ordinal = 3, IsHidden = true,  Input = "[3,3]\n6",            ExpectedOutput = "[0,1]" },
                new TestCase { Id = Guid.Parse("11111111-0000-0000-0000-000000000004"), ProblemId = id, Ordinal = 4, IsHidden = true,  Input = "[-1,-2,-3,-4,-5]\n-8", ExpectedOutput = "[2,4]" }
            ]
        };
    }

    private static Problem ValidParentheses()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        return new Problem
        {
            Id = id,
            Slug = "valid-parentheses",
            Title = "Valid Parentheses",
            Difficulty = Difficulty.Easy,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StatementMd =
                """
                Given a string `s` containing just the characters `(`, `)`, `{`, `}`,
                `[` and `]`, determine whether the input string is valid.

                A string is valid when open brackets are closed by the same type of
                bracket, and open brackets are closed in the correct order.
                """,
            ConstraintsMd =
                """
                - `1 <= s.Length <= 10^4`
                - `s` consists of bracket characters only.
                """,
            StarterCode =
                """
                public class Solution
                {
                    public bool IsValid(string s)
                    {
                        return false;
                    }
                }
                """,
            HarnessCode =
                """
                using System;

                internal static class Harness
                {
                    private static void Main()
                    {
                        var s = Console.ReadLine();
                        if (s == null) s = string.Empty;
                        Console.WriteLine(new Solution().IsValid(s) ? "true" : "false");
                    }
                }
                """,
            TestCases =
            [
                new TestCase { Id = Guid.Parse("22222222-0000-0000-0000-000000000001"), ProblemId = id, Ordinal = 1, IsHidden = false, Input = "()",     ExpectedOutput = "true"  },
                new TestCase { Id = Guid.Parse("22222222-0000-0000-0000-000000000002"), ProblemId = id, Ordinal = 2, IsHidden = false, Input = "()[]{}", ExpectedOutput = "true"  },
                new TestCase { Id = Guid.Parse("22222222-0000-0000-0000-000000000003"), ProblemId = id, Ordinal = 3, IsHidden = true,  Input = "(]",     ExpectedOutput = "false" },
                new TestCase { Id = Guid.Parse("22222222-0000-0000-0000-000000000004"), ProblemId = id, Ordinal = 4, IsHidden = true,  Input = "([)]",   ExpectedOutput = "false" },
                new TestCase { Id = Guid.Parse("22222222-0000-0000-0000-000000000005"), ProblemId = id, Ordinal = 5, IsHidden = true,  Input = "{[]}",   ExpectedOutput = "true"  }
            ]
        };
    }

    private static Problem ReverseLinkedList()
    {
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        return new Problem
        {
            Id = id,
            Slug = "reverse-linked-list",
            Title = "Reverse Linked List",
            Difficulty = Difficulty.Easy,
            CreatedAt = DateTimeOffset.UnixEpoch,
            StatementMd =
                """
                Given the `head` of a singly linked list, reverse the list and return
                the reversed list's head.

                The `ListNode` type is provided for you.
                """,
            ConstraintsMd =
                """
                - The list has between `0` and `5000` nodes.
                - `-5000 <= Node.val <= 5000`
                """,
            StarterCode =
                """
                public class Solution
                {
                    public ListNode ReverseList(ListNode head)
                    {
                        return null;
                    }
                }
                """,
            // This is the case the harness model earns its keep on: the shared ListNode
            // type is declared here, so the submission can reference a type it never
            // defines, exactly as it would on LeetCode.
            HarnessCode =
                """
                using System;
                using System.Collections.Generic;
                using System.Text.Json;

                public class ListNode
                {
                    public int val;
                    public ListNode next;

                    public ListNode(int val = 0, ListNode next = null)
                    {
                        this.val = val;
                        this.next = next;
                    }
                }

                internal static class Harness
                {
                    private static void Main()
                    {
                        var values = JsonSerializer.Deserialize<int[]>(Console.ReadLine());

                        ListNode head = null;
                        for (var i = values.Length - 1; i >= 0; i--)
                        {
                            head = new ListNode(values[i], head);
                        }

                        var reversed = new Solution().ReverseList(head);

                        var output = new List<int>();
                        for (var node = reversed; node != null; node = node.next)
                        {
                            output.Add(node.val);
                        }

                        Console.WriteLine(JsonSerializer.Serialize(output));
                    }
                }
                """,
            TestCases =
            [
                new TestCase { Id = Guid.Parse("33333333-0000-0000-0000-000000000001"), ProblemId = id, Ordinal = 1, IsHidden = false, Input = "[1,2,3,4,5]", ExpectedOutput = "[5,4,3,2,1]" },
                new TestCase { Id = Guid.Parse("33333333-0000-0000-0000-000000000002"), ProblemId = id, Ordinal = 2, IsHidden = false, Input = "[1,2]",       ExpectedOutput = "[2,1]"       },
                new TestCase { Id = Guid.Parse("33333333-0000-0000-0000-000000000003"), ProblemId = id, Ordinal = 3, IsHidden = true,  Input = "[]",          ExpectedOutput = "[]"          }
            ]
        };
    }
}
