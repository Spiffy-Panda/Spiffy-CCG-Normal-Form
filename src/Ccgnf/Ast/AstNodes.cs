using Ccgnf.Diagnostics;

namespace Ccgnf.Ast;

// -----------------------------------------------------------------------------
// AST record hierarchy for the CCGNF grammar.
//
// Design:
//   * Everything is an immutable `record`. Equality is structural.
//   * Every node carries a SourceSpan so diagnostics can anchor back to the
//     parse location.
//   * Abstract bases are annotated `abstract`; concrete types are `sealed`.
//   * Collections are exposed as IReadOnlyList<T>.
//
// The shape mirrors the grammar in src/Ccgnf/Grammar/Ccgnf.g4; grep that file
// for rule names when extending this hierarchy.
// -----------------------------------------------------------------------------

public abstract record AstNode(SourceSpan Span);

// ---------- File and declarations ----------

public sealed record AstFile(
    SourceSpan Span,
    IReadOnlyList<AstDeclaration> Declarations) : AstNode(Span);

public abstract record AstDeclaration(SourceSpan Span) : AstNode(Span)
{
    /// <summary>The declared name (entity, card, or token name; or the
    /// fully-qualified head for augmentations).</summary>
    public abstract string DisplayName { get; }
}

public sealed record AstEntityDecl(
    SourceSpan Span,
    string Name,
    IReadOnlyList<string> IndexParams,
    AstForClause? ForClause,
    AstBlock Body) : AstDeclaration(Span)
{
    public override string DisplayName => Name;
}

public sealed record AstCardDecl(
    SourceSpan Span,
    string Name,
    AstBlock Body) : AstDeclaration(Span)
{
    public override string DisplayName => Name;
}

public sealed record AstTokenDecl(
    SourceSpan Span,
    string Name,
    AstBlock Body) : AstDeclaration(Span)
{
    public override string DisplayName => Name;
}

public sealed record AstEntityAugment(
    SourceSpan Span,
    AstTargetPath Target,
    AstExpr Value) : AstDeclaration(Span)
{
    public override string DisplayName => Target.DisplayPath;
}

// ---------- Structural pieces ----------

public sealed record AstForClause(
    SourceSpan Span,
    string Variable,
    AstExpr Source) : AstNode(Span);

public sealed record AstBlock(
    SourceSpan Span,
    IReadOnlyList<AstField> Fields) : AstNode(Span);

public sealed record AstField(
    SourceSpan Span,
    AstFieldKey Key,
    AstFieldValue Value) : AstNode(Span);

public sealed record AstFieldKey(
    SourceSpan Span,
    string Name,
    IReadOnlyList<AstExpr> Indices) : AstNode(Span);

public abstract record AstFieldValue(SourceSpan Span) : AstNode(Span);

public sealed record AstFieldBlock(
    SourceSpan Span,
    AstBlock Block) : AstFieldValue(Span);

public sealed record AstFieldTyped(
    SourceSpan Span,
    string TypeName,
    AstExpr Value) : AstFieldValue(Span);

public sealed record AstFieldExpr(
    SourceSpan Span,
    AstExpr Value) : AstFieldValue(Span);

public sealed record AstTargetPath(
    SourceSpan Span,
    IReadOnlyList<AstTargetSegment> Segments) : AstNode(Span)
{
    public string DisplayPath =>
        string.Join(".", Segments.Select(s =>
            s.Indices.Count == 0
                ? s.Name
                : $"{s.Name}[{string.Join(",", s.Indices.Select(_ => "…"))}]"));
}

public sealed record AstTargetSegment(
    SourceSpan Span,
    string Name,
    IReadOnlyList<AstExpr> Indices) : AstNode(Span);

// ---------- Expressions ----------

public abstract record AstExpr(SourceSpan Span) : AstNode(Span);

public sealed record AstIntLit(SourceSpan Span, int Value) : AstExpr(Span);

public sealed record AstStringLit(SourceSpan Span, string Value) : AstExpr(Span);

public sealed record AstIdent(SourceSpan Span, string Name) : AstExpr(Span);

public sealed record AstBinaryOp(
    SourceSpan Span,
    string Op,
    AstExpr Left,
    AstExpr Right) : AstExpr(Span);

public sealed record AstUnaryOp(
    SourceSpan Span,
    string Op,
    AstExpr Operand) : AstExpr(Span);

public sealed record AstMemberAccess(
    SourceSpan Span,
    AstExpr Target,
    string Member) : AstExpr(Span);

public sealed record AstIndex(
    SourceSpan Span,
    AstExpr Target,
    IReadOnlyList<AstExpr> Indices) : AstExpr(Span);

public sealed record AstFunctionCall(
    SourceSpan Span,
    AstExpr Callee,
    IReadOnlyList<AstArg> Args) : AstExpr(Span);

public sealed record AstLambda(
    SourceSpan Span,
    IReadOnlyList<string> Parameters,
    AstExpr Body) : AstExpr(Span);

public sealed record AstParen(
    SourceSpan Span,
    IReadOnlyList<AstExpr> Elements) : AstExpr(Span);

public sealed record AstBraceExpr(
    SourceSpan Span,
    IReadOnlyList<AstBraceEntry> Entries) : AstExpr(Span);

public sealed record AstListLit(
    SourceSpan Span,
    IReadOnlyList<AstExpr> Elements) : AstExpr(Span);

public sealed record AstRangeLit(
    SourceSpan Span,
    AstExpr Start,
    AstExpr End) : AstExpr(Span);

public sealed record AstIfExpr(
    SourceSpan Span,
    AstExpr Condition,
    AstExpr Then,
    AstExpr Else) : AstExpr(Span);

public sealed record AstSwitchExpr(
    SourceSpan Span,
    AstExpr Scrutinee,
    IReadOnlyList<AstSwitchCase> Cases) : AstExpr(Span);

public sealed record AstSwitchCase(
    SourceSpan Span,
    string Label,
    AstExpr Value) : AstNode(Span);

public sealed record AstCondExpr(
    SourceSpan Span,
    IReadOnlyList<AstCondArm> Arms) : AstExpr(Span);

public sealed record AstCondArm(
    SourceSpan Span,
    AstExpr Predicate,
    AstExpr Effect) : AstNode(Span);

public sealed record AstWhenExpr(
    SourceSpan Span,
    AstExpr Predicate,
    AstExpr Effect,
    IReadOnlyList<AstWhenOpt> Options) : AstExpr(Span);

public sealed record AstWhenOpt(
    SourceSpan Span,
    string Name,
    AstExpr Value) : AstNode(Span);

public sealed record AstLetExpr(
    SourceSpan Span,
    string Variable,
    AstExpr Value,
    AstExpr Body) : AstExpr(Span);

// ---------- Brace entries (fields or bare exprs) ----------

public abstract record AstBraceEntry(SourceSpan Span) : AstNode(Span);

public sealed record AstBraceField(
    SourceSpan Span,
    AstField Field) : AstBraceEntry(Span);

public sealed record AstBraceValue(
    SourceSpan Span,
    AstExpr Value) : AstBraceEntry(Span);

// ---------- Arguments ----------

public abstract record AstArg(SourceSpan Span) : AstNode(Span);

public sealed record AstArgPositional(
    SourceSpan Span,
    AstExpr Value) : AstArg(Span);

public sealed record AstArgNamed(
    SourceSpan Span,
    string Name,
    AstExpr Value) : AstArg(Span);

public sealed record AstArgBinding(
    SourceSpan Span,
    string Name,
    AstExpr Value) : AstArg(Span);
