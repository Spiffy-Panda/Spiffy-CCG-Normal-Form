using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ccgnf.Rest.Tests;

public class CardsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CardsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cards_ReturnsEveryCardInEncoding()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/cards");
        response.EnsureSuccessStatusCode();

        var cards = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Encoding is still growing toward the 250-card target; assert a
        // floor that matches the current corpus and catches regressions.
        Assert.True(cards.GetArrayLength() >= 100,
            $"Expected ≥100 cards in the catalog, got {cards.GetArrayLength()}.");

        foreach (var card in cards.EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(card.GetProperty("name").GetString()),
                "Card DTO is missing a name.");
            Assert.True(card.GetProperty("factions").GetArrayLength() > 0,
                $"Card {card.GetProperty("name").GetString()} has no factions.");
            Assert.False(string.IsNullOrEmpty(card.GetProperty("type").GetString()),
                $"Card {card.GetProperty("name").GetString()} has no type.");
        }
    }

    [Fact]
    public async Task Cards_SourcePath_ExistsOnDisk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/cards");
        response.EnsureSuccessStatusCode();

        var cards = await response.Content.ReadFromJsonAsync<JsonElement>();
        var repoRoot = FindRepoRoot();
        foreach (var card in cards.EnumerateArray())
        {
            string path = card.GetProperty("sourcePath").GetString()!;
            string absolute = Path.Combine(repoRoot, path);
            Assert.True(File.Exists(absolute), $"Card source path {path} does not resolve.");
        }
    }

    [Fact]
    public async Task Distribution_NoFilter_TotalsMatchCardCount()
    {
        var client = _factory.CreateClient();

        var cardsResponse = await client.GetAsync("/api/cards");
        cardsResponse.EnsureSuccessStatusCode();
        var cards = await cardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        int cardCount = cards.GetArrayLength();

        var distResponse = await client.PostAsJsonAsync("/api/cards/distribution",
            new { cards = (string[]?)null });
        distResponse.EnsureSuccessStatusCode();

        var dist = await distResponse.Content.ReadFromJsonAsync<JsonElement>();
        int typeTotal = 0;
        foreach (var v in dist.GetProperty("type").EnumerateObject())
            typeTotal += v.Value.GetInt32();
        Assert.Equal(cardCount, typeTotal);

        int costTotal = 0;
        foreach (var v in dist.GetProperty("cost").EnumerateObject())
            costTotal += v.Value.GetInt32();
        Assert.Equal(cardCount, costTotal);
    }

    [Fact]
    public async Task Distribution_SubsetFilter_TotalsMatchSubset()
    {
        var client = _factory.CreateClient();
        var cardsResponse = await client.GetAsync("/api/cards");
        cardsResponse.EnsureSuccessStatusCode();

        var cards = await cardsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var subset = cards.EnumerateArray()
            .Take(10)
            .Select(c => c.GetProperty("name").GetString()!)
            .ToArray();

        var distResponse = await client.PostAsJsonAsync("/api/cards/distribution",
            new { cards = subset });
        distResponse.EnsureSuccessStatusCode();

        var dist = await distResponse.Content.ReadFromJsonAsync<JsonElement>();
        int typeTotal = 0;
        foreach (var v in dist.GetProperty("type").EnumerateObject())
            typeTotal += v.Value.GetInt32();
        Assert.Equal(subset.Length, typeTotal);
    }

    [Fact]
    public async Task Distribution_ExtractsText_WhenPresent()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/cards");
        response.EnsureSuccessStatusCode();

        var cards = await response.Content.ReadFromJsonAsync<JsonElement>();
        int withText = 0;
        foreach (var card in cards.EnumerateArray())
        {
            if (!string.IsNullOrEmpty(card.GetProperty("text").GetString())) withText++;
        }
        Assert.True(withText > 0, "Expected at least one card with extracted text.");
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
}
