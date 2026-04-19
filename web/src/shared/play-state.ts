// Typed view over the raw /api/rooms/{id}/state payload. The backend
// currently emits a GameState shape driven by the interpreter; this
// file is a thin projection that keeps the UI types near the board
// component.
//
// NOTE: the interpreter runs only Setup → Round-1 Rise today, so unit
// play, combat, and later phases are not populated. The helpers below
// return empty lists / placeholders where the engine has not yet
// emitted data — the board renders what's there and degrades to empty
// zones otherwise.

export interface Zone {
  capacity: number | null;
  contents: number[];
  order: "Sequential" | "FIFO" | "Unordered" | string;
}

export interface EntityDto {
  id: number;
  kind: "Game" | "Arena" | "Player" | "Card" | "Conduit" | string;
  displayName: string;
  ownerId: number | null;
  parameters: Record<string, string>;
  characteristics: Record<string, string>;
  counters: Record<string, number>;
  zones: Record<string, Zone>;
  abilityCount: number;
}

export interface GameStateDto {
  stepCount: number;
  gameOver: boolean;
  gameId?: number;
  playerIds: number[];
  arenaIds: number[];
  entities: EntityDto[];
  pending?: unknown[];
}

export interface PlayView {
  game: EntityDto | null;
  arenas: EntityDto[];
  players: EntityDto[];
  cardsById: Map<number, EntityDto>;
  conduitsByOwnerArena: Map<string, EntityDto>;
  activePlayerId: number | null;
  round: number | null;
}

const ARENA_ORDER: Record<string, number> = { Left: 0, Center: 1, Right: 2 };

export function buildView(raw: unknown): PlayView | null {
  if (!raw || typeof raw !== "object") return null;
  const st = raw as GameStateDto;
  if (!Array.isArray(st.entities)) return null;

  let game: EntityDto | null = null;
  const arenas: EntityDto[] = [];
  const players: EntityDto[] = [];
  const cardsById = new Map<number, EntityDto>();
  const conduitsByOwnerArena = new Map<string, EntityDto>();

  for (const e of st.entities) {
    switch (e.kind) {
      case "Game": game = e; break;
      case "Arena": arenas.push(e); break;
      case "Player": players.push(e); break;
      case "Card": cardsById.set(e.id, e); break;
      case "Conduit": {
        const arena = e.parameters?.["arena"];
        if (arena) conduitsByOwnerArena.set(`${e.ownerId}:${arena}`, e);
        break;
      }
    }
  }

  arenas.sort((a, b) => {
    const ap = a.parameters?.["pos"] ?? "";
    const bp = b.parameters?.["pos"] ?? "";
    return (ARENA_ORDER[ap] ?? 99) - (ARENA_ORDER[bp] ?? 99);
  });
  players.sort((a, b) => a.id - b.id);

  // first_player is a Game characteristic like "#5" — parse the id out.
  const firstPlayerRaw = game?.characteristics?.["first_player"] ?? "";
  const activePlayerId = firstPlayerRaw.startsWith("#")
    ? parseInt(firstPlayerRaw.slice(1), 10) || null
    : null;
  const round = parseInt(game?.characteristics?.["round"] ?? "", 10) || null;

  return { game, arenas, players, cardsById, conduitsByOwnerArena, activePlayerId, round };
}

export function arenaPos(a: EntityDto): string {
  return a.parameters?.["pos"] ?? a.displayName;
}

export function playerAether(p: EntityDto): number {
  return p.counters?.["aether"] ?? 0;
}

export function playerIntegrity(view: PlayView, playerId: number, arenaPosName: string): number | null {
  const c = view.conduitsByOwnerArena.get(`${playerId}:${arenaPosName}`);
  return c?.counters?.["integrity"] ?? null;
}

export function zoneContents(player: EntityDto, zoneName: string): number[] {
  return player.zones?.[zoneName]?.contents ?? [];
}
