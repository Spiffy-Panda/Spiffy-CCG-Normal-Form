using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Ccgnf.Diagnostics;
using Ccgnf.Grammar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Parsing;

/// <summary>
/// Facade over the ANTLR-generated lexer and parser. Accepts preprocessed
/// source text and returns a parse tree plus any diagnostics.
/// </summary>
public sealed class CcgnfParser
{
    private readonly ILogger<CcgnfParser> _log;

    public CcgnfParser(ILogger<CcgnfParser>? log = null)
    {
        _log = log ?? NullLogger<CcgnfParser>.Instance;
    }

    public ParseResult Parse(string preprocessedText, string sourceName = "<preprocessed>")
    {
        _log.LogDebug("Parsing {SourceName} ({Length} chars)", sourceName, preprocessedText.Length);

        var diagnostics = new List<Diagnostic>();
        var listener = new AntlrDiagnosticListener(sourceName, diagnostics);

        var input = new AntlrInputStream(preprocessedText) { name = sourceName };
        var lexer = new CcgnfLexer(input);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(listener);

        var tokens = new CommonTokenStream(lexer);
        var parser = new Grammar.CcgnfParser(tokens);
        parser.RemoveErrorListeners();
        parser.AddErrorListener(listener);

        IParseTree tree;
        try
        {
            tree = parser.file();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Parser threw while parsing {SourceName}", sourceName);
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error, "P999",
                $"Parser crashed: {ex.Message}",
                new SourcePosition(sourceName, 0, 0)));
            return new ParseResult(null, diagnostics);
        }

        _log.LogInformation(
            "Parsed {SourceName}: {DiagnosticCount} diagnostics; tokens={TokenCount}",
            sourceName, diagnostics.Count, tokens.Size);

        return new ParseResult(tree, diagnostics);
    }
}
