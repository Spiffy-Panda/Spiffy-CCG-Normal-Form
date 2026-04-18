using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Ccgnf.Diagnostics;
using Ccgnf.Grammar;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Ccgnf.Grammar.CcgnfParser;

namespace Ccgnf.Ast;

/// <summary>
/// Walks an ANTLR parse tree and produces a typed AST. Parse errors should
/// already have been flagged by the parser stage before this runs; the builder
/// still defends against partial trees and emits <c>AST...</c> diagnostics for
/// shapes it does not recognize.
///
/// The builder does not perform any semantic analysis. Identifier resolution,
/// arity checks, and ruling compliance live in the Validator (next stage).
/// </summary>
public sealed class AstBuilder
{
    private readonly ILogger<AstBuilder> _log;
    private readonly List<Diagnostic> _diagnostics = new();
    private string _file = "<unknown>";

    public AstBuilder(ILogger<AstBuilder>? log = null)
    {
        _log = log ?? NullLogger<AstBuilder>.Instance;
    }

    public AstResult Build(IParseTree tree, string sourceName = "<unknown>")
    {
        _diagnostics.Clear();
        _file = sourceName;
        _log.LogDebug("Building AST for {SourceName}", sourceName);

        if (tree is not FileContext fileCtx)
        {
            Error("AST001", "Top-level parse tree is not a File context.",
                  SourceSpan.Unknown);
            return new AstResult(null, _diagnostics.ToList());
        }

        var astFile = BuildFile(fileCtx);
        _log.LogInformation(
            "Built AST for {SourceName}: {DeclCount} declarations, {DiagCount} diagnostics",
            sourceName, astFile?.Declarations.Count ?? 0, _diagnostics.Count);

        return new AstResult(astFile, _diagnostics.ToList());
    }

    // -------------------------------------------------------------------------
    // File and declarations
    // -------------------------------------------------------------------------

    private AstFile BuildFile(FileContext ctx)
    {
        var decls = new List<AstDeclaration>();
        foreach (var declCtx in ctx.declaration())
        {
            var decl = BuildDeclaration(declCtx);
            if (decl is not null) decls.Add(decl);
        }
        return new AstFile(Span(ctx), decls);
    }

    private AstDeclaration? BuildDeclaration(DeclarationContext ctx)
    {
        if (ctx.entityDecl() is { } e) return BuildEntityDecl(e);
        if (ctx.cardDecl() is { } c) return BuildCardDecl(c);
        if (ctx.tokenDecl() is { } t) return BuildTokenDecl(t);
        if (ctx.entityAugment() is { } a) return BuildEntityAugment(a);
        Error("AST010", "Unknown declaration form.", Span(ctx));
        return null;
    }

    private AstEntityDecl BuildEntityDecl(EntityDeclContext ctx)
    {
        string name = ctx.name(0).GetText();
        var indexParams = new List<string>();
        // name[i, j, ...]  — subsequent name() children are index params.
        var allNames = ctx.name();
        for (int i = 1; i < allNames.Length; i++)
        {
            indexParams.Add(allNames[i].GetText());
        }
        AstForClause? forClause = ctx.forClause() is { } fc ? BuildForClause(fc) : null;
        var body = BuildBlock(ctx.block());
        return new AstEntityDecl(Span(ctx), name, indexParams, forClause, body);
    }

    private AstCardDecl BuildCardDecl(CardDeclContext ctx) =>
        new(Span(ctx), ctx.name().GetText(), BuildBlock(ctx.block()));

    private AstTokenDecl BuildTokenDecl(TokenDeclContext ctx) =>
        new(Span(ctx), ctx.name().GetText(), BuildBlock(ctx.block()));

    private AstEntityAugment BuildEntityAugment(EntityAugmentContext ctx) =>
        new(Span(ctx),
            BuildTargetPath(ctx.targetPath()),
            BuildExpr(ctx.expr()));

    private AstForClause BuildForClause(ForClauseContext ctx) =>
        new(Span(ctx), ctx.name().GetText(), BuildExpr(ctx.expr()));

    private AstTargetPath BuildTargetPath(TargetPathContext ctx)
    {
        var segments = new List<AstTargetSegment>();
        var names = ctx.name();
        var indexSuffixes = ctx.indexSuffix();
        // Grammar: name indexSuffix? (DOT name indexSuffix?)+
        // Segments line up with names; each segment optionally has an indexSuffix.
        // ANTLR doesn't give us a clean 1:1 between name[i] and indexSuffix[i],
        // so we walk the children in order and pair them up.
        int nameIdx = 0;
        int suffixIdx = 0;
        AstTargetSegment? pending = null;
        foreach (var child in Children(ctx))
        {
            if (child is NameContext n)
            {
                if (pending is not null) segments.Add(pending);
                pending = new AstTargetSegment(Span(n), n.GetText(), Array.Empty<AstExpr>());
                nameIdx++;
            }
            else if (child is IndexSuffixContext s)
            {
                var indices = BuildIndexSuffix(s);
                if (pending is not null)
                {
                    pending = pending with { Indices = indices };
                }
                suffixIdx++;
            }
            // terminals (DOT, etc.) — skip
        }
        if (pending is not null) segments.Add(pending);
        return new AstTargetPath(Span(ctx), segments);
    }

    // -------------------------------------------------------------------------
    // Blocks and fields
    // -------------------------------------------------------------------------

    private AstBlock BuildBlock(BlockContext ctx)
    {
        var fields = ctx.field().Select(BuildField).ToList();
        return new AstBlock(Span(ctx), fields);
    }

    private AstField BuildField(FieldContext ctx) =>
        new(Span(ctx),
            BuildFieldKey(ctx.fieldKey()),
            BuildFieldValue(ctx.fieldValue()));

    private AstFieldKey BuildFieldKey(FieldKeyContext ctx)
    {
        string name = ctx.name().GetText();
        var indices = ctx.indexSuffix() is { } s
            ? BuildIndexSuffix(s)
            : (IReadOnlyList<AstExpr>)Array.Empty<AstExpr>();
        return new AstFieldKey(Span(ctx), name, indices);
    }

    private AstFieldValue BuildFieldValue(FieldValueContext ctx)
    {
        if (ctx.block() is { } b) return new AstFieldBlock(Span(ctx), BuildBlock(b));
        if (ctx.typedExpr() is { } t)
        {
            return new AstFieldTyped(
                Span(ctx),
                t.name().GetText(),
                BuildExpr(t.expr()));
        }
        if (ctx.expr() is { } e) return new AstFieldExpr(Span(ctx), BuildExpr(e));
        Error("AST020", "Unrecognized field value shape.", Span(ctx));
        // Return a placeholder so higher-level builds aren't crashing on null.
        return new AstFieldExpr(Span(ctx), new AstIdent(Span(ctx), "<error>"));
    }

    private IReadOnlyList<AstExpr> BuildIndexSuffix(IndexSuffixContext ctx) =>
        ctx.expr().Select(BuildExpr).ToList();

    // -------------------------------------------------------------------------
    // Expressions — descends the precedence chain
    // -------------------------------------------------------------------------

    private AstExpr BuildExpr(ExprContext ctx) => BuildOrExpr(ctx.orExpr());

    private AstExpr BuildOrExpr(OrExprContext ctx) =>
        FoldLeftBinary(ctx.andExpr(), ctx.OR(), BuildAndExpr);

    private AstExpr BuildAndExpr(AndExprContext ctx) =>
        FoldLeftBinary(ctx.notExpr(), ctx.AND(), BuildNotExpr);

    private AstExpr BuildNotExpr(NotExprContext ctx)
    {
        if (ctx.NOT() is not null)
        {
            return new AstUnaryOp(Span(ctx), "not", BuildNotExpr(ctx.notExpr()));
        }
        return BuildRelExpr(ctx.relExpr());
    }

    private AstExpr BuildRelExpr(RelExprContext ctx)
    {
        var adds = ctx.addExpr();
        if (adds.Length == 1) return BuildAddExpr(adds[0]);
        // Grammar constrains relExpr to at most one infix op.
        string op = FindOperator(ctx) ?? "==";
        return new AstBinaryOp(
            Span(ctx), op,
            BuildAddExpr(adds[0]),
            BuildAddExpr(adds[1]));
    }

    private AstExpr BuildAddExpr(AddExprContext ctx)
    {
        var muls = ctx.mulExpr();
        AstExpr acc = BuildMulExpr(muls[0]);
        for (int i = 1; i < muls.Length; i++)
        {
            string op = GetTerminalBefore(ctx, muls[i]) ?? "+";
            acc = new AstBinaryOp(Span(ctx), op, acc, BuildMulExpr(muls[i]));
        }
        return acc;
    }

    private AstExpr BuildMulExpr(MulExprContext ctx)
    {
        var unaries = ctx.unaryExpr();
        AstExpr acc = BuildUnaryExpr(unaries[0]);
        for (int i = 1; i < unaries.Length; i++)
        {
            string op = GetTerminalBefore(ctx, unaries[i]) ?? "*";
            acc = new AstBinaryOp(Span(ctx), op, acc, BuildUnaryExpr(unaries[i]));
        }
        return acc;
    }

    private AstExpr BuildUnaryExpr(UnaryExprContext ctx)
    {
        if (ctx.unaryExpr() is { } inner)
        {
            string op = ctx.MINUS() is not null ? "-" : "+";
            return new AstUnaryOp(Span(ctx), op, BuildUnaryExpr(inner));
        }
        return BuildPostfixExpr(ctx.postfixExpr());
    }

    private AstExpr BuildPostfixExpr(PostfixExprContext ctx)
    {
        AstExpr acc = BuildAtom(ctx.atom());
        foreach (var trailer in ctx.trailer())
        {
            acc = BuildTrailer(acc, trailer);
        }
        return acc;
    }

    private AstExpr BuildTrailer(AstExpr target, TrailerContext trailer)
    {
        switch (trailer)
        {
            case TrailerMemberContext m:
                return new AstMemberAccess(
                    Span(m), target, m.name().GetText());

            case TrailerIndexContext i:
                return new AstIndex(
                    Span(i), target, BuildIndexSuffix(i.indexSuffix()));

            case TrailerCallContext c:
                var args = c.argList() is { } al
                    ? al.arg().Select(BuildArg).ToList()
                    : new List<AstArg>();
                return new AstFunctionCall(Span(c), target, args);

            default:
                Error("AST030", "Unknown trailer form.", Span(trailer));
                return target;
        }
    }

    private AstArg BuildArg(ArgContext ctx)
    {
        switch (ctx)
        {
            case ArgNamedContext n:
                return new AstArgNamed(
                    Span(n), n.name().GetText(), BuildExpr(n.expr()));
            case ArgBindingContext b:
                return new AstArgBinding(
                    Span(b), b.name().GetText(), BuildExpr(b.expr()));
            case ArgPositionalContext p:
                return new AstArgPositional(Span(p), BuildExpr(p.expr()));
            default:
                Error("AST031", "Unknown argument form.", Span(ctx));
                return new AstArgPositional(Span(ctx), new AstIdent(Span(ctx), "<error>"));
        }
    }

    private AstExpr BuildAtom(AtomContext ctx)
    {
        switch (ctx)
        {
            case AtomLiteralContext l:
                return BuildLiteral(l.literal());

            case AtomLambdaContext la:
                return BuildLambda(la.lambda());

            case AtomBraceContext b:
                return BuildBraceExpr(b.braceExpr());

            case AtomListContext li:
                return BuildListOrRange(li.listOrRange());

            case AtomIfContext i:
                return BuildIfExpr(i.ifExpr());

            case AtomSwitchContext s:
                return BuildSwitchExpr(s.switchExpr());

            case AtomCondContext c:
                return BuildCondExpr(c.condExpr());

            case AtomWhenContext w:
                return BuildWhenExpr(w.whenExpr());

            case AtomLetContext le:
                return BuildLetExpr(le.letExpr());

            case AtomIdentContext id:
                return new AstIdent(Span(id), id.name().GetText());

            case AtomParenContext p:
                var elems = p.expr().Select(BuildExpr).ToList();
                return elems.Count == 1
                    ? elems[0]
                    : new AstParen(Span(p), elems);

            default:
                Error("AST040", "Unknown atom form.", Span(ctx));
                return new AstIdent(Span(ctx), "<error>");
        }
    }

    private AstExpr BuildLiteral(LiteralContext ctx)
    {
        if (ctx.INT_LIT() is { } intTok)
        {
            return new AstIntLit(Span(ctx), int.Parse(intTok.GetText()));
        }
        if (ctx.STRING_LIT() is { } strTok)
        {
            return new AstStringLit(Span(ctx), UnescapeStringLiteral(strTok.GetText()));
        }
        Error("AST050", "Unknown literal form.", Span(ctx));
        return new AstIdent(Span(ctx), "<error>");
    }

    private static string UnescapeStringLiteral(string raw)
    {
        // Strip the surrounding quotes.
        if (raw.Length < 2) return raw;
        var inner = raw.Substring(1, raw.Length - 2);
        // Handle common escapes: \n, \r, \t, \\, \", \0.
        var sb = new System.Text.StringBuilder(inner.Length);
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '\\' && i + 1 < inner.Length)
            {
                char next = inner[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    '0' => '\0',
                    _ => next,
                });
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private AstLambda BuildLambda(LambdaContext ctx)
    {
        switch (ctx)
        {
            case LambdaSingleContext s:
                return new AstLambda(
                    Span(s),
                    new[] { s.name().GetText() },
                    BuildExpr(s.expr()));

            case LambdaMultiContext m:
                var names = m.name().Select(n => n.GetText()).ToList();
                return new AstLambda(Span(m), names, BuildExpr(m.expr()));

            default:
                Error("AST060", "Unknown lambda form.", Span(ctx));
                return new AstLambda(Span(ctx), Array.Empty<string>(),
                                     new AstIdent(Span(ctx), "<error>"));
        }
    }

    private AstBraceExpr BuildBraceExpr(BraceExprContext ctx)
    {
        var entries = ctx.braceEntry().Select(BuildBraceEntry).ToList();
        return new AstBraceExpr(Span(ctx), entries);
    }

    private AstBraceEntry BuildBraceEntry(BraceEntryContext ctx)
    {
        if (ctx.field() is { } f) return new AstBraceField(Span(ctx), BuildField(f));
        if (ctx.expr() is { } e) return new AstBraceValue(Span(ctx), BuildExpr(e));
        Error("AST070", "Unknown brace-entry form.", Span(ctx));
        return new AstBraceValue(Span(ctx), new AstIdent(Span(ctx), "<error>"));
    }

    private AstExpr BuildListOrRange(ListOrRangeContext ctx)
    {
        switch (ctx)
        {
            case ListRangeContext r:
                return new AstRangeLit(
                    Span(r),
                    BuildExpr(r.expr(0)),
                    BuildExpr(r.expr(1)));

            case ListLiteralContext l:
                return new AstListLit(Span(l), l.expr().Select(BuildExpr).ToList());

            default:
                Error("AST080", "Unknown list-or-range form.", Span(ctx));
                return new AstListLit(Span(ctx), Array.Empty<AstExpr>());
        }
    }

    private AstIfExpr BuildIfExpr(IfExprContext ctx) =>
        new(Span(ctx),
            BuildExpr(ctx.expr(0)),
            BuildExpr(ctx.expr(1)),
            BuildExpr(ctx.expr(2)));

    private AstSwitchExpr BuildSwitchExpr(SwitchExprContext ctx)
    {
        var scrutinee = BuildExpr(ctx.expr());
        var cases = ctx.switchCase().Select(BuildSwitchCase).ToList();
        return new AstSwitchExpr(Span(ctx), scrutinee, cases);
    }

    private AstSwitchCase BuildSwitchCase(SwitchCaseContext ctx) =>
        new(Span(ctx), ctx.name().GetText(), BuildExpr(ctx.expr()));

    private AstCondExpr BuildCondExpr(CondExprContext ctx)
    {
        var arms = ctx.condArm().Select(BuildCondArm).ToList();
        return new AstCondExpr(Span(ctx), arms);
    }

    private AstCondArm BuildCondArm(CondArmContext ctx) =>
        new(Span(ctx), BuildExpr(ctx.expr(0)), BuildExpr(ctx.expr(1)));

    private AstWhenExpr BuildWhenExpr(WhenExprContext ctx)
    {
        var pred = BuildExpr(ctx.expr(0));
        var effect = BuildExpr(ctx.expr(1));
        var options = ctx.whenOpt().Select(BuildWhenOpt).ToList();
        return new AstWhenExpr(Span(ctx), pred, effect, options);
    }

    private AstWhenOpt BuildWhenOpt(WhenOptContext ctx) =>
        new(Span(ctx), ctx.name().GetText(), BuildExpr(ctx.expr()));

    private AstLetExpr BuildLetExpr(LetExprContext ctx) =>
        new(Span(ctx),
            ctx.name().GetText(),
            BuildExpr(ctx.expr(0)),
            BuildExpr(ctx.expr(1)));

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fold a left-associative infix chain where <paramref name="ops"/> are
    /// the infix tokens between each pair of <paramref name="operands"/>. The
    /// operand parse contexts are yielded one at a time via
    /// <paramref name="build"/>.
    /// </summary>
    private AstExpr FoldLeftBinary<T>(
        T[] operands,
        ITerminalNode[] ops,
        Func<T, AstExpr> build) where T : ParserRuleContext
    {
        AstExpr acc = build(operands[0]);
        for (int i = 1; i < operands.Length; i++)
        {
            var op = ops[i - 1].GetText();
            acc = new AstBinaryOp(Span(operands[0]), op, acc, build(operands[i]));
        }
        return acc;
    }

    /// <summary>
    /// For add/mul exprs where the grammar embeds `(PLUS | MINUS)` between
    /// child contexts, find the token immediately preceding
    /// <paramref name="child"/> among the parent's children. Returns the
    /// token's text or null if none.
    /// </summary>
    private static string? GetTerminalBefore(ParserRuleContext parent, IParseTree child)
    {
        for (int i = 0; i < parent.ChildCount; i++)
        {
            if (ReferenceEquals(parent.GetChild(i), child))
            {
                if (i > 0 && parent.GetChild(i - 1) is ITerminalNode term)
                {
                    return term.GetText();
                }
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// For relExpr — find the single infix operator token (one of
    /// EQ_EQ, NEQ, LE, GE, LT, GT, ELEMENT_OF, SUBSETEQ).
    /// </summary>
    private static string? FindOperator(RelExprContext ctx)
    {
        for (int i = 0; i < ctx.ChildCount; i++)
        {
            if (ctx.GetChild(i) is ITerminalNode term)
            {
                return term.GetText();
            }
        }
        return null;
    }

    private static IEnumerable<IParseTree> Children(ParserRuleContext ctx)
    {
        for (int i = 0; i < ctx.ChildCount; i++) yield return ctx.GetChild(i);
    }

    private SourceSpan Span(ParserRuleContext ctx)
    {
        if (ctx.Start is null) return new SourceSpan(_file, 0, 0, 0, 0);
        var start = ctx.Start;
        var stop = ctx.Stop ?? start;
        int endCol = stop.Column + (stop.Text?.Length ?? 1);
        return new SourceSpan(
            _file,
            start.Line, start.Column + 1,
            stop.Line, endCol);
    }

    private SourceSpan Span(ITerminalNode term)
    {
        var tok = term.Symbol;
        return new SourceSpan(
            _file,
            tok.Line, tok.Column + 1,
            tok.Line, tok.Column + (tok.Text?.Length ?? 1));
    }

    private void Error(string code, string message, SourceSpan span) =>
        _diagnostics.Add(new Diagnostic(
            DiagnosticSeverity.Error, code, message,
            new SourcePosition(span.File, span.StartLine, span.StartColumn)));
}
