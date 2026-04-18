using Ccgnf.Diagnostics;

namespace Ccgnf.Validation;

/// <summary>
/// Output of the <see cref="Validator"/>: the list of diagnostics it produced
/// and a convenience flag for downstream code. The validated AST is not
/// stored here; the caller passes it in.
/// </summary>
public sealed class ValidationResult
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors { get; }

    public ValidationResult(IReadOnlyList<Diagnostic> diagnostics)
    {
        Diagnostics = diagnostics;
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
