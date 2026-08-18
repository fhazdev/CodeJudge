using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CodeJudge.Judge.Execution;

/// <summary>
/// Runs one test case by spawning <c>CodeJudge.Judge.Runner</c> as a child process and
/// killing it if it misbehaves. Every limit that actually binds is enforced here.
/// </summary>
public sealed partial class SandboxRunner
{
    private readonly JudgeOptions _options;
    private readonly string _runnerDllPath;

    public SandboxRunner(JudgeOptions options, string? runnerDllPath = null)
    {
        _options = options;
        _runnerDllPath = runnerDllPath ?? RunnerLocator.Locate();
    }

    /// <summary>
    /// Environment variables the child must never inherit. The parent holds a database
    /// connection string; the child hosts untrusted code. Those two facts should not meet.
    /// </summary>
    [GeneratedRegex("CONNECTION|SECRET|PASSWORD|TOKEN|CREDENTIAL|AZURE_",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveEnvironmentVariable { get; }

    public async Task<CaseExecution> RunAsync(
        string assemblyPath,
        string input,
        TimeSpan timeLimit,
        int memoryLimitKb,
        CancellationToken cancellationToken = default)
    {
        var startInfo = BuildStartInfo(assemblyPath, memoryLimitKb);

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        process.Start();

        // Read both streams concurrently from the start. Waiting for exit before reading
        // deadlocks the moment a submission writes more than one pipe buffer of output.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        await WriteStandardInputAsync(process, input);

        var memoryCapBytes = (memoryLimitKb * 1024L) + _options.MemoryHeadroomBytes;
        using var monitorCts = new CancellationTokenSource();
        var monitor = MonitorMemoryAsync(process, memoryCapBytes, monitorCts.Token);

        var timedOut = false;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeLimit);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // The whole reason the child exists. A runaway submission is unreachable from
            // inside its own process, so we terminate it from out here.
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
        }

        stopwatch.Stop();
        await monitorCts.CancelAsync();
        var (peakWorkingSet, memoryExceeded) = await monitor;

        var standardOutput = await ReadWithGraceAsync(stdoutTask);
        var standardError = await ReadWithGraceAsync(stderrTask);

        // The GC heap hard limit produces a clean OutOfMemoryException rather than letting
        // the container OOM-kill the whole job. That exception is the primary signal;
        // the working-set backstop only catches native allocation.
        if (standardError.Contains("OutOfMemoryException", StringComparison.Ordinal))
        {
            memoryExceeded = true;
        }

        return new CaseExecution(
            StandardOutput: standardOutput,
            StandardError: standardError,
            ExitCode: SafeExitCode(process, timedOut),
            ElapsedMs: stopwatch.ElapsedMilliseconds,
            PeakWorkingSetBytes: peakWorkingSet,
            TimedOut: timedOut,
            MemoryExceeded: memoryExceeded);
    }

    private ProcessStartInfo BuildStartInfo(string assemblyPath, int memoryLimitKb)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(assemblyPath) ?? Environment.CurrentDirectory
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(_runnerDllPath);
        startInfo.ArgumentList.Add(assemblyPath);

        // ProcessStartInfo.Environment starts life as a copy of ours. Strip what the child
        // has no business seeing before it starts.
        foreach (var key in startInfo.Environment.Keys.ToList())
        {
            if (SensitiveEnvironmentVariable.IsMatch(key))
            {
                startInfo.Environment.Remove(key);
            }
        }

        // Hex, no 0x prefix. Turns "allocate until the container dies" into a catchable
        // OutOfMemoryException inside the child.
        startInfo.Environment["DOTNET_GCHeapHardLimit"] = (memoryLimitKb * 1024L).ToString("X");

        return startInfo;
    }

    private static async Task WriteStandardInputAsync(Process process, string input)
    {
        try
        {
            await process.StandardInput.WriteAsync(input);
            await process.StandardInput.FlushAsync();
        }
        catch (IOException)
        {
            // The submission exited without reading stdin. Legitimate for a problem that
            // takes no input, and not our business either way.
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // Already gone.
            }
        }
    }

    private async Task<(long PeakBytes, bool Exceeded)> MonitorMemoryAsync(
        Process process, long capBytes, CancellationToken cancellationToken)
    {
        long peak = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (process.HasExited)
                    {
                        break;
                    }

                    process.Refresh();
                    peak = Math.Max(peak, process.WorkingSet64);

                    if (peak > capBytes)
                    {
                        TryKill(process);
                        return (peak, true);
                    }
                }
                catch (InvalidOperationException)
                {
                    break; // Process exited between the HasExited check and Refresh.
                }

                await Task.Delay(_options.MemorySampleInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the process exited and the caller cancelled the monitor.
        }

        return (peak, false);
    }

    private static async Task<string> ReadWithGraceAsync(Task<string> readTask)
    {
        // After a kill the pipes close and these complete immediately. The grace period
        // is for the case where a grandchild process still holds the write handle.
        var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(2)));
        return completed == readTask ? await readTask : string.Empty;
    }

    private static int SafeExitCode(Process process, bool timedOut)
    {
        if (timedOut)
        {
            return -1;
        }

        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // entireProcessTree: a submission that spawns its own children should not
                // be able to outlive the kill by hiding behind one.
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            // Already exited, or the OS refused. Either way there is nothing further to do.
        }
    }
}
