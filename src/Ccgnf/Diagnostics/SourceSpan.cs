namespace Ccgnf.Diagnostics;

/// <summary>
/// A contiguous range of source, used by AST nodes to anchor themselves back to
/// their parse location. Lines and columns are 1-based to match editor and
/// diagnostic conventions; End positions are inclusive of the last character
/// of the construct, except in the zero-width case (where Start equals End).
/// </summary>
public readonly record struct SourceSpan(
    string File,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn)
{
    public SourcePosition Start => new(File, StartLine, StartColumn);

    public static SourceSpan Unknown { get; } =
        new("<unknown>", 0, 0, 0, 0);

    public override string ToString() =>
        StartLine == EndLine && StartColumn == EndColumn
            ? $"{File}:{StartLine}:{StartColumn}"
            : $"{File}:{StartLine}:{StartColumn}-{EndLine}:{EndColumn}";
}
