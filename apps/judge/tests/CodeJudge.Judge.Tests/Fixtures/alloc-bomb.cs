// Allocates until the GC heap hard limit refuses. Retains every block so nothing
// can be collected.
public class Solution
{
    public int[] TwoSum(int[] nums, int target)
    {
        var held = new System.Collections.Generic.List<byte[]>();
        while (true)
        {
            held.Add(new byte[16 * 1024 * 1024]);
        }
    }
}
