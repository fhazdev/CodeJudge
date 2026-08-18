using System.Reflection;
using System.Runtime.Loader;

// The sandbox host. Loads a compiled submission and invokes its entry point, nothing more.
//
// This process exists for one reason: to be killable. Managed code cannot be reliably
// aborted from inside its own process (a `while (true)` ignores every CancellationToken),
// so the only dependable way to enforce a time limit is for a *parent* to kill a *child*.
// That single constraint is what ruled out in-process Roslyn scripting.
//
// Protocol:
//   argv[0]  path to the submission assembly
//   stdin    the test case input, passed straight through to the submission
//   stdout   whatever the submission writes
//   stderr   exception detail, when the submission throws
//   exit 0   ran to completion
//   exit 1   the submission threw
//   exit 2   the runner itself could not start (never the submission's fault)

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: CodeJudge.Judge.Runner <assembly-path>");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"submission assembly not found: {assemblyPath}");
    return 2;
}

MethodInfo? entryPoint;
try
{
    var context = new AssemblyLoadContext("submission");
    var assembly = context.LoadFromAssemblyPath(assemblyPath);

    entryPoint = assembly.EntryPoint;
    if (entryPoint is null)
    {
        Console.Error.WriteLine("submission assembly has no entry point");
        return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"failed to load submission assembly: {ex.Message}");
    return 2;
}

try
{
    // The harness declares `static void Main()`, but tolerate `Main(string[])` too.
    var arguments = entryPoint.GetParameters().Length == 0
        ? null
        : new object?[] { Array.Empty<string>() };

    var returned = entryPoint.Invoke(null, arguments);

    Console.Out.Flush();
    return returned is int exitCode ? exitCode : 0;
}
catch (TargetInvocationException ex) when (ex.InnerException is not null)
{
    // Reflection wraps whatever the submission threw. The wrapper is noise; the inner
    // exception is what the user needs to see.
    Console.Out.Flush();
    Console.Error.WriteLine(Describe(ex.InnerException));
    return 1;
}
catch (Exception ex)
{
    Console.Out.Flush();
    Console.Error.WriteLine(Describe(ex));
    return 1;
}

static string Describe(Exception ex)
{
    var stack = ex.StackTrace?
        .Split('\n')
        .Take(5)
        .Select(line => line.TrimEnd())
        ?? [];

    return string.Join(Environment.NewLine, [$"{ex.GetType().Name}: {ex.Message}", .. stack]);
}
