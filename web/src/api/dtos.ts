export type Severity = "Error" | "Warning" | "Info";

export interface SourceFileDto {
  path: string;
  content: string;
}

export interface DiagnosticDto {
  severity: Severity;
  code: string;
  message: string;
  file?: string | null;
  line?: number | null;
  column?: number | null;
}

export interface ProjectRequest {
  files: SourceFileDto[];
}

export interface RunRequest extends ProjectRequest {
  seed: number;
  inputs?: string[];
  deckSize?: number;
}

export type SessionCreateRequest = RunRequest;

export interface HealthResponse {
  ok: boolean;
  service: string;
  port: number;
}

export interface PreprocessResponse {
  ok: boolean;
  expanded: string;
  diagnostics: DiagnosticDto[];
}

export interface ParseResponse {
  ok: boolean;
  tokenCount: number;
  diagnostics: DiagnosticDto[];
}

export interface AstResponse {
  ok: boolean;
  declarationCount: number;
  declarationsByKind: Record<string, number>;
  diagnostics: DiagnosticDto[];
}

export interface ValidateResponse {
  ok: boolean;
  diagnostics: DiagnosticDto[];
}

export interface RunResponse {
  ok: boolean;
  state: unknown;
  diagnostics: DiagnosticDto[];
}

export interface SessionCreateResponse {
  sessionId: string;
  state: unknown;
  diagnostics: DiagnosticDto[];
}

export interface SessionSummary {
  sessionId: string;
  seed: number;
  createdAt: string;
  stepCount: number;
  gameOver: boolean;
}

export type PipelineStage = "preprocess" | "parse" | "ast" | "validate";

export interface CardDto {
  name: string;
  factions: string[];
  type: string;
  cost: number | null;
  rarity: string;
  keywords: string[];
  text: string;
  sourcePath: string;
  sourceLine: number;
}

export interface ProjectFileDto {
  path: string;
  bytes: number;
}

export interface ProjectDeclarationsDto {
  counts: Record<string, number>;
  byFile: Record<string, string[]>;
}

export interface ProjectDto {
  files: ProjectFileDto[];
  macros: string[];
  declarations: ProjectDeclarationsDto;
  loadedAt: string;
}

export interface DistributionDto {
  faction: Record<string, number>;
  type: Record<string, number>;
  cost: Record<string, number>;
  rarity: Record<string, number>;
}
