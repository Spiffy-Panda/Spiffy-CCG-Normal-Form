using Ccgnf.Diagnostics;

namespace Ccgnf.Preprocessing;

/// <summary>
/// Output of <see cref="Preprocessor"/> over one or more source files.
/// </summary>
public sealed class PreprocessorResult
{
    public string ExpandedText { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>Names of every <c>define</c> macro collected during
    /// preprocessing, in registration order.</summary>
    public IReadOnlyList<string> MacroNames { get; }

    public bool HasErrors { get; }

    public PreprocessorResult(
        string expandedText,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string>? macroNames = null)
    {
        ExpandedText = expandedText;
        Diagnostics = diagnostics;
        MacroNames = macroNames ?? Array.Empty<string>();
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
