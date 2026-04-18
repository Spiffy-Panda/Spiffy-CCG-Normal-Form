using System.Collections.Concurrent;
using System.Security.Cryptography;
using Ccgnf.Ast;
using Microsoft.Extensions.Logging;

namespace Ccgnf.Rest.Rooms;

public sealed class RoomStore
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<RoomStore> _log;

    public RoomStore(ILogger<RoomStore> log, ILoggerFactory loggerFactory)
    {
        _log = log;
        _loggerFactory = loggerFactory;
    }

    public Room Create(AstFile astFile, int seed, int playerSlots, int deckSize)
    {
        string id;
        do
        {
            id = GenerateId();
        } while (_rooms.ContainsKey(id));

        var room = new Room(id, astFile, seed, playerSlots, deckSize, _loggerFactory);
        _rooms[id] = room;
        _log.LogInformation(
            "Room {Id} created (seed={Seed}, slots={Slots}, deckSize={DeckSize}).",
            id, seed, playerSlots, deckSize);
        return room;
    }

    public bool TryGet(string id, out Room room)
    {
        var ok = _rooms.TryGetValue(id, out var r);
        room = r!;
        return ok;
    }

    public IReadOnlyCollection<Room> All => _rooms.Values.ToArray();

    public async Task<bool> RemoveAsync(string id)
    {
        if (!_rooms.TryRemove(id, out var room)) return false;
        room.Finish();
        await room.Broadcaster.DisposeAsync();
        _log.LogInformation("Room {Id} removed.", id);
        return true;
    }

    public async Task<int> EvictExpiredAsync(TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        int evicted = 0;
        foreach (var kvp in _rooms)
        {
            if (kvp.Value.IsExpired(ttl, now))
            {
                if (_rooms.TryRemove(kvp.Key, out var room))
                {
                    room.Finish();
                    await room.Broadcaster.DisposeAsync();
                    evicted++;
                }
            }
        }
        if (evicted > 0) _log.LogInformation("Evicted {Count} expired room(s).", evicted);
        return evicted;
    }

    private static string GenerateId()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return "r_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
