using Antlr4.Runtime.Tree;
using Ccgnf.Diagnostics;

namespace Ccgnf.Parsing;

/// <summary>
/// Result of running <see cref="CcgnfParser"/> on preprocessed source.
/// </summary>
public sealed class ParseResult
{
    public IParseTree? Tree { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool HasErrors { get; }

    public ParseResult(IParseTree? tree, IReadOnlyList<Diagnostic> diagnostics)
    {
        Tree = tree;
        Diagnostics = diagnostics;
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}
