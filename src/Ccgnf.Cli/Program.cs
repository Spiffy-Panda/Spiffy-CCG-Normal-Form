using Ccgnf.Ast;
using Ccgnf.Diagnostics;
using Ccgnf.Interpreter;
using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Ccgnf.Validation;
using Microsoft.Extensions.Logging;
using InterpreterRt = Ccgnf.Interpreter.Interpreter;

// Ccgnf CLI — smoke driver for the preprocessor -> parser pipeline.
//
// Usage:
//   ccgnf <file.ccgnf> [<file.ccgnf> ...]
//   ccgnf --log-level {trace|debug|information|warning|error} <file> ...
//   ccgnf --run <file> ...
//
// --run: treat all <file> args as a single project, load through the full
// pipeline, and execute via the v1 interpreter (Setup through the first
// Round-1 Rise phase — see grammar/GrammarSpec.md §8).
//
// Exits 0 on zero diagnostics, 1 on any parse or preprocessor error.

var minLevel = LogLevel.Information;
var files = new List<string>();
var dumpPp = false;
var run = false;
var runSeed = 0;

for (int i = 0; i < args.Length; i++)
{
    var a = args[i];
    if (a is "-h" or "--help")
    {
        PrintHelp();
        return 0;
    }
    if (a == "--dump-pp")
    {
        dumpPp = true;
        continue;
    }
    if (a == "--run")
    {
        run = true;
        continue;
    }
    if (a == "--seed")
    {
        if (i + 1 >= args.Length || !int.TryParse(args[++i], out runSeed))
        {
            Console.Error.WriteLine("--seed requires an integer argument.");
            return 2;
        }
        continue;
    }
    if (a == "--log-level")
    {
        if (i + 1 >= args.Length)
        {
            Console.Error.WriteLine("--log-level requires an argument.");
            return 2;
        }
        if (!Enum.TryParse<LogLevel>(args[++i], ignoreCase: true, out minLevel))
        {
            Console.Error.WriteLine($"Unknown log level: {args[i]}");
            return 2;
        }
        continue;
    }
    if (a.StartsWith('-'))
    {
        Console.Error.WriteLine($"Unknown option: {a}");
        PrintHelp();
        return 2;
    }
    files.Add(a);
}

if (files.Count == 0)
{
    PrintHelp();
    return 2;
}

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(minLevel);
    builder.AddSimpleConsole(o =>
    {
        o.IncludeScopes = false;
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss.fff ";
    });
});

var log = loggerFactory.CreateLogger("Ccgnf.Cli");

if (run)
{
    var loader = new ProjectLoader(loggerFactory);
    var result = loader.LoadFromFiles(files, sourceName: "<project>");
    ReportDiagnostics(result.Diagnostics);
    if (result.HasErrors || result.File is null) return 1;

    var interpreter = new InterpreterRt(
        loggerFactory.CreateLogger<InterpreterRt>(),
        loggerFactory);
    var state = interpreter.Run(result.File, new InterpreterOptions { Seed = runSeed });

    log.LogInformation(
        "Interpreter run complete: players={Players}, arenas={Arenas}, conduits={Conduits}, steps={Steps}",
        state.Players.Count,
        state.Arenas.Count,
        state.Entities.Values.Count(e => e.Kind == "Conduit"),
        state.StepCount);
    return 0;
}

var preprocessor = new Preprocessor(loggerFactory.CreateLogger<Preprocessor>());
var parser = new CcgnfParser(loggerFactory.CreateLogger<CcgnfParser>());
var astBuilder = new AstBuilder(loggerFactory.CreateLogger<AstBuilder>());
var validator = new Validator(loggerFactory.CreateLogger<Validator>());

int errorCount = 0;
foreach (var file in files)
{
    if (!File.Exists(file))
    {
        log.LogError("File not found: {File}", file);
        errorCount++;
        continue;
    }

    var text = File.ReadAllText(file);
    var source = new SourceFile(file, text);

    log.LogInformation("Preprocessing {File} ({Length} chars)", file, text.Length);
    var ppResult = preprocessor.Preprocess(source);
    ReportDiagnostics(ppResult.Diagnostics);

    if (dumpPp)
    {
        var dumpPath = file + ".expanded";
        File.WriteAllText(dumpPath, ppResult.ExpandedText);
        log.LogInformation("Wrote expanded output to {DumpPath}", dumpPath);
    }

    if (ppResult.HasErrors)
    {
        errorCount += ppResult.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        continue;
    }

    log.LogInformation("Parsing {File}", file);
    var parseResult = parser.Parse(ppResult.ExpandedText, sourceName: file);
    ReportDiagnostics(parseResult.Diagnostics);

    if (parseResult.HasErrors)
    {
        errorCount += parseResult.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        continue;
    }

    log.LogInformation("Building AST for {File}", file);
    var astResult = astBuilder.Build(parseResult.Tree!, sourceName: file);
    ReportDiagnostics(astResult.Diagnostics);

    if (astResult.HasErrors || astResult.File is null)
    {
        errorCount += astResult.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        continue;
    }

    log.LogInformation("Validating {File}", file);
    var validationResult = validator.Validate(astResult.File);
    ReportDiagnostics(validationResult.Diagnostics);

    if (validationResult.HasErrors)
    {
        errorCount += validationResult.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        continue;
    }

    log.LogInformation("OK: {File} parsed, built, and validated successfully", file);
}

return errorCount == 0 ? 0 : 1;

static void PrintHelp()
{
    Console.WriteLine("""
        ccgnf — CCGNF pipeline smoke driver.

        Usage:
          ccgnf [options] <file.ccgnf> [<file.ccgnf> ...]

        Options:
          --log-level <level>   trace|debug|information|warning|error
                                (default: information)
          --run                 execute the given files as one project via
                                the v1 interpreter (Setup -> Round-1 Rise)
          --seed <int>          RNG seed for --run (default: 0)
          --dump-pp             write post-preprocess text to <file>.expanded
          -h, --help            show this help

        Exit code: 0 on zero diagnostics, 1 on errors, 2 on CLI misuse.
        """);
}

static void ReportDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
{
    foreach (var d in diagnostics)
    {
        var stream = d.Severity == DiagnosticSeverity.Error ? Console.Error : Console.Out;
        stream.WriteLine(d.ToString());
    }
}
