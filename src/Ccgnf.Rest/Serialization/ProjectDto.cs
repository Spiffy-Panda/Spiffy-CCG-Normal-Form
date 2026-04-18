namespace Ccgnf.Rest.Serialization;

// -----------------------------------------------------------------------------
// Project snapshot DTOs. Backs the Raw and Interpreter pages with the list of
// files loaded by the REST host, the preprocessor's macro inventory, and a
// file → declaration index.
// -----------------------------------------------------------------------------

public sealed record ProjectFileDto(string Path, int Bytes);

public sealed record ProjectDeclarationEntry(string Label, int Line);

public sealed record ProjectDeclarationsDto(
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyDictionary<string, IReadOnlyList<ProjectDeclarationEntry>> ByFile);

public sealed record ProjectDto(
    IReadOnlyList<ProjectFileDto> Files,
    IReadOnlyList<string> Macros,
    ProjectDeclarationsDto Declarations,
    string LoadedAt);
