namespace Ccgnf.Diagnostics;

/// <summary>
/// Identifies a specific position in a source file. Line and column are
/// 1-based to match editor conventions; 0-based offsets are not tracked at
/// this level.
/// </summary>
public readonly record struct SourcePosition(string File, int Line, int Column)
{
    public override string ToString() => $"{File}:{Line}:{Column}";

    public static SourcePosition Unknown { get; } = new("<unknown>", 0, 0);
}
