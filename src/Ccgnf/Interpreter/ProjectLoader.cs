using Ccgnf.Ast;
using Ccgnf.Diagnostics;
using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Ccgnf.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ccgnf.Interpreter;

/// <summary>
/// Loads a CCGNF project — one-or-more <c>.ccgnf</c> source files — through
/// the full preprocess → parse → AST-build → validate pipeline and returns a
/// single <see cref="AstFile"/> ready for the interpreter. Wraps the stages
/// hosts would otherwise reassemble by hand.
/// </summary>
public sealed class ProjectLoadResult
{
    public AstFile? File { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Names of every <c>define</c> macro surfaced by the preprocessor. Empty
    /// when preprocessing failed before any directives were collected.
    /// </summary>
    public IReadOnlyList<string> MacroNames { get; }

    public bool HasErrors { get; }

    public ProjectLoadResult(
        AstFile? file,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string>? macroNames = null)
    {
        File = file;
        Diagnostics = diagnostics;
        MacroNames = macroNames ?? Array.Empty<string>();
        HasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
    }
}

public sealed class ProjectLoader
{
    private readonly ILoggerFactory _loggerFactory;

    public ProjectLoader(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    public ProjectLoadResult LoadFromFiles(IEnumerable<string> paths, string sourceName = "<project>")
    {
        var sources = new List<SourceFile>();
        foreach (var p in paths) sources.Add(new SourceFile(p, System.IO.File.ReadAllText(p)));
        return LoadFromSources(sources, sourceName);
    }

    public ProjectLoadResult LoadFromDirectory(string directory, string sourceName = "<project>")
    {
        var files = System.IO.Directory
            .GetFiles(directory, "*.ccgnf", System.IO.SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);
        return LoadFromFiles(files, sourceName);
    }

    public ProjectLoadResult LoadFromSources(IEnumerable<SourceFile> sources, string sourceName = "<project>")
    {
        var diagnostics = new List<Diagnostic>();

        var preprocessor = new Preprocessor(_loggerFactory.CreateLogger<Preprocessor>());
        var pp = preprocessor.Preprocess(sources);
        diagnostics.AddRange(pp.Diagnostics);
        if (pp.HasErrors) return new ProjectLoadResult(null, diagnostics, pp.MacroNames);

        var parser = new CcgnfParser(_loggerFactory.CreateLogger<CcgnfParser>());
        var parse = parser.Parse(pp.ExpandedText, sourceName);
        diagnostics.AddRange(parse.Diagnostics);
        if (parse.HasErrors || parse.Tree is null) return new ProjectLoadResult(null, diagnostics, pp.MacroNames);

        var builder = new AstBuilder(_loggerFactory.CreateLogger<AstBuilder>());
        var ast = builder.Build(parse.Tree, sourceName);
        diagnostics.AddRange(ast.Diagnostics);
        if (ast.HasErrors || ast.File is null) return new ProjectLoadResult(null, diagnostics, pp.MacroNames);

        var validator = new Validator(_loggerFactory.CreateLogger<Validator>());
        var validation = validator.Validate(ast.File);
        diagnostics.AddRange(validation.Diagnostics);
        if (validation.HasErrors) return new ProjectLoadResult(null, diagnostics, pp.MacroNames);

        return new ProjectLoadResult(ast.File, diagnostics, pp.MacroNames);
    }
}
