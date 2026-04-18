namespace Ccgnf.Validation;

/// <summary>
/// Signatures for well-known builtin functions the Validator checks arity
/// against. Entries here are a best-effort catalog, not exhaustive — unknown
/// callees are tolerated (treated as user-defined or future builtins) rather
/// than errored.
/// </summary>
internal static class BuiltinSignatures
{
    /// <summary>A signature constraint: either a fixed arity or a range.</summary>
    internal sealed record Arity(int Min, int Max)
    {
        public static Arity Exact(int n) => new(n, n);
        public static Arity AtLeast(int n) => new(n, int.MaxValue);
        public static Arity Between(int lo, int hi) => new(lo, hi);
        public bool Accepts(int n) => n >= Min && n <= Max;

        public override string ToString() =>
            Min == Max ? $"{Min}"
            : Max == int.MaxValue ? $"at least {Min}"
            : $"{Min}..{Max}";
    }

    /// <summary>
    /// Known builtins with their expected argument arity. The Validator only
    /// flags arity on names that appear in this table; anything else is either
    /// a user-defined macro (already expanded away by the preprocessor) or a
    /// future builtin we haven't catalogued yet.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Arity> Known =
        new Dictionary<string, Arity>
        {
            // Combinators from common/03-combinators.ccgnf
            ["Sequence"]      = Arity.Exact(1),     // takes one list
            ["Repeat"]        = Arity.Exact(2),
            ["ForEach"]       = Arity.Exact(2),
            ["If"]            = Arity.Exact(3),
            ["Cond"]          = Arity.Exact(1),
            ["Switch"]        = Arity.Exact(2),
            ["Choice"]        = Arity.AtLeast(1),
            ["Target"]        = Arity.AtLeast(1),
            ["NoOp"]          = Arity.Exact(0),

            // Atomic ops
            ["DealDamage"]    = Arity.Exact(2),
            ["Heal"]          = Arity.Between(2, 3),
            ["Draw"]          = Arity.Between(1, 2),
            ["Mill"]          = Arity.Exact(2),
            ["MoveTo"]        = Arity.Between(2, 3),
            ["PayAether"]     = Arity.Exact(2),
            ["IncCounter"]    = Arity.Exact(3),
            ["SetCounter"]    = Arity.Exact(3),
            ["ClearCounter"]  = Arity.Exact(2),
            ["SetCharacteristic"] = Arity.Exact(3),
            ["SetFlag"]       = Arity.Between(2, 3),    // (flag, value) or (entity, flag, value)
            ["CreateToken"]   = Arity.AtLeast(1),
            ["DestroyEntity"] = Arity.Exact(1),
            ["EmitEvent"]     = Arity.Exact(1),
            ["ScheduleAt"]    = Arity.Exact(2),
            ["Guard"]         = Arity.AtLeast(1),

            // Resonance helpers
            ["Resonance"]     = Arity.Exact(2),
            ["Peak"]          = Arity.Exact(1),
            ["Banner"]        = Arity.Exact(1),
            ["BannerExists"]  = Arity.Exact(0),
            ["CountEcho"]     = Arity.Exact(1),
            ["Tiers"]         = Arity.Exact(1),
            ["Lines"]         = Arity.Exact(1),
            ["PushEcho"]      = Arity.Exact(1),
            ["ReduceBaseCost"] = Arity.Exact(2),
        };
}
