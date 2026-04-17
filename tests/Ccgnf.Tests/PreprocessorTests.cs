using Ccgnf.Diagnostics;
using Ccgnf.Preprocessing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ccgnf.Tests;

public class PreprocessorTests
{
    private static PreprocessorResult Run(string text) =>
        new Preprocessor(NullLogger<Preprocessing.Preprocessor>.Instance)
            .Preprocess(new SourceFile("<test>", text));

    [Fact]
    public void EmptyFile_NoDiagnostics_EmptyOutput()
    {
        var r = Run("");
        Assert.False(r.HasErrors);
        Assert.Empty(r.Diagnostics);
        Assert.Equal("", r.ExpandedText);
    }

    [Fact]
    public void PassThrough_NoMacros_TextIsPreserved()
    {
        var r = Run("Entity Foo { kind: Foo }");
        Assert.False(r.HasErrors);
        Assert.Contains("Entity Foo", r.ExpandedText);
        Assert.Contains("kind: Foo", r.ExpandedText);
    }

    [Fact]
    public void DefineDirective_IsStrippedFromOutput()
    {
        var r = Run("define Zero = 0\nEntity Foo {}\n");
        Assert.False(r.HasErrors);
        Assert.DoesNotContain("define", r.ExpandedText);
        Assert.Contains("Entity Foo", r.ExpandedText);
    }

    [Fact]
    public void ZeroArgMacro_ExpandsBareIdentifier()
    {
        var r = Run("""
            define Zero = 0
            Entity Foo { x: Zero }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("x: 0", r.ExpandedText);
    }

    [Fact]
    public void OneArgMacro_ExpandsAndSubstitutes()
    {
        var r = Run("""
            define Identity(x) = x
            Entity Foo { value: Identity(42) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("value: 42", r.ExpandedText);
    }

    [Fact]
    public void MultiArgMacro_SubstitutesInOrder()
    {
        var r = Run("""
            define Sum(a, b) = a + b
            Entity Foo { value: Sum(1, 2) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("1 + 2", r.ExpandedText);
    }

    [Fact]
    public void NestedMacroInvocation_ExpandsRecursively()
    {
        var r = Run("""
            define Zero = 0
            define Wrap(x) = Sequence([x, x])
            Entity Foo { effect: Wrap(Zero) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("Sequence([0, 0])", Normalize(r.ExpandedText));
    }

    [Fact]
    public void WrongArity_EmitsError()
    {
        var r = Run("""
            define Takes2(a, b) = a + b
            Entity Foo { x: Takes2(1) }
            """);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Diagnostics,
            d => d.Code == "PP012" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DuplicateDefine_EmitsError()
    {
        var r = Run("""
            define Foo = 1
            define Foo = 2
            """);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Diagnostics, d => d.Code == "PP001");
    }

    [Fact]
    public void DirectCycle_IsDetected()
    {
        // A macro whose body invokes itself — would loop forever without cycle
        // detection. Arity-0 so it tries to expand on every seen reference.
        // The preprocessor aborts with PP011.
        var r = Run("""
            define Recur = Recur
            Entity Foo { x: Recur }
            """);
        Assert.True(r.HasErrors);
        Assert.Contains(r.Diagnostics, d => d.Code == "PP011");
    }

    [Fact]
    public void ArgumentsRespectNestedParens()
    {
        // Inner parens inside an argument must not prematurely end the arg list.
        var r = Run("""
            define F(x) = x
            Entity Foo { value: F(g(1, 2)) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("g(1, 2)", r.ExpandedText);
    }

    [Fact]
    public void StringLiterals_ArePreservedIncludingCommas()
    {
        // Commas inside string literals must not be treated as argument separators.
        var r = Run("""
            define Wrap(x) = x
            Entity Foo { msg: Wrap("hello, world") }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("\"hello, world\"", r.ExpandedText);
    }

    [Fact]
    public void MultipleMacros_ShareTable()
    {
        var r = Run("""
            define Add(a, b) = a + b
            define Double(x) = Add(x, x)
            Entity Foo { value: Double(7) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("7 + 7", r.ExpandedText);
    }

    [Fact]
    public void BareMacroNameWithoutParens_WhenMacroIsArity0_StillExpands()
    {
        var r = Run("""
            define Pi = 314
            Entity Foo { x: Pi, y: Pi }
            """);
        Assert.False(r.HasErrors);
        var normalized = Normalize(r.ExpandedText);
        Assert.Contains("x: 314", normalized);
        Assert.Contains("y: 314", normalized);
    }

    [Fact]
    public void ArityMacroWithoutParens_LeavesIdentifierAlone()
    {
        // `Takes1` is arity 1 but mentioned without `(`. Preprocessor should
        // leave it alone (it's not a real invocation).
        var r = Run("""
            define Takes1(x) = x
            Entity Foo { ref: Takes1 }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("Takes1", r.ExpandedText);
    }

    [Fact]
    public void BlockComment_InMacroBody_PreservedAsTrivia()
    {
        // A block comment with commas and parens inside a macro body must
        // not interfere with argument parsing when the macro is expanded.
        var r = Run("""
            define Wrap(a, b) = Sequence([
              /* first, with comma and (paren) inside */
              DealDamage(a, b),
              NoOp
            ])
            Entity Foo { effect: Wrap(self, 1) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("DealDamage(self, 1)", Normalize(r.ExpandedText));
    }

    [Fact]
    public void BlockComment_InArgumentList_DoesNotSplitArguments()
    {
        // `Macro(a, /* comment */ b)` must pass `a` and `b` as two arguments.
        var r = Run("""
            define Pair(x, y) = x + y
            Entity Foo { value: Pair(1, /* between args */ 2) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("1 + 2", Normalize(r.ExpandedText));
    }

    [Fact]
    public void BlockComment_WithDoubleSlash_DoesNotExtendToEOL()
    {
        // Block comment containing // must end at its closing */.
        var r = Run("""
            /* comment // with double-slash inside */
            Entity Foo { x: 1 }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("Entity Foo", r.ExpandedText);
    }

    [Fact]
    public void NamedArgLabel_IsNotSubstitutedByMacroParam()
    {
        // Regression for label-position substitution bug. When a macro
        // parameter name collides with a named-arg label in the body,
        // the label must survive expansion.
        var r = Run("""
            define Push(factions) = NewEcho(factions: factions)
            Entity Foo { effect: Push(PYRE) }
            """);
        Assert.False(r.HasErrors);
        Assert.Contains("NewEcho(factions: PYRE)", Normalize(r.ExpandedText));
    }

    private static string Normalize(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
}
