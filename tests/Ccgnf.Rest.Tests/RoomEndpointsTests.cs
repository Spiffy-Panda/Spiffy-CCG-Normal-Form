using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ccgnf.Rest.Tests;

public class RoomEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RoomEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static async Task<IReadOnlyList<object>> LoadEncoding()
    {
        var dir = Path.Combine(FindRepoRoot(), "encoding");
        var files = new List<object>();
        foreach (var f in Directory.GetFiles(dir, "*.ccgnf", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal))
        {
            files.Add(new { path = f, content = await File.ReadAllTextAsync(f) });
        }
        return files;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    private async Task<string> CreateRoom(HttpClient client)
    {
        var files = await LoadEncoding();
        var resp = await client.PostAsJsonAsync("/api/rooms", new { files, seed = 42 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("roomId").GetString()!;
    }

    [Fact]
    public async Task Create_ReturnsRoomId_WaitingForPlayers()
    {
        var client = _factory.CreateClient();
        var files = await LoadEncoding();
        var resp = await client.PostAsJsonAsync("/api/rooms", new { files, seed = 42 });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("roomId").GetString()));
        Assert.Equal("WaitingForPlayers", body.GetProperty("state").GetString());
        Assert.Equal(2, body.GetProperty("playerSlots").GetInt32());
        Assert.Equal(0, body.GetProperty("occupied").GetInt32());
    }

    [Fact]
    public async Task Create_WithValidationError_Returns400()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/rooms", new
        {
            files = new[] { new { path = "bad.ccgnf", content = "Entity Foo { kind: A }\nEntity Foo { kind: B }" } },
            seed = 7,
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Join_WithPresetDeck_StoresDeckNameOnPlayer()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var join = await client.PostAsJsonAsync(
            $"/api/rooms/{roomId}/join",
            new { name = "alice", deck = new { preset = "ember-aggro" } });
        join.EnsureSuccessStatusCode();

        var detail = await (await client.GetAsync($"/api/rooms/{roomId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var p1 = detail.GetProperty("players").EnumerateArray().First();
        Assert.Equal("alice", p1.GetProperty("name").GetString());
        Assert.Equal("EMBER Aggro", p1.GetProperty("deckName").GetString());
    }

    [Fact]
    public async Task Join_WithUnknownPreset_Returns400()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var join = await client.PostAsJsonAsync(
            $"/api/rooms/{roomId}/join",
            new { name = "alice", deck = new { preset = "does-not-exist" } });
        Assert.Equal(HttpStatusCode.BadRequest, join.StatusCode);
    }

    [Fact]
    public async Task Join_WithExplicitCards_StoresCustomDeckLabel()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var join = await client.PostAsJsonAsync(
            $"/api/rooms/{roomId}/join",
            new
            {
                name = "bob",
                deck = new
                {
                    cards = new[]
                    {
                        new { name = "Cinderling", count = 3 },
                        new { name = "Spark", count = 3 },
                    },
                },
            });
        join.EnsureSuccessStatusCode();

        var detail = await (await client.GetAsync($"/api/rooms/{roomId}"))
            .Content.ReadFromJsonAsync<JsonElement>();
        var p = detail.GetProperty("players").EnumerateArray().First();
        Assert.Contains("Custom deck", p.GetProperty("deckName").GetString());
    }

    [Fact]
    public async Task Export_ReturnsSeedDecksAndState()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var j1 = await client.PostAsJsonAsync($"/api/rooms/{roomId}/join",
            new { name = "alice", deck = new { preset = "ember-aggro" } });
        j1.EnsureSuccessStatusCode();
        var j2 = await client.PostAsJsonAsync($"/api/rooms/{roomId}/join",
            new { name = "bob", deck = new { preset = "hollow-disruption" } });
        j2.EnsureSuccessStatusCode();

        var resp = await client.GetAsync($"/api/rooms/{roomId}/export");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(roomId, body.GetProperty("roomId").GetString());
        Assert.Equal(42, body.GetProperty("seed").GetInt32());
        Assert.Equal(30, body.GetProperty("deckSize").GetInt32());
        Assert.True(body.GetProperty("stepCount").GetInt32() > 0);
        var players = body.GetProperty("players").EnumerateArray().ToArray();
        Assert.Equal(2, players.Length);
        Assert.Equal("EMBER Aggro", players[0].GetProperty("deckName").GetString());
        Assert.Equal(30, players[0].GetProperty("deckCardNames").GetArrayLength());
        Assert.True(body.GetProperty("state").ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Join_TwoPlayers_ThirdReturns409()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var j1 = await client.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { name = "alice" });
        j1.EnsureSuccessStatusCode();
        var j1body = await j1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, j1body.GetProperty("playerId").GetInt32());
        Assert.False(string.IsNullOrEmpty(j1body.GetProperty("token").GetString()));

        var j2 = await client.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { name = "bob" });
        j2.EnsureSuccessStatusCode();
        var j2body = await j2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, j2body.GetProperty("playerId").GetInt32());

        var j3 = await client.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { name = "carol" });
        Assert.Equal(HttpStatusCode.Conflict, j3.StatusCode);

        // Room should now be Active; state endpoint returns a GameStateDto.
        var stateResp = await client.GetAsync($"/api/rooms/{roomId}/state");
        stateResp.EnsureSuccessStatusCode();
        var state = await stateResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, state.GetProperty("playerIds").GetArrayLength());
    }

    [Fact]
    public async Task Action_WrongToken_Returns401()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var j1 = await client.PostAsJsonAsync($"/api/rooms/{roomId}/join", new { name = "alice" });
        j1.EnsureSuccessStatusCode();
        var token = (await j1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var bad = await client.PostAsJsonAsync($"/api/rooms/{roomId}/actions", new
        {
            playerId = 1,
            token = "tok_bogus",
            action = "pass",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

        var good = await client.PostAsJsonAsync($"/api/rooms/{roomId}/actions", new
        {
            playerId = 1,
            token,
            action = "pass",
        });
        Assert.Equal(HttpStatusCode.Accepted, good.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_ThenNotFound()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var del = await client.DeleteAsync($"/api/rooms/{roomId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var again = await client.GetAsync($"/api/rooms/{roomId}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task List_IncludesCreatedRoom()
    {
        var client = _factory.CreateClient();
        var roomId = await CreateRoom(client);

        var list = await client.GetAsync("/api/rooms");
        list.EnsureSuccessStatusCode();
        var rooms = await list.Content.ReadFromJsonAsync<JsonElement>();
        bool found = false;
        foreach (var r in rooms.EnumerateArray())
            if (r.GetProperty("roomId").GetString() == roomId) { found = true; break; }
        Assert.True(found, $"Expected to find {roomId} in the room list.");
    }
}
