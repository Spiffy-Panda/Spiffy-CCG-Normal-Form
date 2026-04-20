namespace Ccgnf.Bots.Bt;

/// <summary>
/// Stateless BT evaluator. Each <see cref="Apply"/> call re-enters the
/// tree from scratch — no Running status, no cooldowns. Conditions are
/// expressed as a small comparison DSL (<c>"turn_number &lt;= 3"</c>)
/// evaluated against the host's <see cref="IBtContext"/>.
/// <para>
/// Simpler than <c>reference-code/BehaviorTree/BtRunner.cs</c> — v1 needs
/// only Selector + ConditionGate + Condition + Action. Adding Sequence
/// or a decorator is a one-line change; added on demand, not now.
/// </para>
/// </summary>
public sealed class BtRunner
{
    private readonly IReadOnlyList<BtNode> _roots;

    public BtRunner(IReadOnlyList<BtNode> roots)
    {
        _roots = roots;
    }

    /// <summary>Tick every root against <paramref name="context"/>.</summary>
    public void Apply(IBtContext context)
    {
        foreach (var root in _roots)
            Tick(root, context);
    }

    private BtStatus Tick(BtNode node, IBtContext ctx) => node.Type switch
    {
        BtNodeType.Selector => TickSelector(node, ctx),
        BtNodeType.ConditionGate => TickConditionGate(node, ctx),
        BtNodeType.Condition => EvalCondition(node.Value ?? "", ctx) ? BtStatus.Success : BtStatus.Failure,
        BtNodeType.Action => ctx.ExecuteAction(node.Value ?? ""),
        _ => BtStatus.Failure,
    };

    private BtStatus TickSelector(BtNode node, IBtContext ctx)
    {
        if (node.Children is null) return BtStatus.Failure;
        foreach (var child in node.Children)
            if (Tick(child, ctx) == BtStatus.Success)
                return BtStatus.Success;
        return BtStatus.Failure;
    }

    private BtStatus TickConditionGate(BtNode node, IBtContext ctx)
    {
        if (!EvalCondition(node.Value ?? "", ctx)) return BtStatus.Failure;
        var child = node.Children?.FirstOrDefault();
        return child is null ? BtStatus.Success : Tick(child, ctx);
    }

    /// <summary>
    /// Tiny condition DSL: <c>always</c>, <c>never</c>, or a binary
    /// comparison between two number-or-variable atoms. Examples:
    /// <c>"turn_number &lt;= 3"</c>, <c>"min_own_conduit_integrity &lt;= 3"</c>.
    /// </summary>
    public static bool EvalCondition(string cond, IBtContext ctx)
    {
        if (string.IsNullOrWhiteSpace(cond)) return false;
        var c = cond.Trim().ToLowerInvariant();
        if (c is "always" or "true") return true;
        if (c is "never" or "false") return false;

        foreach (var op in new[] { "<=", ">=", "!=", "==", "<", ">" })
        {
            var idx = c.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;

            var lhs = c[..idx].Trim();
            var rhs = c[(idx + op.Length)..].Trim();
            float l = EvalAtom(lhs, ctx);
            float r = EvalAtom(rhs, ctx);

            return op switch
            {
                "<" => l < r,
                "<=" => l <= r,
                ">" => l > r,
                ">=" => l >= r,
                "==" => MathF.Abs(l - r) < 0.001f,
                "!=" => MathF.Abs(l - r) >= 0.001f,
                _ => false,
            };
        }
        return false;
    }

    private static float EvalAtom(string atom, IBtContext ctx)
    {
        atom = atom.Trim();
        if (float.TryParse(atom, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var num))
            return num;
        return ctx.ResolveVariable(atom);
    }
}
