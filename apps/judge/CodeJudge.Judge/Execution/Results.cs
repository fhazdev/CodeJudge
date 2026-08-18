using CodeJudge.Domain.Enums;

namespace CodeJudge.Judge.Execution;

public sealed record CompilationResult(
    bool Success,
    byte[]? Assembly,
    string? Diagnostics)
{
    public static CompilationResult Ok(byte[] assembly) => new(true, assembly, null);
    public static CompilationResult Failed(string diagnostics) => new(false, null, diagnostics);
}

/// <summary>Raw outcome of one child process run, before it is turned into a verdict.</summary>
public sealed record CaseExecution(
    string StandardOutput,
    string StandardError,
    int ExitCode,
    long ElapsedMs,
    long PeakWorkingSetBytes,
    bool TimedOut,
    bool MemoryExceeded);

public sealed record JudgeResult(
    SubmissionStatus Status,
    int? RuntimeMs = null,
    int? MemoryKb = null,
    int? FailedCaseOrdinal = null,
    string? StderrExcerpt = null);
