using Ccgnf.Ast;
using Ccgnf.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Validation;

/// <summary>
/// Semantic checks over a validated AST. The v1 Validator covers:
///
///   V100  Duplicate top-level declaration name.
///   V101  Duplicate Card/Token definition name.
///   V200  Unknown builtin arity (based on <see cref="BuiltinSignatures"/>).
///   V300  R-5 compliance: Debt computation must use <c>printed_cost</c>,
///         not <c>effective_cost</c> — anchors GrammarSpec §0 ruling R-5.
///   V400  Forbidden construct: <c>Procedure</c> identifier at top level
///         (defense in depth — the grammar already rejects this token).
///
/// More sophisticated checks (R-1..R-4, R-6; once-per-turn sanity; type flow)
/// are out of scope for v1 and tracked in grammar/GrammarSpec.md §12.
/// </summary>
public sealed class Validator
{
    private readonly ILogger<Validator> _log;

    public Validator(ILogger<Validator>? log = null)
    {
        _log = log ?? NullLogger<Validator>.Instance;
    }

    public ValidationResult Validate(AstFile file)
    {
        var diags = new List<Diagnostic>();

        CheckDuplicateDeclarations(file, diags);
        var walker = new SemanticWalker(diags);
        walker.Walk(file);

        _log.LogInformation(
            "Validator: {DeclCount} declarations inspected, {DiagCount} diagnostics",
            file.Declarations.Count, diags.Count);

        return new ValidationResult(diags);
    }

    private static void CheckDuplicateDeclarations(AstFile file, List<Diagnostic> diags)
    {
        // Entities/Cards/Tokens live in one namespace for purposes of this
        // check — we don't let a Card and an Entity share a name, because
        // that would make cross-references ambiguous for the Interpreter.
        // Augmentations target an existing entity and don't declare a new
        // symbol, so they are not tracked here.
        var seen = new Dictionary<string, AstDeclaration>(StringComparer.Ordinal);
        foreach (var decl in file.Declarations)
        {
            string? name = decl switch
            {
                AstEntityDecl e => e.Name,
                AstCardDecl c   => c.Name,
                AstTokenDecl t  => t.Name,
                _ => null,
            };
            if (name is null) continue;

            if (seen.TryGetValue(name, out var prior))
            {
                diags.Add(Err(
                    priorIsCard: prior is AstCardDecl || prior is AstTokenDecl,
                    code: (prior, decl) switch
                    {
                        (AstCardDecl, AstCardDecl) => "V101",
                        (AstTokenDecl, AstTokenDecl) => "V101",
                        _ => "V100",
                    },
                    message: $"Duplicate top-level name '{name}' " +
                             $"(first defined at {prior.Span}).",
                    span: decl.Span));
            }
            else
            {
                seen[name] = decl;
            }
        }
    }

    private static Diagnostic Err(bool priorIsCard, string code, string message, SourceSpan span) =>
        new(DiagnosticSeverity.Error, code, message,
            new SourcePosition(span.File, span.StartLine, span.StartColumn));

    // -------------------------------------------------------------------------
    // Expression-level walker
    // -------------------------------------------------------------------------

    private sealed class SemanticWalker
    {
        private readonly List<Diagnostic> _diags;

        public SemanticWalker(List<Diagnostic> diags) { _diags = diags; }

        public void Walk(AstFile file)
        {
            foreach (var decl in file.Declarations) WalkDeclaration(decl);
        }

        private void WalkDeclaration(AstDeclaration d)
        {
            switch (d)
            {
                case AstEntityDecl e:
                    WalkBlock(e.Body);
                    if (e.ForClause is not null) WalkExpr(e.ForClause.Source);
                    break;
                case AstCardDecl c:
                    WalkBlock(c.Body);
                    break;
                case AstTokenDecl t:
                    WalkBlock(t.Body);
                    break;
                case AstEntityAugment a:
                    WalkExpr(a.Value);
                    break;
            }
        }

        private void WalkBlock(AstBlock b)
        {
            foreach (var f in b.Fields) WalkField(f);
        }

        private void WalkField(AstField f)
        {
            foreach (var idx in f.Key.Indices) WalkExpr(idx);
            WalkFieldValue(f.Value);
        }

        private void WalkFieldValue(AstFieldValue v)
        {
            switch (v)
            {
                case AstFieldBlock fb: WalkBlock(fb.Block); break;
                case AstFieldTyped ft: WalkExpr(ft.Value); break;
                case AstFieldExpr fe:  WalkExpr(fe.Value); break;
            }
        }

        private void WalkExpr(AstExpr expr)
        {
            switch (expr)
            {
                case AstIntLit:
                case AstStringLit:
                    break;

                case AstIdent id:
                    CheckProcedureKeyword(id);
                    break;

                case AstBinaryOp b:
                    WalkExpr(b.Left);
                    WalkExpr(b.Right);
                    break;

                case AstUnaryOp u:
                    WalkExpr(u.Operand);
                    break;

                case AstMemberAccess m:
                    WalkExpr(m.Target);
                    break;

                case AstIndex idx:
                    WalkExpr(idx.Target);
                    foreach (var i in idx.Indices) WalkExpr(i);
                    break;

                case AstFunctionCall call:
                    WalkCall(call);
                    break;

                case AstLambda la:
                    WalkExpr(la.Body);
                    break;

                case AstParen p:
                    foreach (var e in p.Elements) WalkExpr(e);
                    break;

                case AstBraceExpr be:
                    foreach (var entry in be.Entries) WalkBraceEntry(entry);
                    break;

                case AstListLit ll:
                    foreach (var e in ll.Elements) WalkExpr(e);
                    break;

                case AstRangeLit rl:
                    WalkExpr(rl.Start);
                    WalkExpr(rl.End);
                    break;

                case AstIfExpr ie:
                    WalkExpr(ie.Condition);
                    WalkExpr(ie.Then);
                    WalkExpr(ie.Else);
                    break;

                case AstSwitchExpr sw:
                    WalkExpr(sw.Scrutinee);
                    foreach (var c in sw.Cases) WalkExpr(c.Value);
                    break;

                case AstCondExpr cond:
                    foreach (var arm in cond.Arms)
                    {
                        WalkExpr(arm.Predicate);
                        WalkExpr(arm.Effect);
                    }
                    break;

                case AstWhenExpr when_:
                    WalkExpr(when_.Predicate);
                    WalkExpr(when_.Effect);
                    foreach (var o in when_.Options) WalkExpr(o.Value);
                    break;

                case AstLetExpr let:
                    WalkExpr(let.Value);
                    WalkExpr(let.Body);
                    break;
            }
        }

        private void WalkBraceEntry(AstBraceEntry e)
        {
            switch (e)
            {
                case AstBraceField bf: WalkField(bf.Field); break;
                case AstBraceValue bv: WalkExpr(bv.Value); break;
            }
        }

        private void WalkCall(AstFunctionCall call)
        {
            // Arity check if the callee is a bare builtin identifier.
            if (call.Callee is AstIdent callee &&
                BuiltinSignatures.Known.TryGetValue(callee.Name, out var arity))
            {
                int positional = call.Args.Count(a => a is AstArgPositional);
                int named      = call.Args.Count(a => a is AstArgNamed);
                int bindings   = call.Args.Count(a => a is AstArgBinding);
                int total      = positional + named + bindings;
                if (!arity.Accepts(total))
                {
                    _diags.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "V200",
                        $"Builtin '{callee.Name}' expects {arity} argument(s); got {total}.",
                        new SourcePosition(call.Span.File, call.Span.StartLine, call.Span.StartColumn)));
                }
            }

            // R-5: Debt uses printed_cost, never effective_cost. Flag any
            // DealDamage / IncCounter / raw arithmetic inside a Debt-like
            // context that reads `effective_cost` as a member. A full
            // data-flow check is out of scope; we do the conservative
            // "does any subtree multiply something named *effective_cost* by 2"
            // pattern scan and raise V300 on match. Conservative = may
            // produce false positives; the author can suppress by renaming.
            R5Check(call);

            WalkExpr(call.Callee);
            foreach (var arg in call.Args)
            {
                switch (arg)
                {
                    case AstArgPositional p: WalkExpr(p.Value); break;
                    case AstArgNamed n:      WalkExpr(n.Value); break;
                    case AstArgBinding b:    WalkExpr(b.Value); break;
                }
            }
        }

        private void R5Check(AstFunctionCall call)
        {
            // The pattern we're forbidding is `IncCounter(_, debt, 2 *
            // effective_cost)` or any arithmetic on `effective_cost` that
            // flows into a Debt accrual. As a conservative proxy, flag a
            // binary `*` whose operand is a member access ending in
            // `.effective_cost` and whose parent call names IncCounter or
            // is an IncCounter argument tree. Since we only see this call,
            // limit detection to IncCounter(_, debt, 2 * something.effective_cost).
            if (call.Callee is not AstIdent id) return;
            if (id.Name != "IncCounter") return;
            if (call.Args.Count < 3) return;

            // Identify the counter-name argument. In positional form it's
            // the second; in named form named "counter" (rare here).
            bool targetsDebt = false;
            foreach (var arg in call.Args)
            {
                if (arg is AstArgPositional { Value: AstIdent { Name: "debt" } }) targetsDebt = true;
                if (arg is AstArgNamed named && named.Value is AstIdent { Name: "debt" }) targetsDebt = true;
            }
            if (!targetsDebt) return;

            // Look for a binary * that multiplies by `effective_cost`.
            foreach (var arg in call.Args)
            {
                AstExpr? value = arg switch
                {
                    AstArgPositional p => p.Value,
                    AstArgNamed n => n.Value,
                    AstArgBinding b => b.Value,
                    _ => null,
                };
                if (value is null) continue;
                if (ContainsEffectiveCostArithmetic(value))
                {
                    _diags.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        "V300",
                        "Debt computation must use printed_cost, not effective_cost " +
                        "(GrammarSpec §0 ruling R-5).",
                        new SourcePosition(call.Span.File, call.Span.StartLine, call.Span.StartColumn)));
                    return;
                }
            }
        }

        private static bool ContainsEffectiveCostArithmetic(AstExpr expr)
        {
            switch (expr)
            {
                case AstBinaryOp b:
                    return HasEffectiveCostMemberAccess(b.Left)
                        || HasEffectiveCostMemberAccess(b.Right)
                        || ContainsEffectiveCostArithmetic(b.Left)
                        || ContainsEffectiveCostArithmetic(b.Right);
                case AstUnaryOp u:
                    return ContainsEffectiveCostArithmetic(u.Operand);
                case AstFunctionCall call:
                    return call.Args.Any(a => a switch
                    {
                        AstArgPositional p => ContainsEffectiveCostArithmetic(p.Value),
                        AstArgNamed n => ContainsEffectiveCostArithmetic(n.Value),
                        AstArgBinding bb => ContainsEffectiveCostArithmetic(bb.Value),
                        _ => false,
                    });
                default:
                    return false;
            }
        }

        private static bool HasEffectiveCostMemberAccess(AstExpr expr) =>
            expr is AstMemberAccess m && m.Member == "effective_cost";

        private void CheckProcedureKeyword(AstIdent id)
        {
            if (id.Name == "Procedure")
            {
                _diags.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "V400",
                    "'Procedure' is a forbidden construct; game logic is " +
                    "expressed as abilities on entities, not top-level procedures " +
                    "(GrammarSpec §5.4).",
                    new SourcePosition(id.Span.File, id.Span.StartLine, id.Span.StartColumn)));
            }
        }
    }
}
