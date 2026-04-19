import type {
  AstResponse,
  CardDto,
  DistributionDto,
  HealthResponse,
  MockPoolRequest,
  MockPoolResponse,
  ParseResponse,
  PresetDeckDto,
  RoomExportDto,
  PreprocessResponse,
  ProjectDto,
  ProjectRequest,
  RoomActionRequest,
  RoomCreateRequest,
  RoomDetailDto,
  RoomJoinResponse,
  RoomSummaryDto,
  RunRequest,
  RunResponse,
  SessionCreateRequest,
  SessionCreateResponse,
  SessionSummary,
  ValidateResponse,
} from "./dtos";

async function request<T>(url: string, init?: RequestInit): Promise<{ ok: boolean; body: T }> {
  const res = await fetch(url, init);
  const body = (await res.json()) as T;
  return { ok: res.ok, body };
}

async function requestText(url: string): Promise<{ ok: boolean; body: string; status: number }> {
  const res = await fetch(url);
  const body = await res.text();
  return { ok: res.ok, body, status: res.status };
}

function postJson<T>(url: string, payload: unknown): Promise<{ ok: boolean; body: T }> {
  return request<T>(url, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(payload),
  });
}

export const api = {
  health: () => request<HealthResponse>("/api/health"),
  preprocess: (req: ProjectRequest) => postJson<PreprocessResponse>("/api/preprocess", req),
  parse: (req: ProjectRequest) => postJson<ParseResponse>("/api/parse", req),
  ast: (req: ProjectRequest) => postJson<AstResponse>("/api/ast", req),
  validate: (req: ProjectRequest) => postJson<ValidateResponse>("/api/validate", req),
  run: (req: RunRequest) => postJson<RunResponse>("/api/run", req),
  createSession: (req: SessionCreateRequest) =>
    postJson<SessionCreateResponse>("/api/sessions", req),
  listSessions: () => request<SessionSummary[]>("/api/sessions"),

  cards: () => request<CardDto[]>("/api/cards"),
  project: () => request<ProjectDto>("/api/project"),
  projectFile: (path: string) =>
    requestText(`/api/project/file?path=${encodeURIComponent(path)}`),
  cardsDistribution: (cards: string[] | null) =>
    postJson<DistributionDto>("/api/cards/distribution", { cards }),
  mockPool: (req: MockPoolRequest) => postJson<MockPoolResponse>("/api/decks/mock-pool", req),
  deckPresets: () => request<PresetDeckDto[]>("/api/decks/presets"),

  createRoom: (req: RoomCreateRequest) => postJson<RoomSummaryDto>("/api/rooms", req),
  listRooms: () => request<RoomSummaryDto[]>("/api/rooms"),
  getRoom: (id: string) => request<RoomDetailDto>(`/api/rooms/${encodeURIComponent(id)}`),
  joinRoom: (
    id: string,
    name: string | null,
    deck: { preset?: string; cards?: { name: string; count: number }[] } | null = null,
  ) =>
    postJson<RoomJoinResponse>(
      `/api/rooms/${encodeURIComponent(id)}/join`,
      { name, deck },
    ),
  submitAction: (id: string, req: RoomActionRequest) =>
    postJson<{ accepted: boolean }>(`/api/rooms/${encodeURIComponent(id)}/actions`, req),
  roomState: (id: string) =>
    request<unknown>(`/api/rooms/${encodeURIComponent(id)}/state`),
  deleteRoom: (id: string) =>
    fetch(`/api/rooms/${encodeURIComponent(id)}`, { method: "DELETE" }),
  exportRoom: (id: string) =>
    request<RoomExportDto>(`/api/rooms/${encodeURIComponent(id)}/export`),
};
