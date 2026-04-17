using System.Text;
using Ccgnf.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Preprocessing;

/// <summary>
/// Implements the CCGNF preprocessor (see grammar/GrammarSpec.md §4):
/// collect <c>define</c> directives into a macro table, then expand macro
/// invocations with token-level substitution. Recursive expansion is
/// cycle-checked.
///
/// Current limitations (tracked in GrammarSpec §12 Open questions):
///   * No source map: expanded-output positions do not back-reference to
///     original file/line/col. Diagnostics emitted during preprocessing DO
///     carry original positions; diagnostics emitted downstream by the parser
///     will carry positions within the expanded text.
///   * No <c>#include</c> resolution.
///   * Hygiene is not enforced; macro authors must not shadow call-site
///     identifiers.
/// </summary>
public sealed class Preprocessor
{
    private readonly ILogger<Preprocessing.Preprocessor> _log;
    private const int MaxExpansionDepth = 64;

    public Preprocessor(ILogger<Preprocessing.Preprocessor>? log = null)
    {
        _log = log ?? NullLogger<Preprocessing.Preprocessor>.Instance;
    }

    public PreprocessorResult Preprocess(SourceFile source) =>
        Preprocess(new[] { source });

    /// <summary>
    /// Preprocesses the given source files in order. Macros collected from
    /// earlier files are visible in later files.
    /// </summary>
    public PreprocessorResult Preprocess(IEnumerable<SourceFile> sources)
    {
        var diagnostics = new List<Diagnostic>();
        var macros = new MacroTable();
        var sb = new StringBuilder();

        foreach (var source in sources)
        {
            _log.LogDebug("Preprocessing {File}", source.Path);
            var tokens = new PpTokenizer(source).Tokenize();

            var (extracted, diags) = ExtractDefines(tokens, macros);
            diagnostics.AddRange(diags);

            var expanded = ExpandAll(extracted, macros, diagnostics);
            foreach (var t in expanded)
            {
                if (t.Kind == PpTokenKind.Eof) continue;
                sb.Append(t.Text);
            }
        }

        _log.LogInformation(
            "Preprocessor: {MacroCount} macros collected, {DiagnosticCount} diagnostics",
            macros.All.Count, diagnostics.Count);

        return new PreprocessorResult(sb.ToString(), diagnostics);
    }

    // -----------------------------------------------------------------------
    // Pass 1: extract all `define` directives and produce a token stream with
    // those directives removed.
    // -----------------------------------------------------------------------

    private (List<PpToken> remaining, List<Diagnostic> diags) ExtractDefines(
        List<PpToken> tokens,
        MacroTable macros)
    {
        var remaining = new List<PpToken>(tokens.Count);
        var diags = new List<Diagnostic>();
        int i = 0;

        while (i < tokens.Count)
        {
            var t = tokens[i];
            if (t.Kind == PpTokenKind.Ident && t.Text == "define" &&
                IsAtLineStart(tokens, i))
            {
                int startIndex = i;
                var result = TryParseDefine(tokens, ref i, diags);
                if (result is not null)
                {
                    if (!macros.Register(result))
                    {
                        diags.Add(new Diagnostic(
                            DiagnosticSeverity.Error,
                            "PP001",
                            $"Macro '{result.Name}' is already defined.",
                            result.Position));
                    }
                    else
                    {
                        _log.LogDebug(
                            "Defined macro {Name}({Arity}) at {Pos}",
                            result.Name, result.Arity, result.Position);
                    }
                    // Skip any trailing newline after the define body.
                    while (i < tokens.Count && tokens[i].Kind == PpTokenKind.Newline)
                    {
                        i++;
                    }
                    continue;
                }
                // Parse failed; keep the tokens as-is and advance past `define`
                // so we don't loop forever.
                i = startIndex + 1;
                remaining.Add(t);
                continue;
            }
            remaining.Add(t);
            i++;
        }

        return (remaining, diags);
    }

    private static bool IsAtLineStart(List<PpToken> tokens, int i)
    {
        // True if this token is the first non-whitespace on its line.
        for (int j = i - 1; j >= 0; j--)
        {
            var k = tokens[j].Kind;
            if (k == PpTokenKind.Newline) return true;
            if (k == PpTokenKind.Whitespace) continue;
            return false;
        }
        return true;
    }

    private MacroDefinition? TryParseDefine(
        List<PpToken> tokens,
        ref int i,
        List<Diagnostic> diags)
    {
        // `define` is at tokens[i]; consume it.
        var definePos = tokens[i].Pos;
        i++;

        SkipTrivia(tokens, ref i, includeNewlines: false);
        if (i >= tokens.Count || tokens[i].Kind != PpTokenKind.Ident)
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error, "PP002",
                "Expected macro name after 'define'.",
                definePos));
            return null;
        }
        var nameToken = tokens[i];
        string name = nameToken.Text;
        i++;

        SkipTrivia(tokens, ref i, includeNewlines: false);

        // Optional parameter list
        var parameters = new List<string>();
        if (i < tokens.Count && tokens[i].Kind == PpTokenKind.LParen)
        {
            i++; // consume (
            bool expectingParam = true;
            while (i < tokens.Count && tokens[i].Kind != PpTokenKind.RParen)
            {
                if (tokens[i].Kind == PpTokenKind.Ident && expectingParam)
                {
                    parameters.Add(tokens[i].Text);
                    expectingParam = false;
                    i++;
                }
                else if (tokens[i].Kind == PpTokenKind.Comma && !expectingParam)
                {
                    expectingParam = true;
                    i++;
                }
                else if (IsTrivia(tokens[i].Kind))
                {
                    i++;
                }
                else
                {
                    diags.Add(new Diagnostic(
                        DiagnosticSeverity.Error, "PP003",
                        $"Unexpected token '{tokens[i].Text}' in macro parameter list.",
                        tokens[i].Pos));
                    return null;
                }
            }
            if (i >= tokens.Count)
            {
                diags.Add(new Diagnostic(
                    DiagnosticSeverity.Error, "PP004",
                    "Unterminated macro parameter list.",
                    nameToken.Pos));
                return null;
            }
            i++; // consume )
        }

        SkipTrivia(tokens, ref i, includeNewlines: true);

        if (i >= tokens.Count || tokens[i].Kind != PpTokenKind.Eq)
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error, "PP005",
                $"Expected '=' after macro head for '{name}'.",
                nameToken.Pos));
            return null;
        }
        i++; // consume =

        SkipTrivia(tokens, ref i, includeNewlines: true);

        // Body: collect tokens until we reach a body terminator. A terminator
        // is one of:
        //   * EOF
        //   * A blank line (two consecutive Newline tokens with only trivia
        //     between them) at bracket depth 0
        //   * Another top-level declaration (`define`, `Entity`, `Card`,
        //     `Token`) at line start and bracket depth 0
        var body = new List<PpToken>();
        int depth = 0;
        while (i < tokens.Count && tokens[i].Kind != PpTokenKind.Eof)
        {
            var cur = tokens[i];
            if (cur.Kind == PpTokenKind.LParen || cur.Kind == PpTokenKind.LBrace || cur.Kind == PpTokenKind.LBrack)
            {
                depth++;
                body.Add(cur);
                i++;
                continue;
            }
            if (cur.Kind == PpTokenKind.RParen || cur.Kind == PpTokenKind.RBrace || cur.Kind == PpTokenKind.RBrack)
            {
                depth--;
                body.Add(cur);
                i++;
                continue;
            }
            if (depth == 0 && cur.Kind == PpTokenKind.Newline && IsBlankLineAt(tokens, i))
            {
                break;
            }
            if (depth == 0 && cur.Kind == PpTokenKind.Ident && IsAtLineStart(tokens, i))
            {
                if (IsTopLevelStarter(cur.Text))
                {
                    break;
                }
            }
            body.Add(cur);
            i++;
        }

        // Trim trailing trivia from the body.
        while (body.Count > 0 && IsTrivia(body[^1].Kind))
        {
            body.RemoveAt(body.Count - 1);
        }

        return new MacroDefinition(name, parameters, body, definePos);
    }

    private static bool IsBlankLineAt(List<PpToken> tokens, int i)
    {
        // At tokens[i] which is a Newline. Check if the next line contains
        // only whitespace/trivia up to (and including) another Newline.
        int j = i + 1;
        while (j < tokens.Count)
        {
            var k = tokens[j].Kind;
            if (k == PpTokenKind.Newline) return true;
            if (k == PpTokenKind.Whitespace || k == PpTokenKind.LineComment || k == PpTokenKind.BlockComment) { j++; continue; }
            return false;
        }
        return false; // EOF handled elsewhere
    }

    private static bool IsTopLevelStarter(string text) =>
        text is "define" or "Entity" or "Card" or "Token";

    private static bool IsTrivia(PpTokenKind k) =>
        k is PpTokenKind.Whitespace
           or PpTokenKind.Newline
           or PpTokenKind.LineComment
           or PpTokenKind.BlockComment;

    private static void SkipTrivia(List<PpToken> tokens, ref int i, bool includeNewlines)
    {
        while (i < tokens.Count)
        {
            var k = tokens[i].Kind;
            if (k == PpTokenKind.Whitespace
                || k == PpTokenKind.LineComment
                || k == PpTokenKind.BlockComment
                || (includeNewlines && k == PpTokenKind.Newline))
            {
                i++;
                continue;
            }
            break;
        }
    }

    // -----------------------------------------------------------------------
    // Pass 2: expand macro invocations recursively.
    // -----------------------------------------------------------------------

    private List<PpToken> ExpandAll(
        List<PpToken> tokens,
        MacroTable macros,
        List<Diagnostic> diags)
    {
        var expanded = new List<PpToken>(tokens.Count);
        ExpandInto(tokens, expanded, macros, diags, new Stack<string>(), depth: 0);
        return expanded;
    }

    private void ExpandInto(
        IReadOnlyList<PpToken> source,
        List<PpToken> output,
        MacroTable macros,
        List<Diagnostic> diags,
        Stack<string> expansionStack,
        int depth)
    {
        if (depth > MaxExpansionDepth)
        {
            diags.Add(new Diagnostic(
                DiagnosticSeverity.Error, "PP010",
                $"Maximum macro expansion depth ({MaxExpansionDepth}) exceeded.",
                source.Count > 0 ? source[0].Pos : SourcePosition.Unknown));
            return;
        }

        int i = 0;
        while (i < source.Count)
        {
            var t = source[i];

            if (t.Kind == PpTokenKind.Ident && macros.TryGet(t.Text, out var macro))
            {
                if (expansionStack.Contains(macro.Name))
                {
                    diags.Add(new Diagnostic(
                        DiagnosticSeverity.Error, "PP011",
                        $"Recursive expansion of macro '{macro.Name}' detected " +
                        $"(stack: {string.Join(" -> ", expansionStack.Reverse())} -> {macro.Name}).",
                        t.Pos));
                    output.Add(t);
                    i++;
                    continue;
                }

                if (macro.Arity == 0)
                {
                    expansionStack.Push(macro.Name);
                    var sub = SubstituteAndReposition(macro.Body, new Dictionary<string, List<PpToken>>(), t.Pos);
                    ExpandInto(sub, output, macros, diags, expansionStack, depth + 1);
                    expansionStack.Pop();
                    i++;
                    continue;
                }

                // Arity > 0 — require next non-trivia token to be '('.
                int save = i;
                int j = i + 1;
                while (j < source.Count && IsTrivia(source[j].Kind)) j++;
                if (j >= source.Count || source[j].Kind != PpTokenKind.LParen)
                {
                    // Not an invocation; emit as literal.
                    output.Add(t);
                    i++;
                    continue;
                }

                // Parse arguments: comma-separated at top bracket level.
                var args = new List<List<PpToken>>();
                int k = j + 1; // past '('
                int depth2 = 0;
                var cur = new List<PpToken>();
                while (k < source.Count)
                {
                    var a = source[k];
                    if (depth2 == 0 && a.Kind == PpTokenKind.RParen)
                    {
                        if (cur.Count > 0 || args.Count > 0) args.Add(TrimTrivia(cur));
                        k++;
                        break;
                    }
                    if (depth2 == 0 && a.Kind == PpTokenKind.Comma)
                    {
                        args.Add(TrimTrivia(cur));
                        cur = new List<PpToken>();
                        k++;
                        continue;
                    }
                    if (a.Kind is PpTokenKind.LParen or PpTokenKind.LBrace or PpTokenKind.LBrack) depth2++;
                    else if (a.Kind is PpTokenKind.RParen or PpTokenKind.RBrace or PpTokenKind.RBrack) depth2--;
                    cur.Add(a);
                    k++;
                }
                if (args.Count != macro.Arity)
                {
                    diags.Add(new Diagnostic(
                        DiagnosticSeverity.Error, "PP012",
                        $"Macro '{macro.Name}' expects {macro.Arity} argument(s), " +
                        $"got {args.Count}.",
                        t.Pos));
                    // Emit the identifier and '(' as-is and continue, to avoid
                    // losing tokens.
                    output.Add(t);
                    i = save + 1;
                    continue;
                }

                // Build substitution table.
                var subs = new Dictionary<string, List<PpToken>>(StringComparer.Ordinal);
                for (int p = 0; p < macro.Arity; p++)
                {
                    subs[macro.Parameters[p]] = args[p];
                }

                expansionStack.Push(macro.Name);
                var substituted = SubstituteAndReposition(macro.Body, subs, t.Pos);
                ExpandInto(substituted, output, macros, diags, expansionStack, depth + 1);
                expansionStack.Pop();
                i = k; // past the closing ')'
                continue;
            }

            output.Add(t);
            i++;
        }
    }

    /// <summary>
    /// Substitute parameter identifiers in a macro body with their argument
    /// token lists, and reposition the resulting tokens so their SourcePosition
    /// reflects the invocation site (not the define site). This keeps
    /// diagnostics anchored where the author is working.
    /// </summary>
    private static List<PpToken> SubstituteAndReposition(
        IReadOnlyList<PpToken> body,
        Dictionary<string, List<PpToken>> subs,
        SourcePosition callerPos)
    {
        var result = new List<PpToken>();
        foreach (var t in body)
        {
            if (t.Kind == PpTokenKind.Ident && subs.TryGetValue(t.Text, out var replacement))
            {
                foreach (var r in replacement)
                {
                    result.Add(new PpToken(r.Kind, r.Text, callerPos));
                }
            }
            else
            {
                result.Add(new PpToken(t.Kind, t.Text, callerPos));
            }
        }
        return result;
    }

    private static List<PpToken> TrimTrivia(List<PpToken> tokens)
    {
        int start = 0, end = tokens.Count;
        while (start < end && IsTrivia(tokens[start].Kind)) start++;
        while (end > start && IsTrivia(tokens[end - 1].Kind)) end--;
        return tokens.GetRange(start, end - start);
    }
}
