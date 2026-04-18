using Ccgnf.Ast;
using Ccgnf.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ccgnf.Tests;

public class AstBuilderTests
{
    private static AstFile Build(string text)
    {
        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        var parse = parser.Parse(text, sourceName: "<test>");
        Assert.False(parse.HasErrors, $"parse errors: {string.Join(", ", parse.Diagnostics)}");

        var builder = new AstBuilder(NullLogger<AstBuilder>.Instance);
        var ast = builder.Build(parse.Tree!, sourceName: "<test>");
        Assert.False(ast.HasErrors, $"ast errors: {string.Join(", ", ast.Diagnostics)}");
        Assert.NotNull(ast.File);
        return ast.File!;
    }

    [Fact]
    public void EmptyFile_ProducesEmptyDeclarationsList()
    {
        var ast = Build("");
        Assert.Empty(ast.Declarations);
    }

    [Fact]
    public void EntityDecl_CapturesName()
    {
        var ast = Build("Entity Foo { kind: Game }");
        var entity = Assert.Single(ast.Declarations);
        Assert.IsType<AstEntityDecl>(entity);
        Assert.Equal("Foo", ((AstEntityDecl)entity).Name);
    }

    [Fact]
    public void ParameterizedEntity_CapturesIndexParams()
    {
        var ast = Build("Entity Conduit[owner, arena] { kind: Conduit }");
        var e = Assert.IsType<AstEntityDecl>(ast.Declarations[0]);
        Assert.Equal(new[] { "owner", "arena" }, e.IndexParams);
    }

    [Fact]
    public void EntityAugment_CapturesTargetPath()
    {
        var ast = Build("Game.abilities += NoOp");
        var aug = Assert.IsType<AstEntityAugment>(ast.Declarations[0]);
        Assert.Equal(2, aug.Target.Segments.Count);
        Assert.Equal("Game", aug.Target.Segments[0].Name);
        Assert.Equal("abilities", aug.Target.Segments[1].Name);
    }

    [Fact]
    public void IndexedTargetPath_CapturesIndices()
    {
        var ast = Build("Arena[pos].derived += NoOp");
        var aug = Assert.IsType<AstEntityAugment>(ast.Declarations[0]);
        var first = aug.Target.Segments[0];
        Assert.Equal("Arena", first.Name);
        Assert.Single(first.Indices);
        Assert.IsType<AstIdent>(first.Indices[0]);
    }

    [Fact]
    public void CardDecl_CapturesBlock()
    {
        var ast = Build("""
            Card Spark {
              factions: {EMBER}, type: Maneuver, cost: 1, rarity: C
            }
            """);
        var card = Assert.IsType<AstCardDecl>(ast.Declarations[0]);
        Assert.Equal("Spark", card.Name);
        Assert.NotEmpty(card.Body.Fields);
    }

    [Fact]
    public void IntegerLiteral_ParsesToAstIntLit()
    {
        var ast = Build("Entity Foo { n: 42 }");
        var field = ((AstEntityDecl)ast.Declarations[0]).Body.Fields[0];
        var expr = Assert.IsType<AstFieldExpr>(field.Value);
        var lit = Assert.IsType<AstIntLit>(expr.Value);
        Assert.Equal(42, lit.Value);
    }

    [Fact]
    public void StringLiteral_ParsesWithQuotesStripped()
    {
        var ast = Build("""Entity Foo { msg: "hello" }""");
        var field = ((AstEntityDecl)ast.Declarations[0]).Body.Fields[0];
        var lit = Assert.IsType<AstStringLit>(((AstFieldExpr)field.Value).Value);
        Assert.Equal("hello", lit.Value);
    }

    [Fact]
    public void BinaryOp_ChainsCorrectly()
    {
        var ast = Build("Entity Foo { x: 1 + 2 }");
        var value = ((AstFieldExpr)((AstEntityDecl)ast.Declarations[0]).Body.Fields[0].Value).Value;
        var bop = Assert.IsType<AstBinaryOp>(value);
        Assert.Equal("+", bop.Op);
    }

    [Fact]
    public void UnaryPlus_ProducesUnaryOp()
    {
        var ast = Build("Entity Foo { x: +1 }");
        var value = ((AstFieldExpr)((AstEntityDecl)ast.Declarations[0]).Body.Fields[0].Value).Value;
        var uop = Assert.IsType<AstUnaryOp>(value);
        Assert.Equal("+", uop.Op);
    }

    [Fact]
    public void FunctionCall_CapturesArgsAndNamedArgs()
    {
        var ast = Build("Game.x += DealDamage(self, amount: 3)");
        var aug = (AstEntityAugment)ast.Declarations[0];
        var call = Assert.IsType<AstFunctionCall>(aug.Value);
        Assert.Equal(2, call.Args.Count);
        Assert.IsType<AstArgPositional>(call.Args[0]);
        Assert.IsType<AstArgNamed>(call.Args[1]);
    }

    [Fact]
    public void ArgBinding_CapturesNameEqualsValue()
    {
        var ast = Build("Game.x += Triggered(on: Event.Foo(target=self))");
        // Walk: Triggered(on: Event.Foo(target=self))
        //   -> AstFunctionCall(Triggered, [AstArgNamed(on, AstFunctionCall(Event.Foo, [AstArgBinding(target, self)]))])
        var aug = (AstEntityAugment)ast.Declarations[0];
        var triggered = Assert.IsType<AstFunctionCall>(aug.Value);
        var on = Assert.IsType<AstArgNamed>(triggered.Args[0]);
        var innerCall = Assert.IsType<AstFunctionCall>(on.Value);
        Assert.IsType<AstArgBinding>(innerCall.Args[0]);
    }

    [Fact]
    public void Lambda_ProducesAstLambdaWithParams()
    {
        var ast = Build("Game.x += ForEach(u -> u.type, NoOp)");
        var aug = (AstEntityAugment)ast.Declarations[0];
        var call = (AstFunctionCall)aug.Value;
        var lambda = Assert.IsType<AstLambda>(((AstArgPositional)call.Args[0]).Value);
        Assert.Equal(new[] { "u" }, lambda.Parameters);
    }

    [Fact]
    public void Range_ParsesStartAndEnd()
    {
        var ast = Build("Game.x += ForEach(i ∈ [1..5], NoOp)");
        var aug = (AstEntityAugment)ast.Declarations[0];
        var call = (AstFunctionCall)aug.Value;
        var elementOf = (AstBinaryOp)((AstArgPositional)call.Args[0]).Value;
        var range = Assert.IsType<AstRangeLit>(elementOf.Right);
        Assert.IsType<AstIntLit>(range.Start);
        Assert.IsType<AstIntLit>(range.End);
    }

    [Fact]
    public void E2EFixture_BuildsAstWithoutErrors()
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "fixtures", "e2e-grammar-coverage.ccgnf");
        Assert.True(File.Exists(path));

        var raw = File.ReadAllText(path);
        var pp = new Ccgnf.Preprocessing.Preprocessor(
            NullLogger<Ccgnf.Preprocessing.Preprocessor>.Instance)
            .Preprocess(new Ccgnf.Preprocessing.SourceFile(path, raw));
        Assert.False(pp.HasErrors);

        var parser = new CcgnfParser(NullLogger<CcgnfParser>.Instance);
        var parse = parser.Parse(pp.ExpandedText, sourceName: path);
        Assert.False(parse.HasErrors);

        var builder = new AstBuilder(NullLogger<AstBuilder>.Instance);
        var ast = builder.Build(parse.Tree!, sourceName: path);

        if (ast.HasErrors)
        {
            var summary = string.Join("\n", ast.Diagnostics.Take(10));
            Assert.Fail($"AST builder had errors:\n{summary}");
        }
        Assert.NotNull(ast.File);
        Assert.NotEmpty(ast.File!.Declarations);
    }
}
