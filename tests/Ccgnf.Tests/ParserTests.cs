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
    public void EntityMultiParamIndex_Parses()
    {
        // Grammar extension §M2.1: `Entity Name[a, b] { ... }`.
        Assert.False(ParseRaw("Entity Link[owner, slot] { kind: Link }").HasErrors);
    }

    [Fact]
    public void IndexedTargetPath_Parses()
    {
        // Grammar extension §M2.2: `Name[idx].field += expr`.
        Assert.False(ParseRaw("Link[Player1, 0].abilities += NoOp").HasErrors);
    }

    [Fact]
    public void MultiArgIndexTrailer_Parses()
    {
        // Grammar extension §M2.3: `Name[a, b]` as an expression atom.
        Assert.False(ParseRaw("Game.x += DealDamage(Link[Player1, 0], 1)").HasErrors);
    }

    [Fact]
    public void IndexedFieldKey_Parses()
    {
        // Grammar extension §M2.4: `collapsed_for[Player1]: false`.
        var r = ParseRaw("""
            Entity F {
              state_flags: {
                collapsed_for[Player1]: false,
                collapsed_for[Player2]: false
              }
            }
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void UnaryPlus_Parses()
    {
        // Grammar extension §M2.5: `+1` as a unary-plus literal.
        Assert.False(ParseRaw("Game.x += IncCounter(self, c, +1)").HasErrors);
    }

    [Fact]
    public void BlockComment_TopLevel_Stripped()
    {
        Assert.False(ParseRaw("/* before */ Entity Foo { /* in block */ kind: Foo }").HasErrors);
    }

    [Fact]
    public void BlockComment_MultiLine_Stripped()
    {
        Assert.False(ParseRaw("""
            /* This is a
               multi-line
               block comment */
            Entity Foo { kind: Foo }
            """).HasErrors);
    }

    [Fact]
    public void BlockComment_InsideExpression_Stripped()
    {
        Assert.False(ParseRaw("""
            Game.x += Triggered(
              on:     Event.X,
              /* comment between fields */
              effect: Sequence([ /* inline */ NoOp, /**/ NoOp ]))
            """).HasErrors);
    }

    [Fact]
    public void BlockComment_EmptyAndSingleLine_Stripped()
    {
        Assert.False(ParseRaw("/**/Entity/**/Foo/**/{/**/kind:/**/Foo/**/}").HasErrors);
    }

    [Fact]
    public void BlockComment_ContainingDoubleSlash_DoesNotStartLineComment()
    {
        // Make sure `// ...` inside a block comment doesn't extend the block
        // to EOL. The block ends at its `*/`.
        var r = ParseRaw("""
            Entity Foo {
              /* contains // tokens and */
              kind: Foo
            }
            """);
        Assert.False(r.HasErrors);
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
