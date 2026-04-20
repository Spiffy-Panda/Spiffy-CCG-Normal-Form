import "./style.css";
import { api } from "../../api/client";
import type { BotProfileDto } from "../../api/dtos";

interface PageState {
  bots: BotProfileDto[];
  weightsJson: string;
  weightsSource: "file" | "default" | "unknown";
  weightsPath: string | null;
  considerationKeys: string[];
  saveStatus: "idle" | "saving" | "saved" | "error";
  saveError: string | null;
  /** Mirrors CCGNF_AI_EDITOR on the server. False → Save button 404. */
  editorEnabled: boolean;
}

const state: PageState = {
  bots: [],
  weightsJson: "",
  weightsSource: "unknown",
  weightsPath: null,
  considerationKeys: [],
  saveStatus: "idle",
  saveError: null,
  editorEnabled: false,
};

export async function renderAi(container: HTMLElement): Promise<void> {
  container.innerHTML = `
    <main class="ai-page">
      <h1>AI — weights</h1>
      <p class="ai-intro">
        Inspect the utility-bot weight table and (with <code>CCGNF_AI_EDITOR=1</code>)
        save edits back to <code>encoding/ai/utility-weights.json</code>. Tournament
        setup has moved to its own tab — see <a href="#/tournament">Tournament</a>.
      </p>
      <div id="ai-banner"></div>
      <section id="ai-bots" class="ai-section"></section>
      <section id="ai-weights" class="ai-section"></section>
    </main>
  `;

  await loadInitial();
  renderBanner();
  renderBotsSection();
  renderWeightsSection();
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
      Saving weights is disabled because the server was started without
      <code>CCGNF_AI_EDITOR=1</code>. Restart the REST host with that
      environment variable set to enable the full editor.
    </div>
  `;
}

async function loadInitial(): Promise<void> {
  const [botsRes, weightsRes] = await Promise.all([
    api.aiBots(),
    api.aiWeights(),
  ]);

  if (botsRes.ok) {
    state.bots = botsRes.body;
  }
  if (weightsRes.ok) {
    state.weightsJson = weightsRes.body.json || defaultWeightsSkeleton(weightsRes.body.considerationKeys);
    state.weightsSource = weightsRes.body.source;
    state.weightsPath = weightsRes.body.path ?? null;
    state.considerationKeys = weightsRes.body.considerationKeys;
    state.editorEnabled = weightsRes.body.editorEnabled;
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

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, (c) =>
    c === "&" ? "&amp;"
      : c === "<" ? "&lt;"
      : c === ">" ? "&gt;"
      : c === '"' ? "&quot;"
      : "&#39;");
}
