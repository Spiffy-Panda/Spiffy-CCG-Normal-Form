using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ccgnf.Tests;

public class ParserTests
{
    private static ParseResult ParseRaw(string text)
    {
        // Parse directly (skip preprocessor) — useful for grammar-only checks.
        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        return parser.Parse(text, sourceName: "<test>");
    }

    private static ParseResult ParseFull(string text)
    {
        // Preprocess then parse.
        var pp = new Preprocessor(NullLogger<Preprocessing.Preprocessor>.Instance)
            .Preprocess(new SourceFile("<test>", text));
        Assert.False(pp.HasErrors, $"preprocessor errors: {string.Join(", ", pp.Diagnostics)}");
        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        return parser.Parse(pp.ExpandedText, sourceName: "<test>");
    }

    [Fact]
    public void EmptyInput_ParsesToEmptyFile()
    {
        var r = ParseRaw("");
        Assert.False(r.HasErrors);
        Assert.NotNull(r.Tree);
    }

    [Fact]
    public void MinimalEntity_Parses()
    {
        var r = ParseRaw("Entity Foo { }");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void EntityWithFields_Parses()
    {
        var r = ParseRaw("""
            Entity Foo {
              kind: Game
              counter: 42
            }
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void ParameterizedEntity_Parses()
    {
        var r = ParseRaw("Entity Slot[pos] for pos ∈ {Left, Center} { }");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void Card_Parses()
    {
        var r = ParseRaw("""
            Card Cinderling {
              factions: {EMBER}, type: Unit, cost: 1, force: 2, ramparts: 1, rarity: C
              keywords: [ Blitz ]
              abilities: []
            }
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void Token_Parses()
    {
        var r = ParseRaw("""
            Token Sapling {
              characteristics: { name: "Sapling", force: 1, ramparts: 1, rarity: Token }
              keywords: []
              abilities: []
            }
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void EntityAugment_Parses()
    {
        var r = ParseRaw("Game.abilities += Static(modifies: BoardState, rule: NoOp)");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void LambdaSingleParam_Parses()
    {
        var r = ParseRaw("Game.x += ForEach(u -> u.type == Unit, NoOp)");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void LambdaMultiParam_Parses()
    {
        var r = ParseRaw("Game.x += ForEach((a, b) ∈ {1, 2} × {3, 4}, NoOp)");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void TypedDerivedCharacteristic_Parses()
    {
        var r = ParseRaw("""
            Entity Foo {
              derived: {
                live: bool = self.counter > 0
              }
            }
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void LetIn_Parses()
    {
        var r = ParseRaw("Game.x += let n = 5 in If(y > n, a, b)");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void NamedAndPositionalArgs_Parse()
    {
        var r = ParseRaw("Game.x += Func(positional, named: 1, binding=2)");
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void UnicodeAndAsciiLogicalOperators_BothParse()
    {
        Assert.False(ParseRaw("Game.x += (p ∧ q) ∨ (¬ r)").HasErrors);
        Assert.False(ParseRaw("Game.x += (p and q) or (not r)").HasErrors);
    }

    [Fact]
    public void ParseError_ProducesDiagnostic()
    {
        // Missing `{` after entity name.
        var r = ParseRaw("Entity Broken");
        Assert.True(r.HasErrors);
        Assert.NotEmpty(r.Diagnostics);
    }

    [Fact]
    public void E2EFixture_ParsesWithoutErrors()
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "fixtures", "e2e-grammar-coverage.ccgnf");
        Assert.True(File.Exists(path),
            $"Fixture not found at {path}. Check CopyToOutputDirectory in csproj.");

        var text = File.ReadAllText(path);
        var r = ParseFull(text);

        if (r.HasErrors)
        {
            var summary = string.Join("\n", r.Diagnostics.Select(d => d.ToString()));
            Assert.Fail($"E2E fixture parse had errors:\n{summary}");
        }
    }
}
