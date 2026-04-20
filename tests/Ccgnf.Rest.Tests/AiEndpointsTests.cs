using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ccgnf.Rest.Tests;

/// <summary>
/// Integration tests for <c>/api/ai/*</c>. The editor surface is gated
/// behind <c>CCGNF_AI_EDITOR=1</c>; these tests set the variable for the
/// duration of each call so they cover both paths.
/// </summary>
public class AiEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AiEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetBots_ReturnsFixedAndUtility()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/ai/bots");
        response.EnsureSuccessStatusCode();

        var bots = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, bots.GetArrayLength());
        var ids = new[] { bots[0].GetProperty("id").GetString(), bots[1].GetProperty("id").GetString() };
        Assert.Contains("fixed", ids);
        Assert.Contains("utility", ids);
    }

    [Fact]
    public async Task GetWeights_ReturnsDefaultWhenFileAbsent()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/ai/weights");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        var keys = dto.GetProperty("considerationKeys").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("on_curve", keys);
        Assert.Contains("tempo_per_aether", keys);
        Assert.Contains("lowest_live_hp", keys);
    }

    [Fact]
    public async Task PutWeights_Rejects404WhenEditorOff()
    {
        // Explicitly unset the editor flag for this call.
        Environment.SetEnvironmentVariable("CCGNF_AI_EDITOR", null);
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync("/api/ai/weights",
            new { json = "{\"version\":1,\"intents\":{}}" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PreviewScore_DisabledWhenEditorOff()
    {
        Environment.SetEnvironmentVariable("CCGNF_AI_EDITOR", null);
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/ai/preview-score", new
        {
            cpuAether = 2,
            legalActions = Array.Empty<object>(),
            weightsJson = "",
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PreviewScore_ReturnsRankedRowsWhenEditorOn()
    {
        Environment.SetEnvironmentVariable("CCGNF_AI_EDITOR", "1");
        try
        {
            using var client = _factory.CreateClient();
            var req = new
            {
                cpuAether = 2,
                legalActions = new[]
                {
                    new { kind = "play_card", label = "play:good", metadata = new Dictionary<string, string> { ["cost"] = "2", ["force"] = "4" } },
                    new { kind = "play_card", label = "play:bad", metadata = new Dictionary<string, string> { ["cost"] = "5", ["force"] = "1" } },
                },
                weightsJson = "",
            };
            var response = await client.PostAsJsonAsync("/api/ai/preview-score", req);
            response.EnsureSuccessStatusCode();

            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            var rows = doc.GetProperty("rows").EnumerateArray().ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("play:good", rows[0].GetProperty("label").GetString());
            Assert.True(rows[0].GetProperty("score").GetSingle() >= rows[1].GetProperty("score").GetSingle());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CCGNF_AI_EDITOR", null);
        }
    }

    [Fact]
    public async Task PresetsIncludeArchetypes()
    {
        using var client = _factory.CreateClient();
        var doc = await client.GetFromJsonAsync<JsonElement>("/api/decks/presets");
        bool sawArchetypes = false;
        foreach (var deck in doc.EnumerateArray())
        {
            if (!deck.TryGetProperty("archetypes", out var ar)) continue;
            if (ar.GetArrayLength() > 0) { sawArchetypes = true; break; }
        }
        Assert.True(sawArchetypes, "No preset deck carried archetype tags; check ember-aggro.deck.json.");
    }
}
