using Ccgnf.Interpreter;
using Ccgnf.Rest.Serialization;
using Ccgnf.Rest.Sessions;
using Microsoft.Extensions.Logging;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// Session lifecycle endpoints. A session is one successful run of the v1
/// interpreter against a supplied project; the resulting
/// <see cref="GameState"/> stays resident under its id until DELETE'd.
/// v1 is read-only past creation (the interpreter halts after Round-1 Rise);
/// the actions endpoint from GrammarSpec §11.3 lands when the event loop
/// supports mid-run input.
/// </summary>
internal static class SessionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/sessions", Create);
        app.MapGet("/api/sessions", List);
        app.MapGet("/api/sessions/{id}", GetOne);
        app.MapGet("/api/sessions/{id}/state", GetState);
        app.MapDelete("/api/sessions/{id}", Delete);
    }

    // -------------------------------------------------------------------------

    private static IResult Create(
        SessionCreateRequest req,
        SessionStore store,
        ILoggerFactory lf)
    {
        var loader = new ProjectLoader(lf);
        var load = loader.LoadFromSources(PipelineEndpoints.ToSourceFiles(req.Files));
        if (load.HasErrors || load.File is null)
        {
            return Results.BadRequest(new SessionCreateResponse(
                SessionId: string.Empty,
                State: null,
                Diagnostics: DiagnosticMapper.ToDtos(load.Diagnostics)));
        }

        var interpreter = new InterpreterRt(lf.CreateLogger<InterpreterRt>(), lf);
        var state = interpreter.Run(load.File, new InterpreterOptions
        {
            Seed = req.Seed,
            Inputs = PipelineEndpoints.BuildInputs(req.Inputs),
            DefaultDeckSize = req.DeckSize > 0 ? req.DeckSize : 30,
        });

        var session = store.Create(state, req.Seed);
        return Results.Created($"/api/sessions/{session.Id}", new SessionCreateResponse(
            SessionId: session.Id,
            State: StateMapper.ToDto(state),
            Diagnostics: DiagnosticMapper.ToDtos(load.Diagnostics)));
    }

    private static IResult List(SessionStore store) =>
        Results.Ok(store.All
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                sessionId = s.Id,
                seed = s.Seed,
                createdAt = s.CreatedAt,
                stepCount = s.State.StepCount,
                gameOver = s.State.GameOver,
            }));

    private static IResult GetOne(string id, SessionStore store)
    {
        if (!store.TryGet(id, out var s)) return Results.NotFound();
        return Results.Ok(new
        {
            sessionId = s.Id,
            seed = s.Seed,
            createdAt = s.CreatedAt,
        });
    }

    private static IResult GetState(string id, SessionStore store)
    {
        if (!store.TryGet(id, out var s)) return Results.NotFound();
        return Results.Ok(StateMapper.ToDto(s.State));
    }

    private static IResult Delete(string id, SessionStore store) =>
        store.Remove(id) ? Results.NoContent() : Results.NotFound();
}
