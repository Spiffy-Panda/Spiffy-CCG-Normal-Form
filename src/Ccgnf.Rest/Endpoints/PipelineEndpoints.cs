using Ccgnf.Ast;
using Ccgnf.Diagnostics;
using Ccgnf.Interpreter;
using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Ccgnf.Rest.Serialization;
using Ccgnf.Validation;
using Microsoft.Extensions.Logging;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

namespace Ccgnf.Rest.Endpoints;

/// <summary>
/// Exposes each pipeline stage — preprocess, parse, AST build, validate, and
/// interpreter run — as an independent POST endpoint. Every stage accepts the
/// same request shape (a list of source files) so clients can pipe one stage
/// into the next or jump straight to the stage they care about.
/// </summary>
internal static class PipelineEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/preprocess", Preprocess);
        app.MapPost("/api/parse", Parse);
        app.MapPost("/api/ast", BuildAst);
        app.MapPost("/api/validate", Validate);
        app.MapPost("/api/run", Run);
    }

    // -------------------------------------------------------------------------

    private static IResult Preprocess(ProjectRequest req, ILoggerFactory lf)
    {
        var sources = ToSourceFiles(req);
        var preprocessor = new Preprocessor(lf.CreateLogger<Preprocessor>());
        var result = preprocessor.Preprocess(sources);
        return Results.Ok(new PreprocessResponse(
            Ok: !result.HasErrors,
            Expanded: result.ExpandedText,
            Diagnostics: DiagnosticMapper.ToDtos(result.Diagnostics)));
    }

    private static IResult Parse(ProjectRequest req, ILoggerFactory lf)
    {
        var diagnostics = new List<Diagnostic>();
        var (pp, ok) = RunPreprocess(req, lf, diagnostics);
        if (!ok) return ResultsFor(new ParseResponse(false, 0, DiagnosticMapper.ToDtos(diagnostics)));

        var parser = new CcgnfParser(lf.CreateLogger<CcgnfParser>());
        var parse = parser.Parse(pp, "<project>");
        diagnostics.AddRange(parse.Diagnostics);
        return Results.Ok(new ParseResponse(
            Ok: !parse.HasErrors,
            TokenCount: parse.Tree?.ChildCount ?? 0,
            Diagnostics: DiagnosticMapper.ToDtos(diagnostics)));
    }

    private static IResult BuildAst(ProjectRequest req, ILoggerFactory lf)
    {
        var loader = new ProjectLoader(lf);
        var res = loader.LoadFromSources(ToSourceFiles(req));
        // On AST failure we still want to report any diagnostics collected.
        var declsByKind = new Dictionary<string, int>();
        if (res.File is not null)
        {
            foreach (var d in res.File.Declarations)
            {
                var key = d switch
                {
                    AstEntityDecl => "Entity",
                    AstCardDecl => "Card",
                    AstTokenDecl => "Token",
                    AstEntityAugment => "Augment",
                    _ => "Other",
                };
                declsByKind[key] = declsByKind.GetValueOrDefault(key) + 1;
            }
        }
        return Results.Ok(new AstResponse(
            Ok: !res.HasErrors && res.File is not null,
            DeclarationCount: res.File?.Declarations.Count ?? 0,
            DeclarationsByKind: declsByKind,
            Diagnostics: DiagnosticMapper.ToDtos(res.Diagnostics)));
    }

    private static IResult Validate(ProjectRequest req, ILoggerFactory lf)
    {
        var loader = new ProjectLoader(lf);
        var res = loader.LoadFromSources(ToSourceFiles(req));
        return Results.Ok(new ValidateResponse(
            Ok: !res.HasErrors,
            Diagnostics: DiagnosticMapper.ToDtos(res.Diagnostics)));
    }

    private static IResult Run(RunRequest req, ILoggerFactory lf)
    {
        var loader = new ProjectLoader(lf);
        var load = loader.LoadFromSources(ToSourceFiles(req.Files));
        if (load.HasErrors || load.File is null)
        {
            return Results.Ok(new RunResponse(
                Ok: false,
                State: null,
                Diagnostics: DiagnosticMapper.ToDtos(load.Diagnostics)));
        }

        var interpreter = new InterpreterRt(lf.CreateLogger<InterpreterRt>(), lf);
        var state = interpreter.Run(load.File, new InterpreterOptions
        {
            Seed = req.Seed,
            Inputs = BuildInputs(req.Inputs),
            DefaultDeckSize = req.DeckSize > 0 ? req.DeckSize : 30,
        });

        return Results.Ok(new RunResponse(
            Ok: true,
            State: StateMapper.ToDto(state),
            Diagnostics: DiagnosticMapper.ToDtos(load.Diagnostics)));
    }

    // -------------------------------------------------------------------------

    private static List<SourceFile> ToSourceFiles(ProjectRequest req) =>
        ToSourceFiles(req.Files);

    internal static List<SourceFile> ToSourceFiles(SourceFileDto[]? files) =>
        (files ?? Array.Empty<SourceFileDto>())
            .Select(f => new SourceFile(f.Path, f.Content))
            .ToList();

    internal static IHostInputQueue BuildInputs(string[]? values)
    {
        var parsed = (values ?? Array.Empty<string>())
            .Select<string, RtValue>(ParseInput)
            .ToList();
        return new QueuedInputs(parsed);
    }

    private static RtValue ParseInput(string raw)
    {
        if (int.TryParse(raw, out var i)) return new RtInt(i);
        if (raw == "true") return new RtBool(true);
        if (raw == "false") return new RtBool(false);
        // Bare word -> symbol; anything else -> string.
        bool isBareIdent = raw.Length > 0 && char.IsLetter(raw[0])
            && raw.All(c => char.IsLetterOrDigit(c) || c == '_');
        return isBareIdent ? new RtSymbol(raw) : new RtString(raw);
    }

    private static (string expandedText, bool ok) RunPreprocess(
        ProjectRequest req, ILoggerFactory lf, List<Diagnostic> diagnostics)
    {
        var pp = new Preprocessor(lf.CreateLogger<Preprocessor>())
            .Preprocess(ToSourceFiles(req));
        diagnostics.AddRange(pp.Diagnostics);
        return (pp.ExpandedText, !pp.HasErrors);
    }

    private static IResult ResultsFor(ParseResponse r) => Results.Ok(r);
}
