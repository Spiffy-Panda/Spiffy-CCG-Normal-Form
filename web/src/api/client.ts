import type {
  AstResponse,
  HealthResponse,
  ParseResponse,
  PreprocessResponse,
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
};
