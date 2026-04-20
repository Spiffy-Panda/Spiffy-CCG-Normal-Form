import "./style.css";
import { api } from "../../api/client";
import type { AiTournamentResponse, BotProfileDto, PresetDeckDto } from "../../api/dtos";

interface PageState {
  bots: BotProfileDto[];
  weightsJson: string;
  weightsSource: "file" | "default" | "unknown";
  weightsPath: string | null;
  considerationKeys: string[];
  decks: PresetDeckDto[];
  saveStatus: "idle" | "saving" | "saved" | "error";
  saveError: string | null;
  /** Mirrors CCGNF_AI_EDITOR on the server. False → Save + Run buttons 404. */
  editorEnabled: boolean;
  tournament: AiTournamentResponse | null;
  tournamentRunning: boolean;
  tournamentError: string | null;
  selectedDeckId: string | null;
  tournamentGames: number;
}

const state: PageState = {
  bots: [],
  weightsJson: "",
  weightsSource: "unknown",
  weightsPath: null,
  considerationKeys: [],
  decks: [],
  saveStatus: "idle",
  saveError: null,
  editorEnabled: false,
  tournament: null,
  tournamentRunning: false,
  tournamentError: null,
  selectedDeckId: null,
  tournamentGames: 4,
};

export async function renderAi(container: HTMLElement): Promise<void> {
  container.innerHTML = `
    <main class="ai-page">
      <h1>AI — weights & tournaments</h1>
      <p class="ai-intro">
        Inspect the utility-bot weight table, run a mirror tournament
        for a preset deck, and (with <code>CCGNF_AI_EDITOR=1</code>) save
        edited weights back to <code>encoding/ai/utility-weights.json</code>.
      </p>
      <div id="ai-banner"></div>
      <section id="ai-bots" class="ai-section"></section>
      <section id="ai-weights" class="ai-section"></section>
      <section id="ai-tournament" class="ai-section"></section>
    </main>
  `;

  await loadInitial();
  renderBanner();
  renderBotsSection();
  renderWeightsSection();
  renderTournamentSection();
}

function renderBanner(): void {
  const el = document.getElementById("ai-banner");
  if (!el) return;
  if (state.editorEnabled) {
    el.innerHTML = "";
    return;
  }
  el.innerHTML = `
    <div class="ai-banner ai-banner-warning" role="status">
      <strong>Read-only mode.</strong>
      Saving weights and running tournaments are disabled because the
      server was started without <code>CCGNF_AI_EDITOR=1</code>. Restart
      the REST host with that environment variable set to enable the
      full editor.
    </div>
  `;
}

async function loadInitial(): Promise<void> {
  const [botsRes, weightsRes, decksRes] = await Promise.all([
    api.aiBots(),
    api.aiWeights(),
    api.deckPresets(),
  ]);

  if (botsRes.ok) state.bots = botsRes.body;
  if (weightsRes.ok) {
    state.weightsJson = weightsRes.body.json || defaultWeightsSkeleton(weightsRes.body.considerationKeys);
    state.weightsSource = weightsRes.body.source;
    state.weightsPath = weightsRes.body.path ?? null;
    state.considerationKeys = weightsRes.body.considerationKeys;
    state.editorEnabled = weightsRes.body.editorEnabled;
  }
  if (decksRes.ok) {
    state.decks = decksRes.body;
    state.selectedDeckId = decksRes.body[0]?.id ?? null;
  }
}

function defaultWeightsSkeleton(keys: string[]): string {
  const row = keys.reduce<Record<string, number>>((acc, k) => {
    acc[k] = 1.0;
    return acc;
  }, {});
  return JSON.stringify(
    {
      version: 1,
      intents: {
        default: row,
        early_tempo: row,
        pushing: row,
        defend_conduit: row,
        lethal_check: row,
      },
    },
    null,
    2,
  );
}

function renderBotsSection(): void {
  const el = document.getElementById("ai-bots");
  if (!el) return;
  el.innerHTML = `
    <h2>Available bots</h2>
    <ul class="ai-bot-list">
      ${state.bots.map((b) => `
        <li class="ai-bot-row">
          <code>${escapeHtml(b.id)}</code>
          <span class="ai-bot-name">${escapeHtml(b.name)}</span>
          <span class="ai-bot-desc">${escapeHtml(b.description)}</span>
        </li>
      `).join("")}
    </ul>
  `;
}

function renderWeightsSection(): void {
  const el = document.getElementById("ai-weights");
  if (!el) return;
  el.innerHTML = `
    <h2>Weight table
      <span class="ai-source ai-source-${state.weightsSource}">${state.weightsSource}${state.weightsPath ? ` · ${escapeHtml(state.weightsPath)}` : ""}</span>
    </h2>
    <p class="muted">
      Considerations (${state.considerationKeys.length}):
      ${state.considerationKeys.map((k) => `<code>${escapeHtml(k)}</code>`).join(" · ")}
    </p>
    <textarea id="ai-weights-json" spellcheck="false" rows="24">${escapeHtml(state.weightsJson)}</textarea>
    <div class="ai-weights-actions">
      <button id="ai-weights-save" class="ai-btn" ${state.editorEnabled ? "" : "disabled title=\"Set CCGNF_AI_EDITOR=1 to enable saving.\""}>Save</button>
      <span id="ai-weights-status" class="ai-status">${renderSaveStatus()}</span>
    </div>
  `;
  const textarea = document.getElementById("ai-weights-json") as HTMLTextAreaElement;
  textarea?.addEventListener("input", () => {
    state.weightsJson = textarea.value;
  });
  document.getElementById("ai-weights-save")?.addEventListener("click", saveWeights);
}

async function saveWeights(): Promise<void> {
  state.saveStatus = "saving";
  state.saveError = null;
  renderWeightsSection();
  const res = await api.aiPutWeights(state.weightsJson);
  if (res.ok) {
    state.saveStatus = "saved";
  } else {
    state.saveStatus = "error";
    state.saveError = (res.body as unknown as { error?: string }).error || "write failed (editor disabled?)";
  }
  renderWeightsSection();
}

function renderSaveStatus(): string {
  switch (state.saveStatus) {
    case "idle": return "";
    case "saving": return "<span class=\"ai-saving\">Saving…</span>";
    case "saved": return "<span class=\"ai-saved\">Saved</span>";
    case "error": return `<span class="ai-error">${escapeHtml(state.saveError ?? "")}</span>`;
  }
}

function renderTournamentSection(): void {
  const el = document.getElementById("ai-tournament");
  if (!el) return;
  const deckOptions = state.decks
    .map((d) => `<option value="${escapeHtml(d.id)}"${d.id === state.selectedDeckId ? " selected" : ""}>${escapeHtml(d.name)}${d.archetypes.length ? ` [${d.archetypes.join(", ")}]` : ""}</option>`)
    .join("");
  el.innerHTML = `
    <h2>Suggest an AI (mirror tournament)</h2>
    <p class="muted">
      Plays the selected deck against itself with every available bot, N
      games per bot. Top win-rate wins. Requires <code>CCGNF_AI_EDITOR=1</code>.
    </p>
    <div class="ai-tournament-form">
      <label>Deck
        <select id="ai-tournament-deck">${deckOptions}</select>
      </label>
      <label>Games
        <input id="ai-tournament-games" type="number" min="2" max="40" step="2" value="${state.tournamentGames}">
      </label>
      <button id="ai-tournament-run" class="ai-btn" ${state.tournamentRunning || !state.editorEnabled ? "disabled" : ""}${state.editorEnabled ? "" : " title=\"Set CCGNF_AI_EDITOR=1 to enable.\""}>Run</button>
    </div>
    <div id="ai-tournament-result">${renderTournamentResult()}</div>
  `;
  document.getElementById("ai-tournament-deck")?.addEventListener("change", (ev) => {
    state.selectedDeckId = (ev.target as HTMLSelectElement).value;
  });
  document.getElementById("ai-tournament-games")?.addEventListener("change", (ev) => {
    const n = parseInt((ev.target as HTMLInputElement).value, 10);
    if (!isNaN(n) && n > 0) state.tournamentGames = n;
  });
  document.getElementById("ai-tournament-run")?.addEventListener("click", runTournament);
}

function renderTournamentResult(): string {
  if (state.tournamentRunning) return "<p class=\"muted\">Running…</p>";
  if (state.tournamentError) return `<p class="ai-error">${escapeHtml(state.tournamentError)}</p>`;
  if (!state.tournament) return "";
  const rows = state.tournament.rows
    .map((r, i) => `
      <tr>
        <td>${i + 1}</td>
        <td><code>${escapeHtml(r.botName)}</code></td>
        <td>${r.games}</td>
        <td>${r.wins}</td>
        <td>${r.losses}</td>
        <td>${r.draws}</td>
        <td>${(r.winRate * 100).toFixed(1)}%</td>
        <td>${r.avgSteps.toFixed(0)}</td>
      </tr>`).join("");
  return `
    <p class="muted">Result for deck <code>${escapeHtml(state.tournament.deckId)}</code> at ${escapeHtml(state.tournament.timestamp)}</p>
    <table class="ai-tournament-table">
      <thead><tr><th>#</th><th>Bot</th><th>Games</th><th>W</th><th>L</th><th>D</th><th>Win rate</th><th>Avg steps</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>
  `;
}

async function runTournament(): Promise<void> {
  if (!state.selectedDeckId) return;
  state.tournamentRunning = true;
  state.tournamentError = null;
  state.tournament = null;
  renderTournamentSection();
  const res = await api.aiTournament(state.selectedDeckId, state.tournamentGames, 1);
  state.tournamentRunning = false;
  if (res.ok) {
    state.tournament = res.body;
  } else {
    state.tournamentError = (res.body as unknown as { error?: string }).error
      ?? "Tournament failed (CCGNF_AI_EDITOR must be set).";
  }
  renderTournamentSection();
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) =>
    c === "&" ? "&amp;"
      : c === "<" ? "&lt;"
      : c === ">" ? "&gt;"
      : c === '"' ? "&quot;"
      : "&#39;");
}
