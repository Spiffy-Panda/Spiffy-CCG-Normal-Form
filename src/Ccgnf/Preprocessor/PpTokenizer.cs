using System.Text;
using Ccgnf.Diagnostics;

namespace Ccgnf.Preprocessing;

/// <summary>
/// Hand-rolled tokenizer used by the preprocessor only. This is intentionally
/// separate from the ANTLR lexer: the preprocessor needs token granularity
/// just fine enough to recognize `define` directives and macro invocations.
/// All non-recognized characters pass through as <see cref="PpTokenKind.Other"/>
/// so that arbitrary expression text can be substituted verbatim.
/// </summary>
internal sealed class PpTokenizer
{
    private readonly string _text;
    private readonly string _file;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public PpTokenizer(SourceFile source)
    {
        _text = source.Text;
        _file = source.Path;
    }

    public List<PpToken> Tokenize()
    {
        var tokens = new List<PpToken>();
        while (_pos < _text.Length)
        {
            var t = NextToken();
            tokens.Add(t);
        }
        tokens.Add(PpToken.Eof(_file));
        return tokens;
    }

    private PpToken NextToken()
    {
        int startLine = _line;
        int startCol = _col;
        char c = _text[_pos];

        // --- Whitespace / newlines -----------------------------------------
        if (c == '\n')
        {
            Advance();
            return Token(PpTokenKind.Newline, "\n", startLine, startCol);
        }
        if (c == '\r')
        {
            Advance();
            if (_pos < _text.Length && _text[_pos] == '\n') Advance();
            return Token(PpTokenKind.Newline, "\n", startLine, startCol);
        }
        if (c is ' ' or '\t')
        {
            var sb = new StringBuilder();
            while (_pos < _text.Length && _text[_pos] is ' ' or '\t')
            {
                sb.Append(_text[_pos]);
                Advance();
            }
            return Token(PpTokenKind.Whitespace, sb.ToString(), startLine, startCol);
        }

        // --- Comments ------------------------------------------------------
        if (c == '/' && Peek(1) == '/')
        {
            var sb = new StringBuilder();
            while (_pos < _text.Length && _text[_pos] != '\n' && _text[_pos] != '\r')
            {
                sb.Append(_text[_pos]);
                Advance();
            }
            return Token(PpTokenKind.LineComment, sb.ToString(), startLine, startCol);
        }
        if (c == '/' && Peek(1) == '*')
        {
            var sb = new StringBuilder();
            sb.Append(_text[_pos]); Advance();
            sb.Append(_text[_pos]); Advance();
            while (_pos < _text.Length && !(_text[_pos] == '*' && Peek(1) == '/'))
            {
                sb.Append(_text[_pos]);
                Advance();
            }
            if (_pos < _text.Length) { sb.Append(_text[_pos]); Advance(); }
            if (_pos < _text.Length) { sb.Append(_text[_pos]); Advance(); }
            return Token(PpTokenKind.BlockComment, sb.ToString(), startLine, startCol);
        }

        // --- String literals ----------------------------------------------
        if (c == '"')
        {
            var sb = new StringBuilder();
            sb.Append(_text[_pos]); Advance();
            while (_pos < _text.Length && _text[_pos] != '"')
            {
                if (_text[_pos] == '\\' && _pos + 1 < _text.Length)
                {
                    sb.Append(_text[_pos]); Advance();
                    sb.Append(_text[_pos]); Advance();
                }
                else
                {
                    if (_text[_pos] == '\n') break; // unterminated; let parser flag
                    sb.Append(_text[_pos]);
                    Advance();
                }
            }
            if (_pos < _text.Length && _text[_pos] == '"')
            {
                sb.Append(_text[_pos]); Advance();
            }
            return Token(PpTokenKind.String, sb.ToString(), startLine, startCol);
        }

        // --- Integer literals ---------------------------------------------
        if (char.IsDigit(c))
        {
            var sb = new StringBuilder();
            while (_pos < _text.Length && char.IsDigit(_text[_pos]))
            {
                sb.Append(_text[_pos]);
                Advance();
            }
            return Token(PpTokenKind.Int, sb.ToString(), startLine, startCol);
        }

        // --- Identifiers ---------------------------------------------------
        if (char.IsLetter(c) || c == '_')
        {
            var sb = new StringBuilder();
            while (_pos < _text.Length &&
                   (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
            {
                sb.Append(_text[_pos]);
                Advance();
            }
            return Token(PpTokenKind.Ident, sb.ToString(), startLine, startCol);
        }

        // --- Single-char punctuation we care about ------------------------
        PpToken? single = c switch
        {
            '(' => Token(PpTokenKind.LParen, "(", startLine, startCol),
            ')' => Token(PpTokenKind.RParen, ")", startLine, startCol),
            '{' => Token(PpTokenKind.LBrace, "{", startLine, startCol),
            '}' => Token(PpTokenKind.RBrace, "}", startLine, startCol),
            '[' => Token(PpTokenKind.LBrack, "[", startLine, startCol),
            ']' => Token(PpTokenKind.RBrack, "]", startLine, startCol),
            ',' => Token(PpTokenKind.Comma, ",", startLine, startCol),
            '=' => Token(PpTokenKind.Eq, "=", startLine, startCol),
            _ => null,
        };
        if (single is not null)
        {
            Advance();
            return single.Value;
        }

        // --- Fallthrough: emit as Other with preserved text ---------------
        // Capture multi-char operator-ish runs (e.g., `==`, `->`, `+=`, `!=`)
        // so invocations inside "Other" runs don't cross them unexpectedly.
        {
            var sb = new StringBuilder();
            sb.Append(_text[_pos]);
            Advance();
            while (_pos < _text.Length && IsOtherChar(_text[_pos]))
            {
                sb.Append(_text[_pos]);
                Advance();
            }
            return Token(PpTokenKind.Other, sb.ToString(), startLine, startCol);
        }
    }

    private static bool IsOtherChar(char c) =>
        !char.IsLetterOrDigit(c) &&
        c != '_' &&
        c != ' ' && c != '\t' && c != '\r' && c != '\n' &&
        c != '(' && c != ')' && c != '{' && c != '}' && c != '[' && c != ']' &&
        c != ',' && c != '=' && c != '"' && c != '/';

    private char Peek(int offset) =>
        _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private void Advance()
    {
        if (_text[_pos] == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
        _pos++;
    }

    private PpToken Token(PpTokenKind kind, string text, int line, int col) =>
        new(kind, text, new SourcePosition(_file, line, col));
}
