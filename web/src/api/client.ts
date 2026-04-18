import type {
  AstResponse,
  CardDto,
  DistributionDto,
  HealthResponse,
  ParseResponse,
  PreprocessResponse,
  ProjectDto,
  ProjectRequest,
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
};
