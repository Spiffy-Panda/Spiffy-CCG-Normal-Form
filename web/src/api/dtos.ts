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
  abilitiesText: string[];
  sourcePath: string;
  sourceLine: number;
}

export interface ProjectFileDto {
  path: string;
  bytes: number;
}

export interface ProjectDeclarationEntry {
  label: string;
  line: number;
}

export interface ProjectDeclarationsDto {
  counts: Record<string, number>;
  byFile: Record<string, ProjectDeclarationEntry[]>;
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

export interface MockPoolRequest {
  format: string;
  seed: number;
  size: number;
}

export interface MockPoolResponse {
  format: string;
  seed: number;
  cards: string[];
}

export interface DeckCardEntry {
  name: string;
  count: number;
}

export interface PresetDeckDto {
  id: string;
  name: string;
  format: string;
  factions: string[];
  description: string;
  cards: DeckCardEntry[];
  cardCount: number;
  unknownCards: string[];
}

export interface RoomCpuSeatSpec {
  name?: string | null;
  deck?: RoomDeckSpec | null;
}

export interface RoomCreateRequest {
  files: SourceFileDto[];
  seed: number;
  playerSlots?: number;
  deckSize?: number;
  cpuSeats?: RoomCpuSeatSpec[];
}

export interface RoomSummaryDto {
  roomId: string;
  state: "WaitingForPlayers" | "Active" | "Finished";
  seed: number;
  playerSlots: number;
  occupied: number;
  createdAt: string;
}

export interface RoomPlayerDto {
  playerId: number;
  name: string;
  connected: boolean;
  deckName?: string | null;
  seatKind?: "Human" | "Cpu";
}

export interface RoomDeckSpec {
  preset?: string | null;
  cards?: DeckCardEntry[] | null;
}

export interface RoomDetailDto extends RoomSummaryDto {
  lastActivityAt: string;
  players: RoomPlayerDto[];
}

export interface RoomJoinResponse {
  playerId: number;
  token: string;
  state: unknown;
}

export interface RoomActionRequest {
  playerId: number;
  token: string;
  action: string;
}

export interface RoomEventFrame {
  step: number;
  event: { type: string; fields: Record<string, string> };
}

export interface RoomExportPlayerDto {
  playerId: number;
  name: string;
  deckName?: string | null;
  deckCardNames?: string[] | null;
}

export interface RoomExportDto {
  roomId: string;
  seed: number;
  deckSize: number;
  createdAt: string;
  exportedAt: string;
  lifecycle: string;
  stepCount: number;
  gameOver: boolean;
  players: RoomExportPlayerDto[];
  state: unknown;
}
