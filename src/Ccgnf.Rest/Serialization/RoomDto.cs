namespace Ccgnf.Rest.Serialization;

public sealed record RoomCreateRequest(
    SourceFileDto[]? Files,
    int Seed = 0,
    int PlayerSlots = 2,
    int DeckSize = 30);

public sealed record RoomPlayerDto(int PlayerId, string Name, bool Connected);

public sealed record RoomSummaryDto(
    string RoomId,
    string State,
    int Seed,
    int PlayerSlots,
    int Occupied,
    string CreatedAt);

public sealed record RoomDetailDto(
    string RoomId,
    string State,
    int Seed,
    int PlayerSlots,
    int Occupied,
    string CreatedAt,
    string LastActivityAt,
    IReadOnlyList<RoomPlayerDto> Players);

public sealed record RoomJoinRequest(string? Name);

public sealed record RoomJoinResponse(
    int PlayerId,
    string Token,
    GameStateDto? State);

public sealed record RoomActionRequest(int PlayerId, string Token, string Action);

public sealed record RoomActionResponse(bool Accepted);
