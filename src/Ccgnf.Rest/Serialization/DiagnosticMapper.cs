using Ccgnf.Diagnostics;

namespace Ccgnf.Rest.Serialization;

internal static class DiagnosticMapper
{
    public static IReadOnlyList<DiagnosticDto> ToDtos(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Select(d => new DiagnosticDto(
            Severity: d.Severity.ToString(),
            Code: d.Code,
            Message: d.Message,
            File: d.Position.File,
            Line: d.Position.Line,
            Column: d.Position.Column)).ToList();
}
