namespace Ccgnf.Interpreter;

/// <summary>
/// Context published by the interpreter each time it needs a host-supplied
/// value. Consumed by <see cref="IHostInputQueue.Next(InputRequest)"/>; a
/// blocking host (room / UI) reads the request, surfaces it to the player,
/// and eventually supplies the chosen <see cref="RtValue"/>.
/// </summary>
public sealed record InputRequest(
    string Prompt,
    int? PlayerId,
    IReadOnlyList<LegalAction> LegalActions);

/// <summary>
/// One option the player can submit at a pending input point. In v1 the only
/// producer is <c>Choice(chooser, options: {k1: ..., k2: ...})</c>; each
/// option becomes <c>LegalAction { Kind = "choice_option", Label = "k1" }</c>.
/// Future kinds — "target_entity", "play_card", "pass_priority" — plug in
/// alongside without widening the consumer interface.
/// </summary>
public sealed record LegalAction(
    string Kind,
    string Label,
    IReadOnlyDictionary<string, string>? Metadata = null);
