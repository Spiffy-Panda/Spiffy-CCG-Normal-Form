import "./style.css";
import { api } from "../../api/client";
import type { ProjectDto } from "../../api/dtos";
import type { RouteMatch } from "../../router";
import { highlightCcgnf } from "./highlight";

interface PageState {
  project: ProjectDto | null;
  selected: string | null;
  loading: boolean;
  error: string | null;
  rawCache: Map<string, string>;
}

const state: PageState = {
  project: null,
  selected: null,
  loading: false,
  error: null,
  rawCache: new Map(),
};

let container: HTMLElement | null = null;

export async function renderRaw(root: HTMLElement, match: RouteMatch): Promise<void> {
  container = root;
  state.selected = match.query.get("file") ?? state.selected;
  if (!state.project && !state.loading && !state.error) {
    await loadProject(false);
  }
  renderShell();
}

async function loadProject(force: boolean): Promise<void> {
  state.loading = true;
  renderShell();
  try {
    const { ok, body } = force ? await projectReload() : await api.project();
    if (!ok) throw new Error("GET /api/project failed");
    state.project = body;
    state.error = null;
    if (force) state.rawCache.clear();
  } catch (err) {
    state.error = String(err);
  } finally {
    state.loading = false;
    renderShell();
  }
}

async function projectReload() {
  const res = await fetch("/api/project?reload=1");
  const body = (await res.json()) as ProjectDto;
  return { ok: res.ok, body };
}

function renderShell(): void {
  if (!container) return;
  container.innerHTML = "";
  if (state.error) {
    const err = document.createElement("div");
    err.className = "status-err";
    err.style.padding = "24px";
    err.textContent = `Error: ${state.error}`;
    container.appendChild(err);
    return;
  }

  const page = document.createElement("div");
  page.className = "raw-page";
  container.appendChild(page);

  const left = document.createElement("div");
  left.className = "raw-col left";
  page.appendChild(left);

  const right = document.createElement("div");
  right.className = "raw-col raw-viewer";
  page.appendChild(right);

  renderLeft(left);
  renderViewer(right);
}

function renderLeft(col: HTMLElement): void {
  const project = state.project;

  const header = document.createElement("div");
  header.style.display = "flex";
  header.style.justifyContent = "space-between";
  header.style.alignItems = "center";
  const title = document.createElement("div");
  title.innerHTML = `<span class="muted">Project</span>`;
  header.appendChild(title);
  const reload = document.createElement("button");
  reload.className = "raw-reload";
  reload.textContent = state.loading ? "Reloading…" : "Reload";
  reload.disabled = state.loading;
  reload.addEventListener("click", () => void loadProject(true));
  header.appendChild(reload);
  col.appendChild(header);

  if (!project) {
    const loading = document.createElement("div");
    loading.className = "muted";
    loading.textContent = state.loading ? "Loading project…" : "No project loaded.";
    col.appendChild(loading);
    return;
  }

  const meta = document.createElement("div");
  meta.className = "raw-meta";
  const counts = Object.entries(project.declarations.counts)
    .map(([k, v]) => `${k}: ${v}`)
    .join(" · ");
  meta.innerHTML = `
    <h3>Summary</h3>
    <div>${project.files.length} file(s) · ${project.macros.length} macro(s)</div>
    <div style="opacity:0.75">${counts}</div>
    <div class="muted" style="font-size:11px;margin-top:4px">loaded ${project.loadedAt}</div>
  `;
  col.appendChild(meta);

  const treeHead = document.createElement("h3");
  treeHead.className = "raw-meta";
  treeHead.textContent = "Files";
  treeHead.style.margin = "8px 0 4px 0";
  col.appendChild(treeHead);

  const grouped = new Map<string, typeof project.files>();
  for (const file of project.files) {
    const dir = file.path.includes("/") ? file.path.slice(0, file.path.lastIndexOf("/")) : "";
    if (!grouped.has(dir)) grouped.set(dir, []);
    grouped.get(dir)!.push(file);
  }

  const tree = document.createElement("ul");
  tree.className = "raw-tree";
  const dirs = [...grouped.keys()].sort();
  for (const dir of dirs) {
    const label = document.createElement("li");
    label.className = "raw-tree-dir";
    label.textContent = dir || "(root)";
    tree.appendChild(label);
    for (const file of grouped.get(dir)!) {
      const item = document.createElement("li");
      item.className = "raw-tree-file";
      if (file.path === state.selected) item.classList.add("selected");
      const name = file.path.split("/").pop() ?? file.path;
      const nameEl = document.createElement("span");
      nameEl.textContent = name;
      item.appendChild(nameEl);
      const bytes = document.createElement("span");
      bytes.className = "raw-tree-file-bytes";
      bytes.textContent = formatBytes(file.bytes);
      item.appendChild(bytes);
      item.addEventListener("click", () => void selectFile(file.path));
      tree.appendChild(item);
    }
  }
  col.appendChild(tree);

  if (project.macros.length > 0) {
    const macroHead = document.createElement("h3");
    macroHead.className = "raw-meta";
    macroHead.textContent = "Macros";
    macroHead.style.margin = "10px 0 4px 0";
    col.appendChild(macroHead);
    const ul = document.createElement("ul");
    ul.style.margin = "0";
    ul.style.paddingLeft = "18px";
    ul.style.fontSize = "11.5px";
    ul.style.opacity = "0.8";
    for (const m of project.macros) {
      const li = document.createElement("li");
      li.textContent = m;
      ul.appendChild(li);
    }
    col.appendChild(ul);
  }
}

async function selectFile(path: string): Promise<void> {
  state.selected = path;
  updateUrl(path);
  renderShell();
  if (!state.rawCache.has(path)) {
    const { ok, body } = await api.projectFile(path);
    if (ok) state.rawCache.set(path, body);
  }
  renderShell();
}

function updateUrl(path: string): void {
  const params = new URLSearchParams();
  params.set("file", path);
  const hash = `#/raw?${params.toString()}`;
  if (window.location.hash !== hash) {
    history.replaceState(null, "", hash);
  }
}

function renderViewer(col: HTMLElement): void {
  col.innerHTML = "";
  if (!state.selected) {
    const empty = document.createElement("div");
    empty.className = "raw-viewer-empty";
    empty.textContent = "Select a file to view its source.";
    col.appendChild(empty);
    return;
  }

  const header = document.createElement("div");
  header.className = "raw-viewer-header";
  const content = state.rawCache.get(state.selected);
  header.textContent = content === undefined
    ? `${state.selected} — loading…`
    : `${state.selected} — ${content.length} chars`;
  col.appendChild(header);

  const pre = document.createElement("pre");
  pre.className = "raw-viewer-body";
  if (content === undefined) {
    pre.textContent = "(loading)";
  } else {
    pre.appendChild(highlightCcgnf(content));
  }
  col.appendChild(pre);
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}K`;
  return `${(bytes / (1024 * 1024)).toFixed(1)}M`;
}
