import "./style.css";
import { api } from "../../api/client";
import type { RoomDetailDto, RoomEventFrame } from "../../api/dtos";
import type { RouteMatch } from "../../router";

interface Identity {
  roomId: string;
  playerId: number;
  token: string;
  name: string;
}

interface TabletopState {
  roomId: string | null;
  room: RoomDetailDto | null;
  gameState: unknown | null;
  events: RoomEventFrame[];
  identity: Identity | null;
  joining: boolean;
  error: string | null;
}

const state: TabletopState = {
  roomId: null,
  room: null,
  gameState: null,
  events: [],
  identity: null,
  joining: false,
  error: null,
};

let container: HTMLElement | null = null;
let sse: EventSource | null = null;

export async function renderTabletop(root: HTMLElement, match: RouteMatch): Promise<void> {
  container = root;
  const id = match.path.replace(/^\/play\/tabletop\//, "");
  if (!id) {
    root.innerHTML = `<div class="muted" style="padding:24px">Missing room id.</div>`;
    return;
  }
  if (state.roomId !== id) {
    closeStream();
    state.roomId = id;
    state.room = null;
    state.gameState = null;
    state.events = [];
    state.identity = loadIdentity(id);
    state.error = null;
  }
  await refresh();
  openStream(id);
}

async function refresh(): Promise<void> {
  if (!state.roomId) return;
  renderShell();
  try {
    const { ok, body } = await api.getRoom(state.roomId);
    if (!ok) throw new Error("GET /api/rooms/{id} failed");
    state.room = body;
    if (body.state !== "WaitingForPlayers") {
      const st = await api.roomState(state.roomId);
      if (st.ok) state.gameState = st.body;
    }
    state.error = null;
  } catch (err) {
    state.error = String(err);
  }
  renderShell();
}

function openStream(id: string): void {
  if (sse) return;
  try {
    sse = new EventSource(`/api/rooms/${encodeURIComponent(id)}/events`);
    sse.addEventListener("game-event", (ev) => {
      try {
        const frame = JSON.parse((ev as MessageEvent).data) as RoomEventFrame;
        state.events.push(frame);
        // Re-fetch state on any event so the snapshot stays current.
        void refresh();
      } catch (err) {
        console.warn("SSE parse error", err);
      }
    });
    sse.onerror = () => {
      // Close silently; reconnect could be added later.
      closeStream();
    };
  } catch (err) {
    state.error = `SSE connect failed: ${String(err)}`;
    renderShell();
  }
}

function closeStream(): void {
  if (sse) {
    sse.close();
    sse = null;
  }
}

async function join(): Promise<void> {
  if (!state.roomId) return;
  state.joining = true;
  renderShell();
  try {
    const name = window.prompt("Display name (optional):") ?? "";
    const { ok, body } = await api.joinRoom(state.roomId, name || null);
    if (!ok) throw new Error("POST /api/rooms/{id}/join failed");
    state.identity = {
      roomId: state.roomId,
      playerId: body.playerId,
      token: body.token,
      name: name || `Player${body.playerId}`,
    };
    saveIdentity(state.identity);
    await refresh();
  } catch (err) {
    state.error = String(err);
    renderShell();
  } finally {
    state.joining = false;
  }
}

async function submitAction(action: string): Promise<void> {
  if (!state.roomId || !state.identity) return;
  try {
    const { ok } = await api.submitAction(state.roomId, {
      playerId: state.identity.playerId,
      token: state.identity.token,
      action,
    });
    if (!ok) state.error = "Action rejected.";
  } catch (err) {
    state.error = String(err);
  }
  renderShell();
}

function renderShell(): void {
  if (!container) return;
  container.innerHTML = "";

  const page = document.createElement("div");
  page.className = "play-page";
  container.appendChild(page);

  const header = document.createElement("div");
  header.className = "tabletop-header";
  header.innerHTML = `
    <div>
      <div style="font-size:16px;font-weight:600">Room ${state.roomId ?? "?"}</div>
      <div class="muted" style="font-size:12px">
        ${state.room ? `${state.room.state} · ${state.room.occupied}/${state.room.playerSlots} players` : "loading…"}
      </div>
    </div>
    <a href="#/play/lobby" class="play-btn">← Lobby</a>
  `;
  page.appendChild(header);

  if (state.error) {
    const err = document.createElement("div");
    err.className = "status-err";
    err.style.padding = "6px 10px";
    err.style.marginBottom = "10px";
    err.textContent = `Error: ${state.error}`;
    page.appendChild(err);
  }

  const body = document.createElement("div");
  body.className = "tabletop";
  page.appendChild(body);

  const left = document.createElement("div");
  left.className = "tabletop-col";
  body.appendChild(left);

  const right = document.createElement("div");
  right.className = "tabletop-col right";
  body.appendChild(right);

  renderLeft(left);
  renderRight(right);
}

function renderLeft(col: HTMLElement): void {
  const identitySection = document.createElement("div");
  identitySection.className = "play-section";
  identitySection.innerHTML = `<h3>Seat</h3>`;
  if (state.identity) {
    const info = document.createElement("div");
    info.className = "my-player";
    info.textContent = `playerId=${state.identity.playerId}, name="${state.identity.name}"`;
    identitySection.appendChild(info);

    if (state.room?.state === "Active" || state.room?.state === "Finished") {
      const row = document.createElement("div");
      row.className = "play-row";
      row.style.marginTop = "6px";
      const passBtn = document.createElement("button");
      passBtn.className = "play-btn";
      passBtn.textContent = "Submit pass";
      passBtn.addEventListener("click", () => void submitAction("pass"));
      row.appendChild(passBtn);
      identitySection.appendChild(row);
    }
  } else {
    const row = document.createElement("div");
    row.className = "play-row";
    const joinBtn = document.createElement("button");
    joinBtn.className = "play-btn primary";
    joinBtn.textContent = state.joining ? "Joining…" : "Claim a seat";
    joinBtn.disabled =
      state.joining ||
      state.room?.state === "Finished" ||
      (state.room?.occupied ?? 0) >= (state.room?.playerSlots ?? 2);
    joinBtn.addEventListener("click", () => void join());
    row.appendChild(joinBtn);
    identitySection.appendChild(row);
  }
  col.appendChild(identitySection);

  const stateSection = document.createElement("div");
  stateSection.className = "play-section";
  stateSection.innerHTML = `<h3>Game state</h3>`;
  const pre = document.createElement("pre");
  pre.className = "tabletop-state-block";
  if (state.gameState === null) {
    pre.textContent = state.room?.state === "WaitingForPlayers"
      ? "(waiting for second player)"
      : "(no state yet)";
  } else {
    pre.textContent = summarizeGameState(state.gameState);
  }
  stateSection.appendChild(pre);
  col.appendChild(stateSection);
}

function renderRight(col: HTMLElement): void {
  const head = document.createElement("h3");
  head.style.fontSize = "11px";
  head.style.textTransform = "uppercase";
  head.style.letterSpacing = "0.06em";
  head.style.opacity = "0.7";
  head.style.margin = "0 0 8px 0";
  head.textContent = `Event log (${state.events.length})`;
  col.appendChild(head);

  if (state.events.length === 0) {
    const empty = document.createElement("div");
    empty.className = "event-log-empty";
    empty.textContent = "Events appear here as they stream in.";
    col.appendChild(empty);
    return;
  }

  const list = document.createElement("ul");
  list.className = "event-log";
  for (const frame of state.events.slice(-50).reverse()) {
    const li = document.createElement("li");
    li.className = "event-log-item";
    const step = document.createElement("span");
    step.className = "step";
    step.textContent = `#${frame.step}`;
    li.appendChild(step);
    const type = document.createElement("span");
    type.className = "type";
    type.textContent = frame.event.type;
    li.appendChild(type);
    const entries = Object.entries(frame.event.fields);
    if (entries.length > 0) {
      const fields = document.createElement("span");
      fields.className = "fields";
      fields.textContent = entries.map(([k, v]) => `${k}=${v}`).join(" · ");
      li.appendChild(fields);
    }
    list.appendChild(li);
  }
  col.appendChild(list);
}

function summarizeGameState(raw: unknown): string {
  try {
    const obj = raw as {
      stepCount?: number;
      gameOver?: boolean;
      playerIds?: number[];
      arenaIds?: number[];
      entities?: unknown[];
    };
    const lines: string[] = [];
    if (obj.stepCount !== undefined) lines.push(`stepCount: ${obj.stepCount}`);
    if (obj.gameOver !== undefined) lines.push(`gameOver: ${obj.gameOver}`);
    if (obj.playerIds) lines.push(`players: ${obj.playerIds.join(", ")}`);
    if (obj.arenaIds) lines.push(`arenas: ${obj.arenaIds.join(", ")}`);
    if (obj.entities) lines.push(`entities: ${obj.entities.length}`);
    lines.push("");
    lines.push("(full JSON):");
    lines.push(JSON.stringify(raw, null, 2));
    return lines.join("\n");
  } catch {
    return JSON.stringify(raw, null, 2);
  }
}

function identityKey(roomId: string): string {
  return `room-identity:${roomId}`;
}

function saveIdentity(id: Identity): void {
  window.sessionStorage.setItem(identityKey(id.roomId), JSON.stringify(id));
}

function loadIdentity(roomId: string): Identity | null {
  const raw = window.sessionStorage.getItem(identityKey(roomId));
  if (!raw) return null;
  try {
    return JSON.parse(raw) as Identity;
  } catch {
    return null;
  }
}
