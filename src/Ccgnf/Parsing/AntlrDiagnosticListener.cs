using System.IO;
using Antlr4.Runtime;
using Ccgnf.Diagnostics;

namespace Ccgnf.Parsing;

/// <summary>
/// Bridges ANTLR's error-reporting into our <see cref="Diagnostic"/> list.
/// Attached to both the lexer and the parser.
/// </summary>
internal sealed class AntlrDiagnosticListener : BaseErrorListener, IAntlrErrorListener<int>
{
    private readonly string _sourceName;
    private readonly List<Diagnostic> _sink;

    public AntlrDiagnosticListener(string sourceName, List<Diagnostic> sink)
    {
        _sourceName = sourceName;
        _sink = sink;
    }

    // Parser errors.
    public override void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        IToken offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _sink.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            "P001",
            msg,
            new SourcePosition(_sourceName, line, charPositionInLine + 1)));
    }

    // Lexer errors.
    public void SyntaxError(
        TextWriter output,
        IRecognizer recognizer,
        int offendingSymbol,
        int line,
        int charPositionInLine,
        string msg,
        RecognitionException e)
    {
        _sink.Add(new Diagnostic(
            DiagnosticSeverity.Error,
            "L001",
            msg,
            new SourcePosition(_sourceName, line, charPositionInLine + 1)));
    }
}
