using Ccgnf.Diagnostics;

namespace Ccgnf.Preprocessing;

/// <summary>
/// Token kinds recognized by the preprocessor's internal tokenizer. This is
/// deliberately coarse — we only need enough resolution to find `define`
/// directives, macro invocations, and argument boundaries.
/// Everything else (operators, punctuation not listed here) is routed
/// through <see cref="PpTokenKind.Other"/> with its literal text preserved.
/// </summary>
internal enum PpTokenKind
{
    Ident,
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBrack,
    RBrack,
    Comma,
    Eq,
    Int,
    String,
    LineComment,
    BlockComment,
    Whitespace,
    Newline,
    Other,
    Eof,
}

internal readonly record struct PpToken(PpTokenKind Kind, string Text, SourcePosition Pos)
{
    public static PpToken Eof(string file) =>
        new(PpTokenKind.Eof, string.Empty, new SourcePosition(file, 0, 0));
}
