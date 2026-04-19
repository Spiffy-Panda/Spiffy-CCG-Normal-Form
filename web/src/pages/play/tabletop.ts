import "./style.css";
import { api } from "../../api/client";
import type { CardDto, PresetDeckDto, RoomDetailDto, RoomEventFrame } from "../../api/dtos";
import type { RouteMatch } from "../../router";
import { renderBoard } from "../../shared/board";
import { closeInspector, fromEntity, openInspector } from "../../shared/card-inspector";
import { buildView, type EntityDto } from "../../shared/play-state";
import { listSavedDecks, loadDeck } from "../decks/persistence";

interface Identity {
  roomId: string;
  playerId: number;
  token: string;
  name: string;
  deckName: string | null;
}

interface PendingInputView {
  step: number;
  prompt: string;
  playerId: number | null;
  options: string[];
}

interface PhaseMarker {
  phase: string;         // "Rise" | "Channel" | "Clash" | "Fall" | "Pass"
  playerEntityId: number | null;
}

interface TabletopState {
  roomId: string | null;
  room: RoomDetailDto | null;
  gameState: unknown | null;
  events: RoomEventFrame[];
  identity: Identity | null;
  joining: boolean;
  error: string | null;
  presets: PresetDeckDto[];
  selectedDeckKey: string;  // "preset:<id>" | "saved:<name>" | ""
  cardCatalog: CardDto[];
  pendingInput: PendingInputView | null;
  currentPhase: PhaseMarker | null;
}

const state: TabletopState = {
  roomId: null,
  room: null,
  gameState: null,
  events: [],
  identity: null,
  joining: false,
  error: null,
  presets: [],
  selectedDeckKey: "",
  cardCatalog: [],
  pendingInput: null,
  currentPhase: null,
};

let container: HTMLElement | null = null;
let rightColEl: HTMLElement | null = null;
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
    closeInspector();
    state.roomId = id;
    state.room = null;
    state.gameState = null;
    state.events = [];
    state.identity = loadIdentity(id);
    state.error = null;
    state.pendingInput = null;
    state.currentPhase = null;
  }
  await refresh();
  openStream(id);
}

async function refresh(): Promise<void> {
  if (!state.roomId) return;
  renderShell();
  try {
    if (state.presets.length === 0) {
      const presetsResult = await api.deckPresets();
      if (presetsResult.ok) state.presets = presetsResult.body;
    }
    if (state.cardCatalog.length === 0) {
      const cardsResult = await api.cards();
      if (cardsResult.ok) state.cardCatalog = cardsResult.body;
    }
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
        updatePendingFromFrame(frame);
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

function updatePendingFromFrame(frame: RoomEventFrame): void {
  const type = frame.event.type;
  if (type === "GameEvent" && frame.event.fields["eventType"] === "PhaseBegin") {
    const phase = frame.event.fields["field.phase"] ?? "";
    const playerField = frame.event.fields["field.player"] ?? "";
    const m = /#(\d+)/.exec(playerField);
    const playerEntityId = m ? parseInt(m[1], 10) : null;
    if (phase) state.currentPhase = { phase, playerEntityId };
  }
  if (type === "InputPending") {
    const f = frame.event.fields;
    const pid = f.playerId ? parseInt(f.playerId, 10) : NaN;
    const opts = (f.options ?? "").split(",").filter((s) => s.length > 0);
    state.pendingInput = {
      step: frame.step,
      prompt: f.prompt ?? "",
      playerId: Number.isFinite(pid) ? pid : null,
      options: opts,
    };
  } else if (
    type === "CpuAction" ||
    type === "ActionAccepted" ||
    type === "RoomFinished" ||
    type === "RoomCancelled" ||
    type === "InterpreterError"
  ) {
    // Any of these resolves the previous pending either way.
    state.pendingInput = null;
  }
}

async function join(): Promise<void> {
  if (!state.roomId) return;
  state.joining = true;
  renderShell();
  try {
    // window.prompt is blocked in some embedded browsers (Claude Preview,
    // sandboxed iframes) — fall back to the empty string so the server
    // assigns a default name. Users can later rename themselves once an
    // inline name field lands.
    let name = "";
    try {
      name = window.prompt("Display name (optional):") ?? "";
    } catch {
      name = "";
    }
    const deck = resolveSelectedDeck();
    const { ok, body } = await api.joinRoom(state.roomId, name || null, deck);
    if (!ok) throw new Error("POST /api/rooms/{id}/join failed");
    state.identity = {
      roomId: state.roomId,
      playerId: body.playerId,
      token: body.token,
      name: name || `Player${body.playerId}`,
      deckName: deckNameForKey(state.selectedDeckKey),
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

function resolveSelectedDeck(): { preset?: string; cards?: { name: string; count: number }[] } | null {
  const key = state.selectedDeckKey;
  if (!key) return null;
  if (key.startsWith("preset:")) {
    return { preset: key.slice(7) };
  }
  if (key.startsWith("saved:")) {
    const saved = loadDeck("constructed", key.slice(6));
    if (!saved) return null;
    return { cards: saved.cards };
  }
  return null;
}

function deckNameForKey(key: string): string | null {
  if (!key) return null;
  if (key.startsWith("preset:")) {
    return state.presets.find((p) => p.id === key.slice(7))?.name ?? null;
  }
  if (key.startsWith("saved:")) return key.slice(6);
  return null;
}

async function exportState(): Promise<void> {
  if (!state.roomId) return;
  try {
    const { ok, body } = await api.exportRoom(state.roomId);
    if (!ok) {
      state.error = "Export failed.";
      renderShell();
      return;
    }
    const json = JSON.stringify(body, null, 2);
    const blob = new Blob([json], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `room-${state.roomId}-step-${body.stepCount}.json`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  } catch (err) {
    state.error = String(err);
    renderShell();
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
    <div class="tabletop-header-right">
      ${renderTurnChip()}
      <a href="#/play/lobby" class="play-btn">← Lobby</a>
    </div>
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
  left.className = "tabletop-col board-col";
  body.appendChild(left);

  const right = document.createElement("div");
  right.className = "tabletop-col right";
  body.appendChild(right);

  renderLeft(left);
  renderRight(right);
}

function renderLeft(col: HTMLElement): void {
  const topBar = document.createElement("div");
  topBar.className = "play-topbar";

  const seatInfo = document.createElement("div");
  seatInfo.className = "play-topbar-seat";
  if (state.identity) {
    const deckLabel = state.identity.deckName
      ? ` · deck <strong>${escapeHtml(state.identity.deckName)}</strong>`
      : ` · <span class="muted">no deck</span>`;
    seatInfo.innerHTML =
      `Seat: <strong>${escapeHtml(state.identity.name)}</strong> ` +
      `<span class="muted">(playerId ${state.identity.playerId})</span>` +
      deckLabel;
  } else {
    seatInfo.innerHTML = `<span class="muted">Not seated — pick a deck, then claim a seat.</span>`;
  }
  topBar.appendChild(seatInfo);

  const actions = document.createElement("div");
  actions.className = "play-topbar-actions";
  if (state.identity) {
    if (state.room?.state === "Active" || state.room?.state === "Finished") {
      const passBtn = document.createElement("button");
      passBtn.className = "play-btn";
      passBtn.textContent = "Submit pass";
      passBtn.addEventListener("click", () => void submitAction("pass"));
      actions.appendChild(passBtn);

      const exportBtn = document.createElement("button");
      exportBtn.className = "play-btn";
      exportBtn.textContent = "Export";
      exportBtn.title = "Download the current game state as JSON";
      exportBtn.addEventListener("click", () => void exportState());
      actions.appendChild(exportBtn);
    }
  } else {
    actions.appendChild(renderDeckPicker());
    const joinBtn = document.createElement("button");
    joinBtn.className = "play-btn primary";
    joinBtn.textContent = state.joining ? "Joining…" : "Claim a seat";
    joinBtn.disabled =
      state.joining ||
      state.room?.state === "Finished" ||
      (state.room?.occupied ?? 0) >= (state.room?.playerSlots ?? 2);
    joinBtn.addEventListener("click", () => void join());
    actions.appendChild(joinBtn);
  }
  topBar.appendChild(actions);
  col.appendChild(topBar);

  if (state.room && state.room.players.length > 0) {
    const roster = document.createElement("div");
    roster.className = "play-roster muted";
    roster.innerHTML = state.room.players.map((p) => {
      const deck = p.deckName
        ? `<em>${escapeHtml(p.deckName)}</em>`
        : `<span class="roster-no-deck">no deck</span>`;
      const badge = p.seatKind === "Cpu" ? "🤖 " : "";
      return `<span>P${p.playerId} ${badge}${escapeHtml(p.name)} · ${deck}</span>`;
    }).join("  •  ");
    col.appendChild(roster);
  }

  const boardWrap = document.createElement("div");
  boardWrap.className = "play-board-wrap";

  if (state.gameState === null) {
    const pending = document.createElement("div");
    pending.className = "play-board-empty muted";
    pending.textContent = state.room?.state === "WaitingForPlayers"
      ? "Waiting for a second player to join…"
      : "No game state yet.";
    boardWrap.appendChild(pending);
  } else {
    const view = buildView(state.gameState);
    if (!view) {
      const fallback = document.createElement("div");
      fallback.className = "play-board-empty muted";
      fallback.textContent = "Could not parse game state.";
      boardWrap.appendChild(fallback);
    } else {
      const banner = renderEngineBanner(view.round, state.room?.state ?? null);
      if (banner) boardWrap.appendChild(banner);
      const actionBar = renderActionBar();
      if (actionBar) boardWrap.appendChild(actionBar);
      boardWrap.appendChild(renderBoard(view, {
        viewerPlayerId: state.identity?.playerId ?? null,
        onCardClick: onCardClicked,
      }));
    }
  }
  col.appendChild(boardWrap);
}

function renderDeckPicker(): HTMLElement {
  const select = document.createElement("select");
  select.className = "play-select";
  select.innerHTML = `<option value="">Pick a deck…</option>`;
  const presets = state.presets.filter((p) => p.format === "constructed");
  if (presets.length > 0) {
    const group = document.createElement("optgroup");
    group.label = "Presets";
    for (const p of presets) {
      const opt = document.createElement("option");
      opt.value = `preset:${p.id}`;
      opt.textContent = p.name;
      if (state.selectedDeckKey === opt.value) opt.selected = true;
      group.appendChild(opt);
    }
    select.appendChild(group);
  }
  const saved = listSavedDecks("constructed");
  if (saved.length > 0) {
    const group = document.createElement("optgroup");
    group.label = "My saved decks";
    for (const name of saved) {
      const opt = document.createElement("option");
      opt.value = `saved:${name}`;
      opt.textContent = name;
      if (state.selectedDeckKey === opt.value) opt.selected = true;
      group.appendChild(opt);
    }
    select.appendChild(group);
  }
  select.addEventListener("change", () => {
    state.selectedDeckKey = select.value;
    renderShell();
  });
  return select;
}

function renderTurnChip(): string {
  const phase = state.currentPhase;
  if (!phase) return "";
  const view = state.gameState ? buildView(state.gameState) : null;
  const idx = view && phase.playerEntityId !== null
    ? view.players.findIndex((p) => p.id === phase.playerEntityId)
    : -1;
  const seat = idx >= 0 ? state.room?.players[idx] : undefined;
  const viewerId = state.identity?.playerId ?? null;
  const yours = seat !== undefined && viewerId !== null && seat.playerId === viewerId;
  const who = seat ? (yours ? "Your turn" : `${escapeHtml(seat.name)}'s turn`) : "Turn";
  const tone = yours ? "tabletop-chip tabletop-chip-yours" : "tabletop-chip";
  return `<span class="${tone}">${who} · ${escapeHtml(phase.phase)}</span>`;
}

function onCardClicked(entity: EntityDto): void {
  openInspector(fromEntity(entity, state.cardCatalog), rightColEl ?? undefined);
}

function renderActionBar(): HTMLElement | null {
  const pending = state.pendingInput;
  if (!pending || pending.options.length === 0) return null;

  const viewerId = state.identity?.playerId ?? null;
  const viewerSeat = viewerId && state.room?.players.find((p) => p.playerId === viewerId);
  // Map the interpreter's entity id (pending.playerId) back to the roster
  // seat via positional order. State already has view.players in order,
  // but we approximate using the roster and trust chooser seating.
  const isForViewer =
    viewerSeat !== undefined && viewerSeat !== null
      ? (() => {
          const view = state.gameState ? buildView(state.gameState) : null;
          if (!view) return false;
          const idx = view.players.findIndex((p) => p.id === pending.playerId);
          return idx >= 0 && state.room?.players[idx]?.playerId === viewerId;
        })()
      : false;

  const bar = document.createElement("div");
  bar.className = "play-action-bar";

  const label = document.createElement("span");
  label.className = "play-action-bar-label";
  label.textContent = isForViewer ? "Your choice:" : "Waiting on opponent:";
  bar.appendChild(label);

  for (const opt of pending.options) {
    const btn = document.createElement("button");
    btn.className = "play-action-btn";
    btn.textContent = opt;
    btn.disabled = !isForViewer;
    btn.addEventListener("click", () => void submitAction(opt));
    bar.appendChild(btn);
  }

  const note = document.createElement("span");
  note.className = "play-action-note";
  note.textContent = pending.prompt;
  bar.appendChild(note);

  return bar;
}

function renderEngineBanner(round: number | null, _roomState: string | null): HTMLElement | null {
  // Since 8a+8c+8d the engine walks all five phases, plays cards out of
  // hand, and resolves single-target effects. The banner just shows round
  // + a pointer to what's still being built (Clash, SBAs, victory).
  const banner = document.createElement("div");
  banner.className = "play-banner muted";
  banner.innerHTML =
    `Engine state — round ${round ?? "?"}. ` +
    `Main-phase card play and single-target effects are live; Clash damage and Conduit-collapse victory land in 8e–8g.`;
  return banner;
}

function renderRight(col: HTMLElement): void {
  // Hold onto the column element so openInspector can mount into it —
  // the inspector stacks above the event-log wrapper and both flex to
  // share the column's height.
  rightColEl = col;

  // Re-attach the existing singleton inspector panel (if any) so it
  // survives tabletop re-renders. Must come before the log wrap so the
  // flex column orders inspector above log.
  const existingInspector = document.querySelector("aside.card-inspector");
  if (existingInspector) col.appendChild(existingInspector);

  const logWrap = document.createElement("div");
  logWrap.className = "event-log-wrap";

  const head = document.createElement("h3");
  head.style.fontSize = "11px";
  head.style.textTransform = "uppercase";
  head.style.letterSpacing = "0.06em";
  head.style.opacity = "0.7";
  head.style.margin = "0 0 8px 0";
  head.textContent = `Event log (${state.events.length})`;
  logWrap.appendChild(head);

  if (state.events.length === 0) {
    const empty = document.createElement("div");
    empty.className = "event-log-empty";
    empty.textContent = "Events appear here as they stream in.";
    logWrap.appendChild(empty);
  } else {
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
    logWrap.appendChild(list);
  }
  col.appendChild(logWrap);
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) => {
    switch (c) {
      case "&": return "&amp;";
      case "<": return "&lt;";
      case ">": return "&gt;";
      case '"': return "&quot;";
      default: return "&#39;";
    }
  });
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
