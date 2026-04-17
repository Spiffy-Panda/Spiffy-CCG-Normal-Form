using Ccgnf.Diagnostics;

namespace Ccgnf.Preprocessing;

/// <summary>
/// Output of <see cref="Preprocessor"/> over one or more source files.
/// </summary>
public sealed class PreprocessorResult
{
    public string ExpandedText { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors { get; }

    public PreprocessorResult(string expandedText, IReadOnlyList<Diagnostic> diagnostics)
    {
        ExpandedText = expandedText;
        Diagnostics = diagnostics;
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
