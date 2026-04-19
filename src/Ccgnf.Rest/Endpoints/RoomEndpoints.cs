using System.Net.Mime;
using System.Text;
using Ccgnf.Interpreter;
using Ccgnf.Rest.Rooms;
using Ccgnf.Rest.Serialization;
using Ccgnf.Rest.Services;
using Microsoft.Extensions.Logging;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// Room lifecycle + SSE endpoints. See <c>docs/plan/web/rooms-protocol.md</c>
/// for the full protocol. v1 runs the interpreter synchronously on room
/// start; the action endpoint accepts inputs into a buffered queue and
/// acknowledges receipt via SSE, but does not drive further interpreter
/// steps (that lands with the async refactor in step 6c).
/// </summary>
internal static class RoomEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/rooms", Create);
        app.MapGet("/api/rooms", List);
        app.MapGet("/api/rooms/{id}", GetOne);
        app.MapPost("/api/rooms/{id}/join", Join);
        app.MapPost("/api/rooms/{id}/actions", Action);
        app.MapGet("/api/rooms/{id}/state", GetState);
        app.MapGet("/api/rooms/{id}/events", Events);
        app.MapGet("/api/rooms/{id}/export", Export);
        app.MapDelete("/api/rooms/{id}", Delete);
    }

    private static IResult Export(string id, RoomStore store)
    {
        if (!store.TryGet(id, out var room)) return Results.NotFound();
        return Results.Ok(new RoomExportDto(
            RoomId: room.Id,
            Seed: room.Seed,
            DeckSize: room.DeckSize,
            CreatedAt: room.CreatedAt.ToString("o"),
            ExportedAt: DateTimeOffset.UtcNow.ToString("o"),
            Lifecycle: room.Lifecycle.ToString(),
            StepCount: (int)(room.State?.StepCount ?? 0),
            GameOver: room.State?.GameOver ?? false,
            Players: room.Players
                .Select(p => new RoomExportPlayerDto(
                    p.PlayerId, p.Name, p.DeckName, p.DeckCardNames))
                .ToList(),
            State: room.State is null ? null : StateMapper.ToDto(room.State)));
    }

    // -------------------------------------------------------------------------

    private static IResult Create(
        RoomCreateRequest req,
        RoomStore store,
        ILoggerFactory lf)
    {
        var loader = new ProjectLoader(lf);
        var load = loader.LoadFromSources(PipelineEndpoints.ToSourceFiles(req.Files));
        if (load.HasErrors || load.File is null)
        {
            return Results.BadRequest(new
            {
                error = "Validation failed.",
                diagnostics = DiagnosticMapper.ToDtos(load.Diagnostics),
            });
        }

        int slots = req.PlayerSlots > 0 ? req.PlayerSlots : 2;
        int deckSize = req.DeckSize > 0 ? req.DeckSize : 30;
        var room = store.Create(load.File, req.Seed, slots, deckSize);
        return Results.Created($"/api/rooms/{room.Id}", ToSummary(room));
    }

    private static IResult List(RoomStore store) =>
        Results.Ok(store.All.OrderByDescending(r => r.CreatedAt).Select(ToSummary));

    private static IResult GetOne(string id, RoomStore store)
    {
        if (!store.TryGet(id, out var room)) return Results.NotFound();
        return Results.Ok(ToDetail(room));
    }

    private static IResult Join(string id, RoomJoinRequest req, RoomStore store, DeckCatalog decks)
    {
        if (!store.TryGet(id, out var room)) return Results.NotFound();

        string? deckName = null;
        IReadOnlyList<string>? deckCardNames = null;
        if (req.Deck is { } spec)
        {
            if (!string.IsNullOrWhiteSpace(spec.Preset))
            {
                var preset = decks.Get().FirstOrDefault(p => p.Id == spec.Preset);
                if (preset is null)
                {
                    return Results.BadRequest(new { error = $"Unknown preset deck '{spec.Preset}'." });
                }
                deckName = preset.Name;
                deckCardNames = ExpandDeckCards(preset.Cards);
            }
            else if (spec.Cards is { Count: > 0 } cards)
            {
                int total = 0;
                foreach (var c in cards) total += c.Count;
                deckName = $"Custom deck ({total} cards)";
                deckCardNames = ExpandDeckCards(cards);
            }
        }

        var player = room.TryJoin(req.Name, deckName, deckCardNames);
        if (player is null) return Results.Conflict(new { error = "Room full or finished." });

        return Results.Ok(new RoomJoinResponse(
            PlayerId: player.PlayerId,
            Token: player.Token,
            State: room.State is null ? null : StateMapper.ToDto(room.State)));
    }

    private static IReadOnlyList<string> ExpandDeckCards(IReadOnlyList<DeckCardEntry> entries)
    {
        var list = new List<string>();
        foreach (var e in entries)
        {
            for (int i = 0; i < e.Count; i++) list.Add(e.Name);
        }
        return list;
    }

    private static IResult Action(string id, RoomActionRequest req, RoomStore store)
    {
        if (!store.TryGet(id, out var room)) return Results.NotFound();
        if (!room.ValidateToken(req.PlayerId, req.Token))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(req.Action))
        {
            return Results.BadRequest(new { error = "action is required." });
        }
        room.AppendAction(req.PlayerId, req.Action, args: null);
        return Results.Accepted(value: new RoomActionResponse(Accepted: true));
    }

    private static IResult GetState(string id, RoomStore store)
    {
        if (!store.TryGet(id, out var room)) return Results.NotFound();
        if (room.State is null) return Results.NoContent();
        return Results.Ok(StateMapper.ToDto(room.State));
    }

    private static async Task Events(string id, HttpContext ctx, RoomStore store)
    {
        if (!store.TryGet(id, out var room))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

        await foreach (var frame in room.Broadcaster.Subscribe(ctx.RequestAborted))
        {
            var bytes = Encoding.UTF8.GetBytes(frame.ToSseFrame());
            try
            {
                await ctx.Response.Body.WriteAsync(bytes, ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private static async Task<IResult> Delete(string id, RoomStore store) =>
        await store.RemoveAsync(id) ? Results.NoContent() : Results.NotFound();

    // -------------------------------------------------------------------------

    private static RoomSummaryDto ToSummary(Room room) => new(
        RoomId: room.Id,
        State: room.Lifecycle.ToString(),
        Seed: room.Seed,
        PlayerSlots: room.PlayerSlots,
        Occupied: room.Players.Count,
        CreatedAt: room.CreatedAt.ToString("o"));

    private static RoomDetailDto ToDetail(Room room) => new(
        RoomId: room.Id,
        State: room.Lifecycle.ToString(),
        Seed: room.Seed,
        PlayerSlots: room.PlayerSlots,
        Occupied: room.Players.Count,
        CreatedAt: room.CreatedAt.ToString("o"),
        LastActivityAt: room.LastActivityAt.ToString("o"),
        Players: room.Players
            .Select(p => new RoomPlayerDto(p.PlayerId, p.Name, Connected: true, DeckName: p.DeckName))
            .ToList());
}
