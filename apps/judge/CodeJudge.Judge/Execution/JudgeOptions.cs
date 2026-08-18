namespace CodeJudge.Judge.Execution;

/// <summary>
/// The middle layers of the timeout budget in section 5 of the build plan. The innermost
/// layer (per test case) lives on the Problem, because a legitimately heavier problem may
/// need a longer one. The outer two layers (job replicaTimeout, queue visibility) are
/// infrastructure configuration and are not represented here.
/// </summary>
public sealed class JudgeOptions
{
    /// <summary>
    /// Roslyn is not linear in input size, and the input is untrusted by definition.
    /// An uncapped compile is a denial of service against our own free tier.
    /// </summary>
    public TimeSpan CompileTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Wall clock for the whole submission. Guards the case that no per-case limit catches:
    /// twenty cases at 1,999 ms each all pass, and the run still takes 40 seconds.
    /// </summary>
    public TimeSpan SubmissionBudget { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Compiler diagnostics and captured stderr are truncated to this length.</summary>
    public int MaxStderrLength { get; init; } = 4_000;

    /// <summary>
    /// Slack above the problem's memory limit before the parent kills the child outright.
    /// The GC heap hard limit should produce a clean OutOfMemoryException well before this;
    /// this backstop only catches native allocation, which the GC limit does not govern.
    /// A .NET process costs roughly 30 to 40 MB before running any user code, so this has
    /// to be generous or every submission trips it.
    /// </summary>
    public long MemoryHeadroomBytes { get; init; } = 128L * 1024 * 1024;

    /// <summary>How often the parent samples the child's working set.</summary>
    public TimeSpan MemorySampleInterval { get; init; } = TimeSpan.FromMilliseconds(25);
}
