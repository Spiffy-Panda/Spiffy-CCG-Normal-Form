namespace Ccgnf.Interpreter;

/// <summary>
/// Lexical environment for expression evaluation. A chain of frames: each
/// frame holds bindings introduced by a lambda parameter or a <c>let</c>, and
/// parent chains eventually reach the empty root. Lookup walks up the chain;
/// binding creates a new frame so child scopes don't mutate parents.
/// </summary>
public sealed class RtEnv
{
    public static readonly RtEnv Empty = new(null, new Dictionary<string, RtValue>(0));

    private readonly RtEnv? _parent;
    private readonly Dictionary<string, RtValue> _bindings;

    private RtEnv(RtEnv? parent, Dictionary<string, RtValue> bindings)
    {
        _parent = parent;
        _bindings = bindings;
    }

    public bool TryLookup(string name, out RtValue value)
    {
        for (var env = this; env is not null; env = env._parent)
        {
            if (env._bindings.TryGetValue(name, out var v))
            {
                value = v;
                return true;
            }
        }
        value = null!;
        return false;
    }

    public RtEnv Extend(string name, RtValue value)
    {
        var frame = new Dictionary<string, RtValue>(1) { [name] = value };
        return new RtEnv(this, frame);
    }

    public RtEnv Extend(IReadOnlyList<(string Name, RtValue Value)> pairs)
    {
        var frame = new Dictionary<string, RtValue>(pairs.Count);
        foreach (var (n, v) in pairs) frame[n] = v;
        return new RtEnv(this, frame);
    }
}
