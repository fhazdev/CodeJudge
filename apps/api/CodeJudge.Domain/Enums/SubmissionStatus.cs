namespace CodeJudge.Domain.Enums;

/// <summary>
/// Terminal and in-flight states of a submission. Persisted by name, not by ordinal,
/// so reordering this enum cannot silently rewrite history in the database.
/// </summary>
public enum SubmissionStatus
{
    /// <summary>Accepted by the API, message on the queue, not yet picked up.</summary>
    Queued,

    /// <summary>A judge execution has claimed it.</summary>
    Running,

    /// <summary>Every test case matched.</summary>
    Accepted,

    /// <summary>Compiled and ran, but produced the wrong output for some case.</summary>
    WrongAnswer,

    /// <summary>A single case, or the submission as a whole, exceeded its time budget.</summary>
    TimeLimitExceeded,

    /// <summary>The submitted code threw, or the process exited non-zero.</summary>
    RuntimeError,

    /// <summary>Roslyn reported errors, or compilation itself timed out.</summary>
    CompileError,

    /// <summary>The child process exceeded its memory cap.</summary>
    MemoryLimitExceeded,

    /// <summary>The judge itself failed. Terminal state for a poison message.</summary>
    InternalError
}

public static class SubmissionStatusExtensions
{
    /// <summary>True once no further judging will change the verdict.</summary>
    public static bool IsTerminal(this SubmissionStatus status) =>
        status is not (SubmissionStatus.Queued or SubmissionStatus.Running);
}
