namespace CodeJudge.Judge.Execution;

public static class RunnerLocator
{
    public const string EnvironmentVariable = "CODEJUDGE_RUNNER_DLL";

    private const string RunnerFileName = "CodeJudge.Judge.Runner.dll";

    /// <summary>
    /// Finds the sandbox runner assembly. Runner.targets copies it into a "runner"
    /// subfolder of whatever is doing the spawning, which covers both the judge itself and
    /// the test project. The environment variable exists so the container image can put it
    /// somewhere else without a rebuild.
    /// </summary>
    public static string Locate()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!File.Exists(configured))
            {
                throw new FileNotFoundException(
                    $"{EnvironmentVariable} is set to '{configured}' but no file is there.", configured);
            }

            return Path.GetFullPath(configured);
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "runner", RunnerFileName);
        if (File.Exists(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        throw new FileNotFoundException(
            $"Could not find {RunnerFileName}. Looked in '{candidate}'. " +
            $"Build CodeJudge.Judge.Runner, or set {EnvironmentVariable} to its path.",
            candidate);
    }
}
