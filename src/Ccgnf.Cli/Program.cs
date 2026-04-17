using Ccgnf.Diagnostics;
using Ccgnf.Parsing;
using Ccgnf.Preprocessing;
using Microsoft.Extensions.Logging;

// Ccgnf CLI — smoke driver for the preprocessor -> parser pipeline.
//
// Usage:
//   ccgnf <file.ccgnf> [<file.ccgnf> ...]
//   ccgnf --log-level {trace|debug|information|warning|error} <file> ...
//
// Exits 0 on zero diagnostics, 1 on any parse or preprocessor error.
//
// This is a reference harness; the full CLI surface described in
// grammar/GrammarSpec.md §11.3 is a future expansion.

var minLevel = LogLevel.Information;
var files = new List<string>();
var dumpPp = false;

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
var preprocessor = new Preprocessor(loggerFactory.CreateLogger<Preprocessor>());
var parser = new CcgnfParser(loggerFactory.CreateLogger<CcgnfParser>());

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

    log.LogInformation("OK: {File} parsed successfully", file);
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
