namespace Ccgnf.Diagnostics;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One diagnostic — an error, warning, or informational note — emitted by a
/// compilation stage (preprocessor, parser, validator, interpreter).
/// </summary>
public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    SourcePosition Position)
{
    public override string ToString() =>
        $"{Position}: {SeverityLabel()} {Code}: {Message}";

    private string SeverityLabel() => Severity switch
    {
        DiagnosticSeverity.Error   => "error",
        DiagnosticSeverity.Warning => "warning",
        DiagnosticSeverity.Info    => "info",
        _ => Severity.ToString().ToLowerInvariant(),
    };
}
