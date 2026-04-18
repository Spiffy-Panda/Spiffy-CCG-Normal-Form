using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ccgnf.Rest.Tests;

public class ProjectEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProjectEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Project_ReportsFilesAndMacros()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/project");
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("files").GetArrayLength() >= 22,
            "Expected ≥22 CCGNF files loaded.");
        Assert.True(doc.GetProperty("macros").GetArrayLength() > 0,
            "Expected a non-empty macro inventory.");

        var counts = doc.GetProperty("declarations").GetProperty("counts");
        Assert.True(counts.TryGetProperty("Card", out var cardCount));
        // Matches the current corpus floor; see CardsEndpointsTests.
        Assert.True(cardCount.GetInt32() >= 100);
    }

    [Fact]
    public async Task ProjectFile_ReturnsRequestedContent()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/project/file?path=encoding/engine/04-entities.ccgnf");
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("Entity", text);
    }

    [Fact]
    public async Task ProjectFile_RejectsPathTraversal()
    {
        var client = _factory.CreateClient();

        var dotDot = await client.GetAsync("/api/project/file?path=../etc/passwd");
        Assert.True(
            dotDot.StatusCode == HttpStatusCode.BadRequest ||
            dotDot.StatusCode == HttpStatusCode.NotFound,
            $"Expected 400 or 404 for traversal path, got {dotDot.StatusCode}.");

        var absolute = await client.GetAsync("/api/project/file?path=/etc/passwd");
        Assert.True(
            absolute.StatusCode == HttpStatusCode.BadRequest ||
            absolute.StatusCode == HttpStatusCode.NotFound,
            $"Expected 400 or 404 for absolute path, got {absolute.StatusCode}.");
    }

    [Fact]
    public async Task ProjectFile_UnknownPath_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/project/file?path=encoding/does-not-exist.ccgnf");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
