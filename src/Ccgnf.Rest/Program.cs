using Ccgnf.Rest.Endpoints;
using Ccgnf.Rest.Sessions;

// -----------------------------------------------------------------------------
// Ccgnf.Rest — HTTP host for the CCGNF engine.
//
// Each pipeline stage (preprocess, parse, AST build, validate, interpreter
// run) is exposed as an independent POST endpoint; sessions add a thin
// lifecycle layer on top of interpreter runs. A static playground lives at
// `/` for browser-driven use.
//
// Port convention (see README): HTTP defaults to 19397. Override with the
// CCGNF_HTTP_PORT environment variable. Future transports (SSE, WebSocket)
// increment from there.
// -----------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

const int DefaultHttpPort = 19397;
int httpPort = int.TryParse(
    Environment.GetEnvironmentVariable("CCGNF_HTTP_PORT"),
    out var p) ? p : DefaultHttpPort;
builder.WebHost.UseUrls($"http://localhost:{httpPort}");

builder.Services.AddSingleton<SessionStore>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
{
    o.SerializerOptions.WriteIndented = false;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => new { ok = true, service = "ccgnf.rest", port = httpPort });

PipelineEndpoints.Map(app);
SessionEndpoints.Map(app);

app.Run();

/// <summary>Exposed so integration tests can target it via WebApplicationFactory.</summary>
public partial class Program { }
