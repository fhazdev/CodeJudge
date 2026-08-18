using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeJudge.Judge.Execution;

/// <summary>
/// Turns a problem's harness plus a user's submission into a runnable assembly, in memory,
/// without MSBuild. See section 4 of the build plan for why the harness exists at all.
/// </summary>
public sealed class CompilationService
{
    /// <summary>Path recorded on the submission's syntax tree, and shown in diagnostics.</summary>
    public const string SubmissionPath = "Solution.cs";

    /// <summary>Path recorded on the harness syntax tree.</summary>
    public const string HarnessPath = "Harness.cs";

    /// <summary>
    /// Assemblies a submission is allowed to reference. This process has Roslyn, EF Core
    /// and Npgsql loaded; without this filter, a submission could reference all of them
    /// and open its own database connection. Note the honest limit of this defence:
    /// System.Private.CoreLib is mandatory and already contains File, Directory and
    /// Environment, so an allowlist raises the cost of misbehaving without preventing it.
    /// The child process and the container are the real boundary.
    /// </summary>
    private static readonly HashSet<string> AllowedAssemblies =
    [
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Runtime.Extensions",
        "System.Runtime.Numerics",
        "System.Runtime.InteropServices",
        "System.Console",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Collections.Immutable",
        "System.Collections.Specialized",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Memory",
        "System.Numerics.Vectors",
        "System.ObjectModel",
        "System.Text.Json",
        "System.Text.Encodings.Web",
        "System.Text.RegularExpressions",
        "System.Threading",
        "netstandard"
    ];

    private static readonly Lazy<ImmutableArray<MetadataReference>> References =
        new(BuildReferences, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    private readonly JudgeOptions _options;

    public CompilationService(JudgeOptions options) => _options = options;

    public async Task<CompilationResult> CompileAsync(
        string harnessCode,
        string submissionCode,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CompileTimeout);

        try
        {
            return await Task.Run(() => Compile(harnessCode, submissionCode, timeout.Token), timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CompilationResult.Failed(
                $"Compilation timed out after {_options.CompileTimeout.TotalSeconds:0} seconds.");
        }
    }

    private CompilationResult Compile(string harnessCode, string submissionCode, CancellationToken ct)
    {
        // Two separate syntax trees, never concatenated text. This is what makes Roslyn
        // report errors against the user's own line numbers instead of lines shifted by
        // however long the harness happens to be.
        var submissionTree = CSharpSyntaxTree.ParseText(
            submissionCode, ParseOptions, path: SubmissionPath, cancellationToken: ct);

        var harnessTree = CSharpSyntaxTree.ParseText(
            harnessCode, ParseOptions, path: HarnessPath, cancellationToken: ct);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"Submission_{Guid.NewGuid():N}",
            syntaxTrees: [submissionTree, harnessTree],
            references: References.Value,
            options: new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Release,
                // `unsafe` would hand the submission raw pointers. Cheap to refuse.
                allowUnsafe: false,
                // Submissions are ordinary LeetCode-style code; nullable warnings would
                // be noise, and we only ever fail on errors anyway.
                nullableContextOptions: NullableContextOptions.Disable,
                warningLevel: 0,
                deterministic: true,
                concurrentBuild: true));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream, cancellationToken: ct);

        if (!emitResult.Success)
        {
            return CompilationResult.Failed(FormatDiagnostics(emitResult.Diagnostics));
        }

        return CompilationResult.Ok(peStream.ToArray());
    }

    private string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
            .Take(20)
            .ToList();

        var sb = new StringBuilder();
        var harnessOnly = errors.Count > 0 && errors.All(d => IsHarness(d));

        foreach (var diagnostic in errors)
        {
            var span = diagnostic.Location.GetLineSpan();
            var origin = IsHarness(diagnostic) ? "test harness" : SubmissionPath;

            sb.AppendLine(
                $"{origin}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): " +
                $"{diagnostic.Id}: {diagnostic.GetMessage()}");
        }

        if (harnessOnly)
        {
            // The common case behind this: the user renamed the method, or changed a
            // parameter type, so the harness no longer binds. Saying so directly beats
            // making them work it out from a CS1061 pointing at code they cannot see.
            sb.AppendLine();
            sb.AppendLine(
                "All errors are in the test harness, which means your Solution class does " +
                "not match the expected signature. Check the method name, parameter types " +
                "and return type against the starter code.");
        }

        var text = sb.ToString().TrimEnd();
        return text.Length <= _options.MaxStderrLength
            ? text
            : text[.._options.MaxStderrLength] + "\n… truncated";
    }

    private static bool IsHarness(Diagnostic diagnostic) =>
        diagnostic.Location.GetLineSpan().Path == HarnessPath;

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        // The runtime hands us the full list of assemblies it loaded this process with.
        // Filtering it is both faster and more accurate than probing a reference pack,
        // because these are the exact assemblies the child process will bind against.
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";

        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        foreach (var path in trustedPlatformAssemblies.Split(
                     Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (AllowedAssemblies.Contains(Path.GetFileNameWithoutExtension(path)))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
        }

        if (builder.Count == 0)
        {
            throw new InvalidOperationException(
                "No metadata references resolved. The judge cannot compile anything.");
        }

        return builder.ToImmutable();
    }
}
