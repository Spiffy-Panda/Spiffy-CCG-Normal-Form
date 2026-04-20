using System.Text.Json.Serialization;

namespace Ccgnf.Bots.Bt;

/// <summary>
/// One node in the phase-selection behaviour tree. The tree is
/// intent-selection only — leaves produce an intent label via
/// <see cref="BtNodeType.Action"/>, not a game move. (Every real move
/// goes through <see cref="Utility.UtilityBot"/>.)
/// <para>
/// Ported from <c>reference-code/BehaviorTree/BtCore.cs</c> with the
/// composite set pared down: we don't use Parallel, Repeater, Cooldown,
/// or Inverter in v1. Keeping them out keeps the JSON schema small and
/// the evaluator trivial.
/// </para>
/// </summary>
public sealed record BtNode(
    BtNodeType Type,
    string? Value = null,
    List<BtNode>? Children = null,
    string? Comment = null)
{
    public static BtNode Sel(params BtNode[] children) =>
        new(BtNodeType.Selector, Children: children.ToList());

    public static BtNode Gate(string condition, BtNode child) =>
        new(BtNodeType.ConditionGate, Value: condition, Children: new List<BtNode> { child });

    public static BtNode Act(string value) =>
        new(BtNodeType.Action, Value: value);

    public static BtNode Cond(string expr) =>
        new(BtNodeType.Condition, Value: expr);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BtNodeType
{
    Selector,
    ConditionGate,
    Condition,
    Action,
}

public enum BtStatus { Success, Failure }
