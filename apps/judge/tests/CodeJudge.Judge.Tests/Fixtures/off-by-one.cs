// Returns the indices one too high. Compiles, runs, produces the wrong answer.
public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        var seen = new System.Collections.Generic.Dictionary<int, int>();
        for (var i = 0; i < nums.Length; i++)
        {
            if (seen.TryGetValue(target - nums[i], out var j))
            {
                return new[] { j + 1, i + 1 };
            }
            seen[nums[i]] = i;
        }
        return new int[0];
    }
}
