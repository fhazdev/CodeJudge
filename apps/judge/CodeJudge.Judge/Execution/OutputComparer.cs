namespace CodeJudge.Judge.Execution;

/// <summary>
/// Compares a submission's stdout against the expected output.
///
/// Forgiving about presentation, strict about content. Nobody should get WrongAnswer for
/// a trailing newline or for running on Windows, but "[0,1]" and "[0, 1]" really are
/// different answers and stay different.
/// </summary>
public static class OutputComparer
{
    public static bool Matches(string expected, string actual) =>
        Normalize(expected) == Normalize(actual);

    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var normalized = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            normalized.Add(line.TrimEnd());
        }

        // Trailing blank lines are an artifact of Console.WriteLine, not an answer.
        while (normalized.Count > 0 && normalized[^1].Length == 0)
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        return string.Join('\n', normalized);
    }
}
