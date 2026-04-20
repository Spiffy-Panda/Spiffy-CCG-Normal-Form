using Ccgnf.Ast;
using System.Globalization;
using System.Text;

namespace Ccgnf.Interpreter;

/// <summary>
/// Opt-in diagnostic log that records every card-cast and every
/// non-Game Triggered-ability fire to a JSONL file. Turned on by setting
/// the <c>CCGNF_CAST_LOG</c> environment variable to the output path
/// (<c>append</c>-mode). When the variable is unset, every hook is a
/// cheap null-check and no I/O happens.
/// <para>
/// Each line is a single JSON object; the schema is stable enough for
/// <c>tools/cast-log-summary.py</c> to consume. Two record kinds:
/// </para>
/// <list type="bullet">
///   <item><c>{ "kind": "cast", "card": ..., "type": ..., "cost": ...,
///     "player_id": ..., "arena": ..., "keywords": [...],
///     "abilities": [...] }</c></item>
///   <item><c>{ "kind": "trigger", "owner_card": ..., "owner_id": ...,
///     "pattern": ..., "event": ..., "event_target": ... }</c></item>
/// </list>
/// Consumers can group by <c>card</c> (alphabetical) to verify that
/// each played card's triggered abilities actually fired in the run.
/// </summary>
public static class CastLog
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static bool _resolved;

    public static bool IsEnabled
    {
        get
        {
            EnsureResolved();
            return _writer is not null;
        }
    }

    public static void RecordCast(
        Entity card,
        Entity player,
        Ast.AstCardDecl decl,
        int cost,
        string cardType,
        RtSymbol? arenaPos)
    {
        var w = Writer();
        if (w is null) return;

        var sb = new StringBuilder(256);
        sb.Append("{\"kind\":\"cast\"");
        AppendJsonString(sb, ",\"card\":", card.DisplayName);
        AppendJsonString(sb, ",\"type\":", cardType);
        sb.Append(",\"cost\":").Append(cost.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"player_id\":").Append(player.Id.ToString(CultureInfo.InvariantCulture));
        if (arenaPos is not null) AppendJsonString(sb, ",\"arena\":", arenaPos.Name);

        // Keywords — read off the declaration so the log sees the
        // designer-facing form ("Fortify(2)" not a counter key).
        var keywords = KeywordRuntime.ReadKeywords(decl);
        sb.Append(",\"keywords\":[");
        for (int i = 0; i < keywords.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var (name, param) = keywords[i];
            string label = param is int p ? $"{name}({p})" : name;
            AppendJsonString(sb, label);
        }
        sb.Append(']');

        // Abilities — kind + on-pattern description. Gives the
        // summariser enough to know "this card declared an OnEnter"
        // without re-reading the encoding.
        sb.Append(",\"abilities\":[");
        bool first = true;
        foreach (var f in decl.Body.Fields)
        {
            if (f.Key.Name != "abilities") continue;
            if (f.Value is not AstFieldExpr fe) continue;
            if (fe.Value is not AstListLit list) continue;
            foreach (var el in list.Elements)
            {
                if (el is not AstFunctionCall fc) continue;
                if (fc.Callee is not AstIdent id) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                AppendJsonString(sb, "\"kind\":", id.Name);
                string? onText = null;
                foreach (var a in fc.Args)
                {
                    if (a is AstArgNamed { Name: "on" } n)
                    {
                        onText = DescribePattern(n.Value);
                        break;
                    }
                }
                if (onText is not null) AppendJsonString(sb, ",\"on\":", onText);
                sb.Append('}');
            }
            break;
        }
        sb.Append(']');
        sb.Append('}');

        lock (_lock) { w.WriteLine(sb.ToString()); }
    }

    public static void RecordTrigger(
        Entity owner,
        AstExpr pattern,
        GameEvent fired)
    {
        var w = Writer();
        if (w is null) return;

        var sb = new StringBuilder(128);
        sb.Append("{\"kind\":\"trigger\"");
        AppendJsonString(sb, ",\"owner_card\":", owner.DisplayName);
        sb.Append(",\"owner_id\":").Append(owner.Id.ToString(CultureInfo.InvariantCulture));
        AppendJsonString(sb, ",\"pattern\":", DescribePattern(pattern));
        AppendJsonString(sb, ",\"event\":", fired.TypeName);
        if (fired.Fields.TryGetValue("target", out var tv) && tv is RtEntityRef tr)
        {
            sb.Append(",\"event_target\":").Append(tr.Id.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append('}');

        lock (_lock) { w.WriteLine(sb.ToString()); }
    }

    private static string DescribePattern(AstExpr expr)
    {
        if (expr is AstMemberAccess ma && ma.Target is AstIdent ti)
        {
            return $"{ti.Name}.{ma.Member}";
        }
        if (expr is AstFunctionCall fc && fc.Callee is AstMemberAccess fma
            && fma.Target is AstIdent fti)
        {
            return $"{fti.Name}.{fma.Member}(...)";
        }
        return expr.GetType().Name;
    }

    private static StreamWriter? Writer()
    {
        EnsureResolved();
        return _writer;
    }

    private static void EnsureResolved()
    {
        if (_resolved) return;
        lock (_lock)
        {
            if (_resolved) return;
            var path = Environment.GetEnvironmentVariable("CCGNF_CAST_LOG");
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var fs = new FileStream(
                        path,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read);
                    _writer = new StreamWriter(fs) { AutoFlush = true };
                }
                catch
                {
                    _writer = null;
                }
            }
            _resolved = true;
        }
    }

    private static void AppendJsonString(StringBuilder sb, string prefix, string value)
    {
        sb.Append(prefix);
        AppendJsonString(sb, value);
    }

    private static void AppendJsonString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
