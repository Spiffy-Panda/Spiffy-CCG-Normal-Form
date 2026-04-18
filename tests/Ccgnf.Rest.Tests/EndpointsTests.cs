using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ccgnf.Rest.Tests;

public class EndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public EndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // -----------------------------------------------------------------------
    // Shared fixtures
    // -----------------------------------------------------------------------

    private static object SimpleEntityPayload() => new
    {
        files = new[]
        {
            new { path = "e.ccgnf", content = "Entity Foo { kind: Foo }" },
        },
    };

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

    // -----------------------------------------------------------------------
    // Health
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.Equal("ccgnf.rest", doc.GetProperty("service").GetString());
    }

    // -----------------------------------------------------------------------
    // Pipeline stages
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Preprocess_Simple_ReturnsExpandedText()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/preprocess", SimpleEntityPayload());
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.Contains("Entity", doc.GetProperty("expanded").GetString() ?? "");
    }

    [Fact]
    public async Task Parse_Simple_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/parse", SimpleEntityPayload());
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Ast_CountsDeclarations()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/ast", SimpleEntityPayload());
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.Equal(1, doc.GetProperty("declarationCount").GetInt32());
    }

    [Fact]
    public async Task Validate_Simple_ReturnsNoDiagnostics()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/validate", SimpleEntityPayload());
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        Assert.Equal(0, doc.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public async Task Validate_DuplicateEntities_ReturnsDiagnostic()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/validate", new
        {
            files = new[]
            {
                new { path = "e.ccgnf", content = "Entity Foo { kind: Foo }\nEntity Foo { kind: Bar }" },
            },
        });
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(doc.GetProperty("ok").GetBoolean());
        Assert.True(doc.GetProperty("diagnostics").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Run_Encoding_ProducesState()
    {
        var client = _factory.CreateClient();
        var files = await LoadEncoding();
        var response = await client.PostAsJsonAsync("/api/run", new
        {
            files,
            seed = 42,
            inputs = new[] { "pass", "pass", "pass", "pass" },
        });
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("ok").GetBoolean());
        var state = doc.GetProperty("state");
        Assert.Equal(2, state.GetProperty("playerIds").GetArrayLength());
        Assert.Equal(3, state.GetProperty("arenaIds").GetArrayLength());
        int conduits = 0;
        foreach (var e in state.GetProperty("entities").EnumerateArray())
        {
            if (e.GetProperty("kind").GetString() == "Conduit") conduits++;
        }
        Assert.Equal(6, conduits);
    }

    // -----------------------------------------------------------------------
    // Sessions
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sessions_CreateThenGetThenDelete()
    {
        var client = _factory.CreateClient();
        var files = await LoadEncoding();

        var create = await client.PostAsJsonAsync("/api/sessions", new
        {
            files,
            seed = 7,
            inputs = new[] { "pass", "pass", "pass", "pass" },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        string id = created.GetProperty("sessionId").GetString()!;
        Assert.False(string.IsNullOrEmpty(id));

        // State endpoint returns the same shape.
        var state = await client.GetAsync($"/api/sessions/{id}/state");
        state.EnsureSuccessStatusCode();

        var del = await client.DeleteAsync($"/api/sessions/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var again = await client.GetAsync($"/api/sessions/{id}/state");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Sessions_Unknown_ReturnsNotFound()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/sessions/does-not-exist/state");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -----------------------------------------------------------------------
    // Static playground
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Root_ReturnsPlaygroundHtml()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("CCGNF Playground", html);
    }
}
