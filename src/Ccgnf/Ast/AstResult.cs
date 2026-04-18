using Ccgnf.Diagnostics;

namespace Ccgnf.Ast;

/// <summary>
/// Output of the AST builder: a typed AST plus any diagnostics the builder
/// produced (unrecognized constructs, incomplete parse subtrees, ...). The
/// builder runs AFTER a parse that produced a tree; if the parse itself had
/// errors, those are carried in the parse stage's diagnostics list, not here.
/// </summary>
public sealed class AstResult
{
    public AstFile? File { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors { get; }

    public AstResult(AstFile? file, IReadOnlyList<Diagnostic> diagnostics)
    {
        File = file;
        Diagnostics = diagnostics;
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
