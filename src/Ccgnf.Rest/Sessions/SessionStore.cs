using System.Collections.Concurrent;
using Ccgnf.Interpreter;

namespace Ccgnf.Rest.Sessions;

/// <summary>
/// In-process registry of live game sessions. v1 scope: one run per session,
/// state lives until DELETE. A future revision can add TTL, persistence, and
/// per-session DI scopes (see GrammarSpec §11.3).
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    public GameSession Create(GameState state, int seed)
    {
        string id = Guid.NewGuid().ToString("N");
        var session = new GameSession(id, state, seed);
        _sessions[id] = session;
        return session;
    }

    public bool TryGet(string id, out GameSession session)
    {
        return _sessions.TryGetValue(id, out session!);
    }

    public bool Remove(string id) => _sessions.TryRemove(id, out _);

    public IEnumerable<GameSession> All => _sessions.Values;
}

public sealed class GameSession
{
    public string Id { get; }
    public GameState State { get; }
    public int Seed { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

    public GameSession(string id, GameState state, int seed)
    {
        Id = id;
        State = state;
        Seed = seed;
    }
}
