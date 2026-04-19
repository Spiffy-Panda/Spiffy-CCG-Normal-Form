using System.Text;
using System.Text.RegularExpressions;
using Ccgnf.Ast;
using Ccgnf.Rest.Rendering;

namespace Ccgnf.Rest.Serialization;

/// <summary>
/// Projects an <see cref="AstCardDecl"/> into a UI-friendly <see cref="CardDto"/>.
/// Field extraction walks the card's top-level block; card text is pulled from
/// the raw source starting at the supplied <c>sourceLine</c> (trailing
/// <c>// text:</c> line comments are stripped by the preprocessor before
/// parsing, so they can only be recovered from raw content).
/// </summary>
public static class CardMapper
{
    private static readonly Regex TextRegex = new(
        @"^\s*//\s*text:\s*(?<body>.*?)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static CardDto ToDto(
        AstCardDecl card,
        string? rawContent,
        string sourcePath,
        int sourceLine)
    {
        string type = "";
        int? cost = null;
        string rarity = "";
        var factions = new List<string>();
        var keywords = new List<string>();
        IReadOnlyList<string> abilitiesText = Array.Empty<string>();

        foreach (var field in card.Body.Fields)
        {
            switch (field.Key.Name)
            {
                case "factions":
                    ExtractIdents(Unwrap(field.Value), factions);
                    break;
                case "type":
                    if (Unwrap(field.Value) is AstIdent typeId) type = typeId.Name;
                    break;
                case "cost":
                    if (Unwrap(field.Value) is AstIntLit costLit) cost = costLit.Value;
                    break;
                case "rarity":
                    if (Unwrap(field.Value) is AstIdent rarityId) rarity = rarityId.Name;
                    break;
                case "keywords":
                    if (Unwrap(field.Value) is AstListLit kwList)
                    {
                        foreach (var element in kwList.Elements)
                        {
                            var rendered = RenderKeyword(element);
                            if (!string.IsNullOrEmpty(rendered)) keywords.Add(rendered);
                        }
                    }
                    break;
                case "abilities":
                    abilitiesText = AstHumanizer.HumanizeAbilitiesField(field.Value);
                    break;
            }
        }

        return new CardDto(
            Name: card.Name,
            Factions: factions,
            Type: type,
            Cost: cost,
            Rarity: rarity,
            Keywords: keywords,
            Text: ExtractText(rawContent, sourceLine),
            AbilitiesText: abilitiesText,
            SourcePath: sourcePath,
            SourceLine: sourceLine);
    }

    private static AstExpr? Unwrap(AstFieldValue value) =>
        value is AstFieldExpr e ? e.Value : null;

    private static void ExtractIdents(AstExpr? expr, List<string> into)
    {
        switch (expr)
        {
            case AstBraceExpr brace:
                foreach (var entry in brace.Entries)
                {
                    if (entry is AstBraceValue bv && bv.Value is AstIdent id)
                        into.Add(id.Name);
                }
                break;
            case AstListLit list:
                foreach (var element in list.Elements)
                    if (element is AstIdent id) into.Add(id.Name);
                break;
            case AstIdent single:
                into.Add(single.Name);
                break;
        }
    }

    private static string RenderKeyword(AstExpr expr)
    {
        switch (expr)
        {
            case AstIdent id:
                return id.Name;
            case AstFunctionCall call when call.Callee is AstIdent head:
                {
                    var sb = new StringBuilder();
                    sb.Append(head.Name);
                    sb.Append('(');
                    for (int i = 0; i < call.Args.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(RenderArg(call.Args[i]));
                    }
                    sb.Append(')');
                    return sb.ToString();
                }
            default:
                return "";
        }
    }

    private static string RenderArg(AstArg arg) => arg switch
    {
        AstArgPositional p => RenderExprShort(p.Value),
        AstArgNamed n => $"{n.Name}: {RenderExprShort(n.Value)}",
        AstArgBinding b => $"{b.Name}={RenderExprShort(b.Value)}",
        _ => "",
    };

    private static string RenderExprShort(AstExpr expr) => expr switch
    {
        AstIntLit i => i.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        AstStringLit s => $"\"{s.Value}\"",
        AstIdent id => id.Name,
        _ => "…",
    };

    private static string ExtractText(string? rawContent, int startLine)
    {
        if (string.IsNullOrEmpty(rawContent) || startLine <= 0) return "";

        int cursor = 0;
        int lineNumber = 1;
        while (lineNumber < startLine && cursor < rawContent.Length)
        {
            int nl = rawContent.IndexOf('\n', cursor);
            if (nl < 0) return "";
            cursor = nl + 1;
            lineNumber++;
        }

        int braceDepth = 0;
        bool sawOpeningBrace = false;
        while (cursor < rawContent.Length)
        {
            int nl = rawContent.IndexOf('\n', cursor);
            int lineEnd = nl < 0 ? rawContent.Length : nl;
            var line = rawContent.AsSpan(cursor, lineEnd - cursor);

            var match = TextRegex.Match(rawContent, cursor, lineEnd - cursor);
            if (match.Success) return match.Groups["body"].Value;

            foreach (var c in line)
            {
                if (c == '{') { braceDepth++; sawOpeningBrace = true; }
                else if (c == '}') braceDepth--;
            }
            if (sawOpeningBrace && braceDepth <= 0) return "";

            if (nl < 0) break;
            cursor = nl + 1;
        }
        return "";
    }
}
