using System.Text;

namespace Ccgnf.Interpreter;

/// <summary>
/// Produces a deterministic textual dump of a <see cref="GameState"/>. The
/// goal is not a perfect round-trip format — it's a canonical form that two
/// runs with identical inputs should produce identically, so the determinism
/// test can string-compare instead of recurse through object graphs.
/// </summary>
public static class StateSerializer
{
    public static string Serialize(GameState state)
    {
        var sb = new StringBuilder();
        sb.Append("GameState steps=").Append(state.StepCount)
          .Append(" over=").Append(state.GameOver)
          .Append('\n');

        foreach (var entity in state.Entities.Values.OrderBy(e => e.Id))
        {
            sb.Append("entity #").Append(entity.Id)
              .Append(' ').Append(entity.Kind)
              .Append(' ').Append(entity.DisplayName)
              .Append('\n');

            foreach (var (k, v) in entity.Characteristics.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append("  char ").Append(k).Append('=').Append(Format(v)).Append('\n');
            }
            foreach (var (k, v) in entity.Counters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append("  ctr  ").Append(k).Append('=').Append(v).Append('\n');
            }
            foreach (var (k, v) in entity.Parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append("  param ").Append(k).Append('=').Append(Format(v)).Append('\n');
            }
            foreach (var (k, z) in entity.Zones.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append("  zone ").Append(k)
                  .Append(' ').Append(z.Order)
                  .Append(" [").Append(string.Join(",", z.Contents)).Append("]\n");
            }
            if (entity.Abilities.Count > 0)
            {
                sb.Append("  abilities=").Append(entity.Abilities.Count).Append('\n');
            }
        }

        sb.Append("pending=");
        foreach (var e in state.PendingEvents.Snapshot())
        {
            sb.Append(e).Append(';');
        }
        sb.Append('\n');
        return sb.ToString();
    }

    private static string Format(RtValue v) => v switch
    {
        RtList l => "[" + string.Join(",", l.Elements.Select(Format)) + "]",
        RtSet s => "{" + string.Join(",", s.Elements.Select(Format)) + "}",
        RtTuple t => "(" + string.Join(",", t.Elements.Select(Format)) + ")",
        _ => v.ToString() ?? "?",
    };
}
