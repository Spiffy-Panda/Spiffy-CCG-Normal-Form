import "./style.css";
import { api } from "../../api/client";
import type { RoomSummaryDto, SourceFileDto } from "../../api/dtos";
import { buildHash, type RouteMatch } from "../../router";

interface LobbyState {
  rooms: RoomSummaryDto[];
  loading: boolean;
  error: string | null;
  creating: boolean;
  seed: number;
}

const state: LobbyState = {
  rooms: [],
  loading: false,
  error: null,
  creating: false,
  seed: 42,
};

let container: HTMLElement | null = null;

export async function renderLobby(root: HTMLElement, _match: RouteMatch): Promise<void> {
  container = root;
  await refresh();
}

async function refresh(): Promise<void> {
  state.loading = true;
  renderShell();
  try {
    const { ok, body } = await api.listRooms();
    if (!ok) throw new Error("GET /api/rooms failed");
    state.rooms = body;
    state.error = null;
  } catch (err) {
    state.error = String(err);
  } finally {
    state.loading = false;
    renderShell();
  }
}

function renderShell(): void {
  if (!container) return;
  container.innerHTML = "";
  const page = document.createElement("div");
  page.className = "play-page";
  container.appendChild(page);

  const intro = document.createElement("p");
  intro.className = "muted";
  intro.style.margin = "0 0 14px 0";
  intro.textContent =
    "Rooms host multi-consumer play against the loaded encoding. v1 runs the interpreter synchronously at start; actions queue for a future async-interpreter pass.";
  page.appendChild(intro);

  // Create section.
  const createSection = document.createElement("div");
  createSection.className = "play-section";
  createSection.innerHTML = `<h3>Create room</h3>`;
  const row = document.createElement("div");
  row.className = "play-row";

  const seedLabel = document.createElement("label");
  seedLabel.textContent = "seed";
  seedLabel.style.fontSize = "12px";
  row.appendChild(seedLabel);

  const seedInput = document.createElement("input");
  seedInput.className = "play-input";
  seedInput.type = "number";
  seedInput.value = String(state.seed);
  seedInput.style.width = "90px";
  seedInput.addEventListener("change", () => {
    state.seed = parseInt(seedInput.value, 10) || 0;
  });
  row.appendChild(seedInput);

  const createBtn = document.createElement("button");
  createBtn.className = "play-btn primary";
  createBtn.textContent = state.creating ? "Creating…" : "Create with loaded encoding";
  createBtn.disabled = state.creating;
  createBtn.addEventListener("click", () => void createRoom());
  row.appendChild(createBtn);

  const reloadBtn = document.createElement("button");
  reloadBtn.className = "play-btn";
  reloadBtn.textContent = state.loading ? "Refreshing…" : "Refresh";
  reloadBtn.disabled = state.loading;
  reloadBtn.addEventListener("click", () => void refresh());
  row.appendChild(reloadBtn);

  createSection.appendChild(row);
  page.appendChild(createSection);

  if (state.error) {
    const err = document.createElement("div");
    err.className = "status-err";
    err.style.padding = "8px 12px";
    err.textContent = `Error: ${state.error}`;
    page.appendChild(err);
  }

  // Rooms table.
  const listSection = document.createElement("div");
  listSection.className = "play-section";
  listSection.innerHTML = `<h3>Rooms (${state.rooms.length})</h3>`;
  if (state.rooms.length === 0) {
    const empty = document.createElement("div");
    empty.className = "muted";
    empty.textContent = "No rooms yet.";
    listSection.appendChild(empty);
  } else {
    const table = document.createElement("table");
    table.className = "rooms-table";
    table.innerHTML = `
      <thead><tr>
        <th>ID</th><th>State</th><th>Slots</th><th>Seed</th><th>Created</th><th></th>
      </tr></thead>`;
    const tbody = document.createElement("tbody");
    for (const room of state.rooms) {
      const tr = document.createElement("tr");
      if (room.state === "Finished") tr.classList.add("finished");
      tr.innerHTML = `
        <td>${room.roomId}</td>
        <td>${room.state}</td>
        <td>${room.occupied}/${room.playerSlots}</td>
        <td>${room.seed}</td>
        <td>${new Date(room.createdAt).toLocaleTimeString()}</td>`;
      const actionTd = document.createElement("td");
      const link = document.createElement("a");
      link.href = buildHash(`/play/tabletop/${room.roomId}`);
      link.textContent = "Open";
      actionTd.appendChild(link);
      tr.appendChild(actionTd);
      tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    listSection.appendChild(table);
  }
  page.appendChild(listSection);
}

async function createRoom(): Promise<void> {
  state.creating = true;
  state.error = null;
  renderShell();
  try {
    const { ok, body } = await api.project();
    if (!ok) throw new Error("GET /api/project failed");
    const files: SourceFileDto[] = await Promise.all(
      body.files.map(async (f) => {
        const res = await api.projectFile(f.path);
        return { path: f.path, content: res.body };
      }),
    );
    const created = await api.createRoom({ files, seed: state.seed, playerSlots: 2 });
    if (!created.ok) throw new Error("POST /api/rooms failed");
    window.location.hash = buildHash(`/play/tabletop/${created.body.roomId}`);
  } catch (err) {
    state.error = String(err);
  } finally {
    state.creating = false;
    renderShell();
  }
}
