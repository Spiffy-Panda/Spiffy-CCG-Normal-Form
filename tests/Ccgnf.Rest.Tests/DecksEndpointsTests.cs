using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ccgnf.Rest.Tests;

public class DecksEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DecksEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MockPool_DeterministicForSameSeed()
    {
        var client = _factory.CreateClient();
        var req = new { format = "draft", seed = 1234, size = 40 };
        var first = await (await client.PostAsJsonAsync("/api/decks/mock-pool", req))
            .Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsJsonAsync("/api/decks/mock-pool", req))
            .Content.ReadFromJsonAsync<JsonElement>();

        var a = first.GetProperty("cards").EnumerateArray().Select(x => x.GetString()).ToArray();
        var b = second.GetProperty("cards").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task MockPool_RespectsSize()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/decks/mock-pool",
            new { format = "draft", seed = 7, size = 20 });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, body.GetProperty("cards").GetArrayLength());
    }

    [Fact]
    public async Task MockPool_SizeTooLarge_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/decks/mock-pool",
            new { format = "draft", seed = 7, size = 10_000 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task MockPool_UnknownFormat_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/decks/mock-pool",
            new { format = "nonsense", seed = 7, size = 10 });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task MockPool_Constructed_ReturnsFullPool()
    {
        var client = _factory.CreateClient();
        var cardsResp = await client.GetAsync("/api/cards");
        cardsResp.EnsureSuccessStatusCode();
        int pool = (await cardsResp.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength();

        var resp = await client.PostAsJsonAsync("/api/decks/mock-pool",
            new { format = "constructed", seed = 0, size = 0 });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(pool, body.GetProperty("cards").GetArrayLength());
    }
}
