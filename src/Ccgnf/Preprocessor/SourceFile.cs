namespace Ccgnf.Preprocessing;

/// <summary>
/// A source file available to the preprocessor. Held as a string in memory;
/// we do not stream.
/// </summary>
public sealed record SourceFile(string Path, string Text);
