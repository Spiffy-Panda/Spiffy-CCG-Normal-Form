using Ccgnf.Interpreter;

namespace Ccgnf.Bots;

/// <summary>
/// Contract for a CPU-seat decision maker. One <c>Choose</c> call picks
/// one <see cref="RtValue"/> to submit in response to a single
/// <see cref="InputRequest"/>.
/// <para>
/// Implementations are stateless with respect to each other — they read
/// the snapshotted <see cref="GameState"/> and the pending
/// <see cref="InputRequest.LegalActions"/>. Any across-decision memory
/// (see 10.2g sticky intent) lives on the hosting Room, not in the bot.
/// </para>
/// <para>
/// Takes <see cref="GameState"/> rather than <see cref="InterpreterRun"/>
/// because the bot only needs the snapshot, not the generator handle;
/// this also keeps tests light — they don't need a running interpreter
/// to exercise decision logic.
/// </para>
/// </summary>
public interface IRoomBot
{
    /// <summary>
    /// Pick a value to submit for the given <paramref name="pending"/>
    /// input. <paramref name="cpuEntityId"/> is the GameState Entity.Id of
    /// the Player the bot is seated as.
    /// </summary>
    RtValue Choose(GameState state, InputRequest pending, int cpuEntityId);
}
