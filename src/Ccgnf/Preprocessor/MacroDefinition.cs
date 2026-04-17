using Ccgnf.Diagnostics;

namespace Ccgnf.Preprocessing;

/// <summary>
/// A single <c>define NAME(params) = body</c> directive, captured as a list of
/// tokens with the original source position of each.
/// </summary>
internal sealed class MacroDefinition
{
    public string Name { get; }
    public IReadOnlyList<string> Parameters { get; }
    public IReadOnlyList<PpToken> Body { get; }
    public SourcePosition Position { get; }

    public MacroDefinition(
        string name,
        IReadOnlyList<string> parameters,
        IReadOnlyList<PpToken> body,
        SourcePosition position)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
        Position = position;
    }

    public int Arity => Parameters.Count;

    public override string ToString() =>
        $"{Name}({string.Join(", ", Parameters)}) at {Position}";
}
