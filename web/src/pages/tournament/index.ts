import "./style.css";
import { api } from "../../api/client";
import type {
  BotProfileDto,
  PresetDeckDto,
  TournamentConfigV2,
  TournamentPairDto,
  TournamentMatchupDto,
  TournamentRunResponseV2,
  TournamentValidateResponse,
} from "../../api/dtos";

interface PageState {
  bots: BotProfileDto[];
  decks: PresetDeckDto[];
  editorEnabled: boolean;
  config: TournamentConfigV2;
  running: boolean;
  runError: string | null;
  result: TournamentRunResponseV2 | null;
  validateResult: TournamentValidateResponse | null;
  status: "idle" | "running" | "done" | "error";
}

const state: PageState = {
  bots: [],
  decks: [],
  editorEnabled: false,
  config: defaultConfig(),
  running: false,
  runError: null,
  result: null,
  validateResult: null,
  status: "idle",
};

function defaultConfig(): TournamentConfigV2 {
  return {
    version: 2,
    name: "Untitled tournament",
    description: "",
    pairs: [],
    matchups: null,
    includeMirror: true,
    gamesPerMatchup: 40,
    baseSeed: 1,
    maxInputsPerGame: 6000,
    maxEventsPerGame: 150000,
    logLevel: "summary",
  };
}

export async function renderTournament(container: HTMLElement): Promise<void> {
  container.innerHTML = `
    <main class="tournament-page">
      <h1>Tournament</h1>
      <p class="tournament-intro">
        Pit different deck/AI pairs against each other. Pairs can mix decks
        and bots freely; round-robin or explicit matchups decide who plays
        whom. Export the config as JSON (GUI or LLM can re-load it) and get
        a leaderboard + matchup matrix + analysis when it's done.
      </p>
      <div id="tourney-banner"></div>
      <section class="tourney-section">
        <h2>Tournament settings</h2>
        <div id="tourney-meta"></div>
      </section>
      <section class="tourney-section">
        <h2>Deck / AI pairs</h2>
        <div id="tourney-pairs"></div>
      </section>
      <section class="tourney-section">
        <h2>Run / export / import</h2>
        <div id="tourney-actions"></div>
        <div id="tourney-validate"></div>
      </section>
      <section class="tourney-section" id="tourney-result-section"></section>
    </main>
  `;

  await loadInitial();
  renderBanner();
  renderMetaSection();
  renderPairsSection();
  renderActionsSection();
  renderResultSection();
}

async function loadInitial(): Promise<void> {
  const [botsRes, decksRes, weightsRes] = await Promise.all([
    api.aiBots(),
    api.deckPresets(),
    api.aiWeights(),
  ]);
  if (botsRes.ok) state.bots = botsRes.body;
  if (decksRes.ok) state.decks = decksRes.body;
  if (weightsRes.ok) state.editorEnabled = weightsRes.body.editorEnabled;

  // Seed with two sensible default pairs so the page isn't empty on first load.
  if (state.config.pairs.length === 0 && state.decks.length > 0 && state.bots.length > 0) {
    const deckId = state.decks[0].id;
    const profileA = state.bots.find((b) => b.id === "fixed")?.id ?? state.bots[0].id;
    const profileB = state.bots.find((b) => b.id === "utility")?.id ?? state.bots[state.bots.length - 1].id;
    state.config.pairs = [
      { pairId: "A", deckId, botProfile: profileA, label: "Fixed-ladder baseline" },
      { pairId: "B", deckId, botProfile: profileB, label: "Utility bot" },
    ];
  }
}

function renderBanner(): void {
  const el = document.getElementById("tourney-banner");
  if (!el) return;
  if (state.editorEnabled) {
    el.innerHTML = "";
    return;
  }
  el.innerHTML = `
    <div class="tourney-banner tourney-banner-warning" role="status">
      <strong>Read-only mode.</strong>
      Running a tournament requires the REST host to be started with
      <code>CCGNF_AI_EDITOR=1</code>. Editing the config and exporting still work.
    </div>`;
}

function renderMetaSection(): void {
  const el = document.getElementById("tourney-meta");
  if (!el) return;
  el.innerHTML = `
    <div class="tourney-meta-grid">
      <label>Name
        <input id="tf-name" type="text" value="${attr(state.config.name ?? "")}">
      </label>
      <label>Games per matchup
        <input id="tf-games" type="number" min="1" max="200" step="1" value="${state.config.gamesPerMatchup}">
      </label>
      <label>Base seed
        <input id="tf-seed" type="number" step="1" value="${state.config.baseSeed}">
      </label>
      <label>Max inputs / game
        <input id="tf-maxinputs" type="number" min="100" step="100" value="${state.config.maxInputsPerGame}">
      </label>
      <label>Max events / game
        <input id="tf-maxevents" type="number" min="1000" step="1000" value="${state.config.maxEventsPerGame}">
      </label>
      <label>Log level
        <select id="tf-loglevel">
          <option value="silent"${(state.config.logLevel ?? "summary") === "silent" ? " selected" : ""}>silent — no analysis notes</option>
          <option value="summary"${(state.config.logLevel ?? "summary") === "summary" ? " selected" : ""}>summary — analysis block only</option>
          <option value="llm"${(state.config.logLevel ?? "summary") === "llm" ? " selected" : ""}>llm — learning log for LLM consumption (writes JSONL to logs/tournaments/)</option>
        </select>
      </label>
      <label class="inline">
        <input id="tf-mirror" type="checkbox" ${state.config.includeMirror ? "checked" : ""}>
        Include mirror matchups (A vs A) in round-robin
      </label>
      <label style="grid-column: 1 / -1">Description
        <textarea id="tf-desc">${esc(state.config.description ?? "")}</textarea>
      </label>
    </div>
  `;
  bindInput("tf-name", (v) => { state.config.name = v; });
  bindInputNumber("tf-games", (n) => { state.config.gamesPerMatchup = n; });
  bindInputNumber("tf-seed", (n) => { state.config.baseSeed = n; });
  bindInputNumber("tf-maxinputs", (n) => { state.config.maxInputsPerGame = n; });
  bindInputNumber("tf-maxevents", (n) => { state.config.maxEventsPerGame = n; });
  (document.getElementById("tf-mirror") as HTMLInputElement)?.addEventListener("change", (ev) => {
    state.config.includeMirror = (ev.target as HTMLInputElement).checked;
  });
  (document.getElementById("tf-loglevel") as HTMLSelectElement)?.addEventListener("change", (ev) => {
    const v = (ev.target as HTMLSelectElement).value as "silent" | "summary" | "llm";
    state.config.logLevel = v;
  });
  (document.getElementById("tf-desc") as HTMLTextAreaElement)?.addEventListener("input", (ev) => {
    state.config.description = (ev.target as HTMLTextAreaElement).value;
  });
}

function renderPairsSection(): void {
  const el = document.getElementById("tourney-pairs");
  if (!el) return;

  const deckOpts = state.decks
    .map((d) => `<option value="${attr(d.id)}">${esc(d.name)}${d.archetypes.length ? ` [${d.archetypes.join(", ")}]` : ""}</option>`)
    .join("");
  const botOpts = state.bots
    .map((b) => `<option value="${attr(b.id)}" title="${attr(b.description)}">${esc(b.id)}</option>`)
    .join("");

  const pairRows = state.config.pairs.map((p, i) => {
    const deckSelectedAttr = (id: string) => id === p.deckId ? " selected" : "";
    const botSelectedAttr = (id: string) => id === p.botProfile ? " selected" : "";
    const deckOptsSel = state.decks
      .map((d) => `<option value="${attr(d.id)}"${deckSelectedAttr(d.id)}>${esc(d.name)}</option>`)
      .join("");
    const botOptsSel = state.bots
      .map((b) => `<option value="${attr(b.id)}"${botSelectedAttr(b.id)}>${esc(b.id)}</option>`)
      .join("");
    return `
      <div class="tourney-pair-row" data-index="${i}">
        <input class="pair-id" type="text" value="${attr(p.pairId)}" placeholder="pairId">
        <select class="pair-deck">${deckOptsSel}</select>
        <select class="pair-bot">${botOptsSel}</select>
        <input class="pair-label" type="text" value="${attr(p.label ?? "")}" placeholder="label (optional)">
        <button class="pair-del" data-index="${i}" title="Remove pair">remove</button>
      </div>
    `;
  }).join("");

  el.innerHTML = `
    <div class="tourney-pairs">
      ${pairRows || `<p class="muted">No pairs yet. Add one below.</p>`}
    </div>
    <div class="tourney-pairs-actions">
      <button id="pair-add" class="tourney-btn">+ Add pair</button>
      <span class="muted">${state.config.pairs.length} pair(s). Round-robin produces ${matchupCount(state.config)} matchup(s) &times; ${state.config.gamesPerMatchup} game(s).</span>
    </div>
    <datalist id="tourney-decks-dl">${deckOpts}</datalist>
    <datalist id="tourney-bots-dl">${botOpts}</datalist>
  `;

  el.querySelectorAll<HTMLInputElement>(".pair-id").forEach((inp) => {
    inp.addEventListener("input", () => {
      const row = inp.closest<HTMLElement>(".tourney-pair-row");
      const idx = row ? parseInt(row.dataset.index ?? "-1", 10) : -1;
      if (idx >= 0) state.config.pairs[idx].pairId = inp.value;
    });
  });
  el.querySelectorAll<HTMLSelectElement>(".pair-deck").forEach((sel) => {
    sel.addEventListener("change", () => {
      const row = sel.closest<HTMLElement>(".tourney-pair-row");
      const idx = row ? parseInt(row.dataset.index ?? "-1", 10) : -1;
      if (idx >= 0) state.config.pairs[idx].deckId = sel.value;
    });
  });
  el.querySelectorAll<HTMLSelectElement>(".pair-bot").forEach((sel) => {
    sel.addEventListener("change", () => {
      const row = sel.closest<HTMLElement>(".tourney-pair-row");
      const idx = row ? parseInt(row.dataset.index ?? "-1", 10) : -1;
      if (idx >= 0) state.config.pairs[idx].botProfile = sel.value;
    });
  });
  el.querySelectorAll<HTMLInputElement>(".pair-label").forEach((inp) => {
    inp.addEventListener("input", () => {
      const row = inp.closest<HTMLElement>(".tourney-pair-row");
      const idx = row ? parseInt(row.dataset.index ?? "-1", 10) : -1;
      if (idx >= 0) state.config.pairs[idx].label = inp.value;
    });
  });
  el.querySelectorAll<HTMLButtonElement>(".pair-del").forEach((btn) => {
    btn.addEventListener("click", () => {
      const idx = parseInt(btn.dataset.index ?? "-1", 10);
      if (idx >= 0) {
        state.config.pairs.splice(idx, 1);
        renderPairsSection();
      }
    });
  });
  document.getElementById("pair-add")?.addEventListener("click", () => {
    const suggestedId = nextPairId(state.config.pairs);
    const deckId = state.decks[0]?.id ?? "";
    const botProfile = state.bots[0]?.id ?? "fixed";
    state.config.pairs.push({ pairId: suggestedId, deckId, botProfile, label: "" });
    renderPairsSection();
  });
}

function matchupCount(cfg: TournamentConfigV2): number {
  if (cfg.matchups && cfg.matchups.length > 0) return cfg.matchups.length;
  const n = cfg.pairs.length;
  if (n === 0) return 0;
  // round-robin: n*(n+1)/2 with mirror, n*(n-1)/2 without.
  return cfg.includeMirror ? (n * (n + 1)) / 2 : (n * (n - 1)) / 2;
}

function nextPairId(pairs: TournamentPairDto[]): string {
  const used = new Set(pairs.map((p) => p.pairId));
  for (let i = 0; i < 26; i++) {
    const id = String.fromCharCode(65 + i);
    if (!used.has(id)) return id;
  }
  for (let i = 1; i < 1000; i++) {
    const id = `P${i}`;
    if (!used.has(id)) return id;
  }
  return `P${Math.floor(Math.random() * 9999)}`;
}

function renderActionsSection(): void {
  const el = document.getElementById("tourney-actions");
  if (!el) return;
  const canRun = state.editorEnabled && !state.running && state.config.pairs.length >= 1;
  el.innerHTML = `
    <div class="tourney-actions">
      <button id="tf-run" class="tourney-btn" ${canRun ? "" : "disabled"}>Run tournament</button>
      <button id="tf-validate" class="tourney-btn tourney-btn-secondary">Validate</button>
      <button id="tf-export" class="tourney-btn tourney-btn-secondary">Export JSON</button>
      <button id="tf-import" class="tourney-btn tourney-btn-secondary">Import JSON</button>
      <input id="tf-import-file" type="file" accept=".json,application/json" style="display:none">
      <span class="tourney-status ${statusClass()}">${statusText()}</span>
    </div>
    <div class="tourney-validate-result">${renderValidateResult()}</div>
    <details style="margin-top: 10px">
      <summary class="muted">Config JSON (raw — edit freely; export writes this shape)</summary>
      <textarea id="tf-json" class="tourney-config-json" spellcheck="false">${esc(JSON.stringify(state.config, null, 2))}</textarea>
      <div class="muted">
        Invariant: every <code>pair.pairId</code> is unique; every <code>deckId</code> resolves in <code>/api/decks/presets</code>;
        every <code>botProfile</code> resolves in <code>/api/ai/bots</code>. An LLM can POST this exact shape to
        <code>/api/ai/tournament/run</code> to kick off a run.
      </div>
    </details>
  `;
  document.getElementById("tf-run")?.addEventListener("click", runTournament);
  document.getElementById("tf-validate")?.addEventListener("click", validateConfig);
  document.getElementById("tf-export")?.addEventListener("click", exportConfig);
  document.getElementById("tf-import")?.addEventListener("click", () =>
    (document.getElementById("tf-import-file") as HTMLInputElement)?.click());
  (document.getElementById("tf-import-file") as HTMLInputElement)?.addEventListener("change", importConfigFromFile);
  const jsonTa = document.getElementById("tf-json") as HTMLTextAreaElement | null;
  jsonTa?.addEventListener("blur", () => {
    try {
      const parsed = JSON.parse(jsonTa.value) as TournamentConfigV2;
      state.config = normalizeConfig(parsed);
      renderMetaSection();
      renderPairsSection();
      renderActionsSection();
    } catch (e) {
      state.runError = `Invalid JSON: ${(e as Error).message}`;
      state.status = "error";
      renderActionsSection();
    }
  });
}

function renderValidateResult(): string {
  if (!state.validateResult) return "";
  const v = state.validateResult;
  if (v.ok && v.warnings.length === 0) return `<span class="tourney-status-ok">Config OK.</span>`;
  const errList = v.errors.length > 0
    ? `<div class="tourney-status-err"><strong>Errors:</strong><ul>${v.errors.map((e) => `<li>${esc(e)}</li>`).join("")}</ul></div>`
    : "";
  const warnList = v.warnings.length > 0
    ? `<div class="tourney-status-running"><strong>Warnings:</strong><ul>${v.warnings.map((w) => `<li>${esc(w)}</li>`).join("")}</ul></div>`
    : "";
  return errList + warnList;
}

function statusClass(): string {
  switch (state.status) {
    case "running": return "tourney-status-running";
    case "done": return "tourney-status-ok";
    case "error": return "tourney-status-err";
    default: return "";
  }
}
function statusText(): string {
  switch (state.status) {
    case "running": return "Running…";
    case "done": return "Done.";
    case "error": return state.runError ?? "Error.";
    default: return "";
  }
}

function normalizeConfig(cfg: TournamentConfigV2): TournamentConfigV2 {
  const rawLevel = (cfg.logLevel ?? "summary").toString().toLowerCase();
  const logLevel: "silent" | "summary" | "llm" =
    rawLevel === "silent" || rawLevel === "llm" ? rawLevel : "summary";
  return {
    version: 2,
    name: cfg.name ?? "Untitled tournament",
    description: cfg.description ?? "",
    pairs: Array.isArray(cfg.pairs) ? cfg.pairs : [],
    matchups: Array.isArray(cfg.matchups) ? cfg.matchups : null,
    includeMirror: !!cfg.includeMirror,
    gamesPerMatchup: Number.isFinite(cfg.gamesPerMatchup) && cfg.gamesPerMatchup > 0 ? cfg.gamesPerMatchup : 4,
    baseSeed: Number.isFinite(cfg.baseSeed) ? cfg.baseSeed : 1,
    maxInputsPerGame: Number.isFinite(cfg.maxInputsPerGame) && cfg.maxInputsPerGame > 0 ? cfg.maxInputsPerGame : 2000,
    maxEventsPerGame: Number.isFinite(cfg.maxEventsPerGame) && cfg.maxEventsPerGame > 0 ? cfg.maxEventsPerGame : 50000,
    logLevel,
  };
}

async function validateConfig(): Promise<void> {
  const res = await api.aiTournamentValidate(state.config);
  if (res.ok) state.validateResult = res.body;
  else state.validateResult = { ok: false, errors: [`Server rejected config: ${JSON.stringify(res.body)}`], warnings: [] };
  renderActionsSection();
}

function exportConfig(): void {
  const filename = `${(state.config.name ?? "tournament").replace(/[^a-z0-9-_]+/gi, "_")}.tournament.json`;
  const blob = new Blob([JSON.stringify(state.config, null, 2)], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  setTimeout(() => {
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }, 0);
}

async function importConfigFromFile(ev: Event): Promise<void> {
  const input = ev.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  try {
    const text = await file.text();
    const parsed = JSON.parse(text) as TournamentConfigV2;
    state.config = normalizeConfig(parsed);
    state.validateResult = null;
    state.status = "idle";
    renderMetaSection();
    renderPairsSection();
    renderActionsSection();
    // Auto-validate imported files — noisy is fine; silence is not.
    await validateConfig();
  } catch (e) {
    state.runError = `Import failed: ${(e as Error).message}`;
    state.status = "error";
    renderActionsSection();
  } finally {
    input.value = "";
  }
}

async function runTournament(): Promise<void> {
  state.running = true;
  state.status = "running";
  state.runError = null;
  state.result = null;
  renderActionsSection();
  renderResultSection();
  const res = await api.aiTournamentRun(state.config);
  state.running = false;
  if (res.ok) {
    state.result = res.body;
    state.status = "done";
  } else {
    state.status = "error";
    state.runError = (res.body as unknown as { error?: string }).error
      ?? "Tournament failed. Is CCGNF_AI_EDITOR set?";
  }
  renderActionsSection();
  renderResultSection();
}

function renderResultSection(): void {
  const el = document.getElementById("tourney-result-section");
  if (!el) return;
  if (!state.result && state.status !== "running") {
    el.innerHTML = `<h2>Results</h2><p class="muted">Run a tournament to see the leaderboard, matchup matrix, and analysis.</p>`;
    return;
  }
  if (state.status === "running") {
    el.innerHTML = `<h2>Results</h2><p class="tourney-status-running">Running ${state.config.pairs.length} pair(s) &times; ${state.config.gamesPerMatchup} game(s)…</p>`;
    return;
  }
  const r = state.result!;
  el.innerHTML = `
    <h2>Results <span class="muted">— ${esc(r.timestamp)} · log level <code>${esc(r.logLevel)}</code>${r.logPath ? ` · <code>${esc(r.logPath)}</code>` : ""}</span></h2>
    ${renderAnalysisBlock(r)}
    ${renderLearningLog(r)}
    <h3 style="margin-top:18px">Leaderboard</h3>
    ${renderLeaderboard(r)}
    <h3 style="margin-top:18px">Matchup matrix</h3>
    ${renderMatrix(r)}
    <div class="tourney-actions" style="margin-top:12px">
      <button id="tf-result-export" class="tourney-btn tourney-btn-secondary">Download results JSON</button>
      ${r.logPath ? `<button id="tf-log-fetch" class="tourney-btn tourney-btn-secondary">Download JSONL log</button>` : ""}
    </div>
  `;
  document.getElementById("tf-log-fetch")?.addEventListener("click", async () => {
    if (!state.result?.logPath) return;
    // Parse the id from the path: logs/tournaments/<id>.jsonl
    const m = state.result.logPath.match(/([^/\\]+)\.jsonl$/);
    if (!m) return;
    const r = await api.aiTournamentLogFetch(m[1]);
    if (!r.ok) return;
    const blob = new Blob([r.body], { type: "application/x-ndjson" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `${m[1]}.jsonl`;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => { document.body.removeChild(a); URL.revokeObjectURL(url); }, 0);
  });
  document.getElementById("tf-result-export")?.addEventListener("click", () => {
    const filename = `${(state.config.name ?? "tournament").replace(/[^a-z0-9-_]+/gi, "_")}.results.json`;
    const blob = new Blob([JSON.stringify(state.result, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    setTimeout(() => {
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }, 0);
  });
}

function renderLearningLog(r: TournamentRunResponseV2): string {
  if (r.logLevel !== "llm" || r.learningLog.length === 0) return "";
  const lines = r.learningLog.map((l) => `<li>${esc(l)}</li>`).join("");
  return `
    <details class="tourney-learning-log" open>
      <summary>LLM learning log (${r.learningLog.length} statement${r.learningLog.length === 1 ? "" : "s"})</summary>
      <p class="muted">Natural-language observations intended for an LLM to read as training signal
      for "which bot/deck pairings beat which." A JSONL copy lives at
      <code>${esc(r.logPath ?? "")}</code> and can be fetched via
      <code>GET /api/ai/tournament/logs/&lt;id&gt;</code>.</p>
      <ol class="tourney-learning-list">${lines}</ol>
    </details>
  `;
}

function renderAnalysisBlock(r: TournamentRunResponseV2): string {
  const a = r.analysis;
  const advantageStr = a.topPerformerAdvantage !== null && a.topPerformerAdvantage !== undefined
    ? `${(a.topPerformerAdvantage * 100).toFixed(0)} pts over #2`
    : "—";
  return `
    <div class="tourney-analysis">
      <div class="tourney-analysis-card">
        <div class="label">Top performer</div>
        <div class="value">${esc(a.topPerformer ?? "—")}</div>
        <div class="muted">${esc(advantageStr)}</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Weakest performer</div>
        <div class="value">${esc(a.weakestPerformer ?? "—")}</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Most balanced matchup</div>
        <div class="value">${esc(a.mostBalancedMatchup ?? "—")}</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Most lopsided matchup</div>
        <div class="value">${esc(a.mostLopsidedMatchup ?? "—")}</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Avg game length</div>
        <div class="value">${a.avgGameLength.toFixed(0)} steps</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Games played</div>
        <div class="value">${a.totalGames} across ${a.totalMatchups} matchup(s)</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Longest matchup</div>
        <div class="value">${esc(a.longestGameMatchup ?? "—")}</div>
      </div>
      <div class="tourney-analysis-card">
        <div class="label">Shortest matchup</div>
        <div class="value">${esc(a.shortestGameMatchup ?? "—")}</div>
      </div>
    </div>
    ${a.notes.length > 0 ? `
      <div class="tourney-analysis-notes">
        <strong>Observations</strong>
        <ul>${a.notes.map((n) => `<li>${esc(n)}</li>`).join("")}</ul>
      </div>
    ` : ""}
  `;
}

function renderLeaderboard(r: TournamentRunResponseV2): string {
  const rows = r.pairs.map((row, i) => `
    <tr>
      <td>${i + 1}</td>
      <td><code>${esc(row.pairId)}</code></td>
      <td><code>${esc(row.deckId)}</code></td>
      <td><code>${esc(row.botProfile)}</code></td>
      <td>${row.games}</td>
      <td>${row.wins}</td>
      <td>${row.losses}</td>
      <td>${row.draws}</td>
      <td>${(row.winRate * 100).toFixed(1)}%</td>
      <td>${row.avgSteps.toFixed(0)}</td>
    </tr>
  `).join("");
  return `
    <table class="tourney-leaderboard">
      <thead><tr>
        <th>#</th><th>Pair</th><th>Deck</th><th>Bot</th>
        <th>Games</th><th>W</th><th>L</th><th>D</th>
        <th>Win rate</th><th>Avg steps</th>
      </tr></thead>
      <tbody>${rows}</tbody>
    </table>
  `;
}

function renderMatrix(r: TournamentRunResponseV2): string {
  // Unique axis in order pairs appeared in config.
  const pairIds = r.config.pairs.map((p) => p.pairId);
  const byAxis = new Map<string, Map<string, typeof r.matchups[number]>>();
  for (const m of r.matchups) {
    if (!byAxis.has(m.aPairId)) byAxis.set(m.aPairId, new Map());
    byAxis.get(m.aPairId)!.set(m.bPairId, m);
    if (!byAxis.has(m.bPairId)) byAxis.set(m.bPairId, new Map());
    // Also populate mirror view (bWinRate from A's POV).
    if (!byAxis.get(m.bPairId)!.has(m.aPairId)) {
      byAxis.get(m.bPairId)!.set(m.aPairId, {
        ...m,
        aPairId: m.bPairId,
        bPairId: m.aPairId,
        aWins: m.bWins,
        bWins: m.aWins,
        aWinRate: m.games === 0 ? 0 : m.bWins / m.games,
      });
    }
  }

  const header = `<tr><th></th>${pairIds.map((p) => `<th>${esc(p)}</th>`).join("")}</tr>`;
  const body = pairIds.map((aId) => {
    const cells = pairIds.map((bId) => {
      const m = byAxis.get(aId)?.get(bId);
      if (!m) return `<td class="matrix-cell muted">—</td>`;
      const wr = m.aWinRate;
      const cls = wr >= 0.7 ? "cell-domination"
                : wr <= 0.3 ? "cell-lopsided"
                : (Math.abs(wr - 0.5) <= 0.1) ? "cell-balanced"
                : "";
      return `<td class="matrix-cell ${cls}" title="${attr(`${aId} vs ${bId}: ${m.aWins}-${m.bWins}-${m.draws} (${m.avgSteps.toFixed(0)} steps)`)}">${(wr * 100).toFixed(0)}%</td>`;
    }).join("");
    return `<tr><td class="matrix-axis"><code>${esc(aId)}</code></td>${cells}</tr>`;
  }).join("");

  return `
    <table class="tourney-matrix">
      <thead>${header}</thead>
      <tbody>${body}</tbody>
    </table>
    <p class="muted">Cell values are the row pair's win rate versus the column pair. Green = dominant (≥70%), red = ceded (≤30%), gold = balanced (45–55%).</p>
  `;
}

// ─── Helpers ───────────────────────────────────────────────────────────

function bindInput(id: string, cb: (v: string) => void): void {
  const el = document.getElementById(id) as HTMLInputElement | null;
  el?.addEventListener("input", () => cb(el.value));
}
function bindInputNumber(id: string, cb: (n: number) => void): void {
  const el = document.getElementById(id) as HTMLInputElement | null;
  el?.addEventListener("input", () => {
    const n = parseFloat(el.value);
    if (!Number.isNaN(n)) cb(n);
  });
}

function esc(s: string): string {
  return String(s).replace(/[&<>"']/g, (c) =>
    c === "&" ? "&amp;"
      : c === "<" ? "&lt;"
      : c === ">" ? "&gt;"
      : c === '"' ? "&quot;"
      : "&#39;");
}

function attr(s: string): string {
  return esc(s);
}

// re-export unused imports to keep the type surface honest
export type { TournamentMatchupDto };
