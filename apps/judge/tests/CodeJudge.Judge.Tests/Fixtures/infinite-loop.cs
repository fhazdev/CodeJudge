// The case that cannot be caught in-process: no CancellationToken is ever observed,
// so the only way out is for a parent to kill this process.
public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        while (true)
        {
        }
    }
}
