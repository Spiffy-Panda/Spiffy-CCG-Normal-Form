namespace Ccgnf.Preprocessing;

internal sealed class MacroTable
{
    private readonly Dictionary<string, MacroDefinition> _macros = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, MacroDefinition> All => _macros;

    public bool TryGet(string name, out MacroDefinition def) =>
        _macros.TryGetValue(name, out def!);

    public bool Contains(string name) => _macros.ContainsKey(name);

    public MacroDefinition? Get(string name) =>
        _macros.TryGetValue(name, out var d) ? d : null;

    /// <summary>
    /// Registers a macro. Returns false if a macro with the same name already exists;
    /// the caller is expected to emit a redefinition diagnostic in that case.
    /// </summary>
    public bool Register(MacroDefinition def)
    {
        if (_macros.ContainsKey(def.Name)) return false;
        _macros[def.Name] = def;
        return true;
    }
}
