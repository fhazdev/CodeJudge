using CodeJudge.Judge.Execution;

namespace CodeJudge.Judge.Tests;

/// <summary>
/// Forgiving about presentation, strict about content. Every one of these is a real thing
/// a submission does, and getting the line wrong in either direction produces a verdict
/// the user will not believe.
/// </summary>
public sealed class OutputComparerTests
{
    [Theory]
    [InlineData("[0,1]", "[0,1]")]
    [InlineData("[0,1]", "[0,1]\n")]
    [InlineData("[0,1]", "[0,1]\r\n")]
    [InlineData("[0,1]", "[0,1]   ")]
    [InlineData("[0,1]", "[0,1]\n\n\n")]
    [InlineData("true", "true\r\n")]
    [InlineData("1\n2", "1\r\n2\r\n")]
    [InlineData("", "")]
    [InlineData("", "\n")]
    public void PresentationDifferencesMatch(string expected, string actual) =>
        OutputComparer.Matches(expected, actual).ShouldBeTrue();

    [Theory]
    [InlineData("[0,1]", "[0, 1]")]      // Whitespace inside the answer is part of it.
    [InlineData("[0,1]", "[1,0]")]       // Order matters.
    [InlineData("true", "True")]         // Case matters.
    [InlineData("1\n2", "2\n1")]
    [InlineData("[0,1]", "")]
    [InlineData("[0,1]", "  [0,1]")]     // Leading whitespace is not trimmed.
    public void ContentDifferencesDoNotMatch(string expected, string actual) =>
        OutputComparer.Matches(expected, actual).ShouldBeFalse();

    [Fact]
    public void NormalizeCollapsesLineEndingsToNewline() =>
        OutputComparer.Normalize("a\r\nb\rc\n").ShouldBe("a\nb\nc");
}
