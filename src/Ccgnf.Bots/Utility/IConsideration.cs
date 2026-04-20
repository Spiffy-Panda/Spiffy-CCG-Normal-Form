using Ccgnf.Interpreter;

namespace Ccgnf.Bots.Utility;

/// <summary>
/// One scoring lens. A consideration declares which
/// <see cref="LegalAction.Kind"/>s it knows how to score, and returns a
/// normalised <c>[0, 1]</c> float per action. The <see cref="UtilityBot"/>
/// multiplies that output by the consideration's weight under the
/// current <see cref="Intent"/>.
/// </summary>
public interface IConsideration
{
    /// <summary>Key used to look this consideration up in the weight table.</summary>
    string Key { get; }

    /// <summary>
    /// True when this consideration applies to <paramref name="actionKind"/>.
    /// Lets the bot sum only across relevant scorers per action.
    /// </summary>
    bool Handles(string actionKind);

    /// <summary>
    /// Return a normalised score in <c>[0, 1]</c> for <paramref name="action"/>.
    /// Values outside that range are clamped by the bot.
    /// </summary>
    float Score(ScoringContext ctx, LegalAction action);
}
