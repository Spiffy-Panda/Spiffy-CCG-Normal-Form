namespace Ccgnf.Rest.Serialization;

// -----------------------------------------------------------------------------
// Request / response DTOs for the Ccgnf.Rest surface. Kept tiny and flat so
// they serialize to JSON without custom converters. Each pipeline stage has
// its own response shape; shared types (diagnostics, source files) live
// alongside.
// -----------------------------------------------------------------------------

public sealed record SourceFileDto(string Path, string Content);

public sealed record ProjectRequest(SourceFileDto[]? Files);

public sealed record RunRequest(
    SourceFileDto[]? Files,
    int Seed = 0,
    string[]? Inputs = null,
    int DeckSize = 30);

public sealed record SessionCreateRequest(
    SourceFileDto[]? Files,
    int Seed = 0,
    string[]? Inputs = null,
    int DeckSize = 30);

public sealed record DiagnosticDto(
    string Severity,
    string Code,
    string Message,
    string File,
    int Line,
    int Column);

public sealed record PreprocessResponse(
    bool Ok,
    string Expanded,
    IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record ParseResponse(
    bool Ok,
    int TokenCount,
    IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record AstResponse(
    bool Ok,
    int DeclarationCount,
    IReadOnlyDictionary<string, int> DeclarationsByKind,
    IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record ValidateResponse(
    bool Ok,
    IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record RunResponse(
    bool Ok,
    GameStateDto? State,
    IReadOnlyList<DiagnosticDto> Diagnostics);

public sealed record SessionCreateResponse(
    string SessionId,
    GameStateDto? State,
    IReadOnlyList<DiagnosticDto> Diagnostics);
