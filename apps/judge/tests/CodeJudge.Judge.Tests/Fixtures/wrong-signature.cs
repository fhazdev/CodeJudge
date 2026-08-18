// Compiles on its own, but the harness calls TwoSum(int[], int) and finds nothing.
// The resulting errors all land in the harness, which is what triggers the
// "your signature does not match" hint.
public class Solution
{
    public int[] TwoSums(string nums, string target)
    {
        return new int[0];
    }
}
