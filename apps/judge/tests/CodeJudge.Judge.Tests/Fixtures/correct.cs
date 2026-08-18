public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        var seen = new System.Collections.Generic.Dictionary<int, int>();
        for (var i = 0; i < nums.Length; i++)
        {
            if (seen.TryGetValue(target - nums[i], out var j))
            {
                return new[] { j, i };
            }
            seen[nums[i]] = i;
        }
        return new int[0];
    }
}
