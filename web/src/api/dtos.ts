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
  archetypes: string[];
  suggestedAi?: string | null;
}

export interface BotProfileDto {
  id: string;
  name: string;
  description: string;
}

export interface AiWeightsDto {
  source: "file" | "default";
  path?: string | null;
  considerationKeys: string[];
  json: string;
  editorEnabled: boolean;
}

export interface AiPreviewRow {
  kind: string;
  label: string;
  score: number;
  breakdown: Record<string, number>;
}

export interface AiTournamentRow {
  botName: string;
  games: number;
  wins: number;
  losses: number;
  draws: number;
  winRate: number;
  avgSteps: number;
}

export interface AiTournamentResponse {
  deckId: string;
  rows: AiTournamentRow[];
  timestamp: string;
}

// ─── Tournament V2 (heterogeneous deck/bot pairs) ──────────────────────

export interface TournamentPairDto {
  pairId: string;
  deckId: string;
  botProfile: string;
  label?: string | null;
}

export interface TournamentMatchupDto {
  aPairId: string;
  bPairId: string;
}

export type TournamentLogLevel = "silent" | "summary" | "llm";

export interface TournamentConfigV2 {
  version: number;
  name?: string;
  description?: string;
  pairs: TournamentPairDto[];
  matchups?: TournamentMatchupDto[] | null;
  includeMirror: boolean;
  gamesPerMatchup: number;
  baseSeed: number;
  maxInputsPerGame: number;
  maxEventsPerGame: number;
  /** "silent" | "summary" | "llm". Controls verbosity + disk persistence. */
  logLevel?: TournamentLogLevel;
}

export interface TournamentPairRowDto {
  pairId: string;
  deckId: string;
  botProfile: string;
  games: number;
  wins: number;
  losses: number;
  draws: number;
  winRate: number;
  avgSteps: number;
}

export interface TournamentMatchupResultDto {
  aPairId: string;
  bPairId: string;
  games: number;
  aWins: number;
  bWins: number;
  draws: number;
  aWinRate: number;
  avgSteps: number;
}

export interface TournamentAnalysisDto {
  totalMatchups: number;
  totalGames: number;
  avgGameLength: number;
  topPerformer?: string | null;
  topPerformerAdvantage?: number | null;
  weakestPerformer?: string | null;
  mostBalancedMatchup?: string | null;
  mostLopsidedMatchup?: string | null;
  longestGameMatchup?: string | null;
  shortestGameMatchup?: string | null;
  notes: string[];
}

export interface TournamentRunResponseV2 {
  config: TournamentConfigV2;
  pairs: TournamentPairRowDto[];
  matchups: TournamentMatchupResultDto[];
  analysis: TournamentAnalysisDto;
  timestamp: string;
  logLevel: TournamentLogLevel;
  /** Natural-language statements for LLM consumption; empty unless
   *  logLevel is "llm". */
  learningLog: string[];
  /** Relative path to the on-disk JSONL log, or null. */
  logPath?: string | null;
}

export interface TournamentValidateResponse {
  ok: boolean;
  errors: string[];
  warnings: string[];
}

export interface TournamentLogSummaryDto {
  id: string;
  path: string;
  timestamp: string;
  name?: string | null;
  pairCount: number;
  totalGames: number;
  topPerformer?: string | null;
  topPerformerAdvantage?: number | null;
}

export interface TournamentLogListResponse {
  logs: TournamentLogSummaryDto[];
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
