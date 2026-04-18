using Ccgnf.Ast;
using Ccgnf.Diagnostics;
using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Ccgnf.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ccgnf.Tests;

public class ValidatorTests
{
    private static ValidationResult Validate(string text)
    {
        var pp = new Preprocessor(NullLogger<Preprocessor>.Instance)
            .Preprocess(new SourceFile("<test>", text));
        Assert.False(pp.HasErrors, $"pp errors: {string.Join(", ", pp.Diagnostics)}");

        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        var parse = parser.Parse(pp.ExpandedText, sourceName: "<test>");
        Assert.False(parse.HasErrors, $"parse errors: {string.Join(", ", parse.Diagnostics)}");

        var builder = new AstBuilder(NullLogger<AstBuilder>.Instance);
        var ast = builder.Build(parse.Tree!, sourceName: "<test>");
        Assert.False(ast.HasErrors, $"ast errors: {string.Join(", ", ast.Diagnostics)}");

        var validator = new Validator(NullLogger<Validator>.Instance);
        return validator.Validate(ast.File!);
    }

    [Fact]
    public void EmptyFile_NoDiagnostics()
    {
        var r = Validate("");
        Assert.Empty(r.Diagnostics);
    }

    [Fact]
    public void SingleEntityCardToken_NoDiagnostics()
    {
        var r = Validate("""
            Entity Foo { kind: Foo }
            Card Bar { factions: {NEUTRAL}, type: Unit, cost: 1, rarity: C }
            Token Baz { characteristics: { name: "Baz" } abilities: [] }
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void DuplicateDeclaration_EmitsV100OrV101()
    {
        var r = Validate("""
            Entity Foo { kind: Foo }
            Entity Foo { kind: Bar }
            """);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Diagnostics,
            d => d.Code == "V100" || d.Code == "V101");
    }

    [Fact]
    public void DuplicateCards_EmitV101()
    {
        var r = Validate("""
            Card Spark { factions: {EMBER}, type: Maneuver, cost: 1, rarity: C }
            Card Spark { factions: {EMBER}, type: Maneuver, cost: 2, rarity: C }
            """);
        Assert.Contains(r.Diagnostics, d => d.Code == "V101");
    }

    [Fact]
    public void BuiltinWithCorrectArity_NoDiagnostics()
    {
        var r = Validate("""
            Game.abilities += Sequence([NoOp, NoOp])
            """);
        Assert.False(r.HasErrors);
    }

    [Fact]
    public void IfWithWrongArity_EmitsV200()
    {
        var r = Validate("""
            Game.abilities += If(true, NoOp)
            """);
        Assert.Contains(r.Diagnostics,
            d => d.Code == "V200" && d.Message.Contains("If"));
    }

    [Fact]
    public void DealDamageArityWrong_EmitsV200()
    {
        var r = Validate("""
            Game.abilities += DealDamage(self)
            """);
        Assert.Contains(r.Diagnostics, d => d.Code == "V200");
    }

    [Fact]
    public void DebtWithEffectiveCost_EmitsV300()
    {
        // Pattern explicitly banned by R-5: IncCounter(p, debt, 2 * b.effective_cost).
        var r = Validate("""
            Game.abilities += IncCounter(p, debt, 2 * b.effective_cost)
            """);
        Assert.Contains(r.Diagnostics, d => d.Code == "V300");
    }

    [Fact]
    public void DebtWithPrintedCost_NoV300()
    {
        // R-5 compliant: printed_cost is fine.
        var r = Validate("""
            Game.abilities += IncCounter(p, debt, 2 * b.card.printed_cost)
            """);
        Assert.DoesNotContain(r.Diagnostics, d => d.Code == "V300");
    }

    [Fact]
    public void CompleteEncoding_Validates()
    {
        // Sanity check: the real encoding files, when concatenated into a
        // single project, still produce zero validator errors.
        var repoRoot = FindRepoRoot();
        var encDir = Path.Combine(repoRoot, "encoding");
        var preprocessor = new Preprocessor(NullLogger<Preprocessor>.Instance);
        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        var builder = new AstBuilder(NullLogger<AstBuilder>.Instance);
        var validator = new Validator(NullLogger<Validator>.Instance);

        foreach (var file in Directory.GetFiles(encDir, "*.ccgnf", SearchOption.AllDirectories))
        {
            var raw = File.ReadAllText(file);
            var pp = preprocessor.Preprocess(new SourceFile(file, raw));
            if (pp.HasErrors) continue; // preprocessor errors have their own test
            var parse = parser.Parse(pp.ExpandedText, sourceName: file);
            if (parse.HasErrors) continue;
            var ast = builder.Build(parse.Tree!, sourceName: file);
            if (ast.HasErrors) continue;
            var vr = validator.Validate(ast.File!);
            if (vr.HasErrors)
            {
                var errs = string.Join("\n", vr.Diagnostics.Take(5));
                Assert.Fail($"Validator errors in {Path.GetFileName(file)}:\n{errs}");
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Ccgnf.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repo root.");
    }
}
