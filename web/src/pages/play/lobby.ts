import "./style.css";
import { api } from "../../api/client";
import type {
  PresetDeckDto,
  RoomCpuSeatSpec,
  RoomSummaryDto,
  SourceFileDto,
} from "../../api/dtos";
import { buildHash, type RouteMatch } from "../../router";

interface CpuDraft {
  name: string;
  deckKey: string;   // "preset:<id>" | ""
}

interface LobbyState {
  rooms: RoomSummaryDto[];
  loading: boolean;
  error: string | null;
  creating: boolean;
  seed: number;
  presets: PresetDeckDto[];
  cpuSeats: CpuDraft[];
}

const state: LobbyState = {
  rooms: [],
  loading: false,
  error: null,
  creating: false,
  seed: 42,
  presets: [],
  cpuSeats: [],
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
    const [rooms, presets] = await Promise.all([
      api.listRooms(),
      state.presets.length === 0 ? api.deckPresets() : Promise.resolve({ ok: true, body: state.presets }),
    ]);
    if (!rooms.ok) throw new Error("GET /api/rooms failed");
    state.rooms = rooms.body;
    if (presets.ok) state.presets = presets.body;
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
    "Rooms host multi-seat play against the loaded encoding. Fill any unclaimed seats with CPUs below; humans pick a deck on join.";
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
  createBtn.disabled = state.creating || state.cpuSeats.length >= 2;
  createBtn.addEventListener("click", () => void createRoom());
  row.appendChild(createBtn);

  const reloadBtn = document.createElement("button");
  reloadBtn.className = "play-btn";
  reloadBtn.textContent = state.loading ? "Refreshing…" : "Refresh";
  reloadBtn.disabled = state.loading;
  reloadBtn.addEventListener("click", () => void refresh());
  row.appendChild(reloadBtn);

  createSection.appendChild(row);
  createSection.appendChild(renderCpuSeatsEditor());
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

function renderCpuSeatsEditor(): HTMLElement {
  const wrap = document.createElement("div");
  wrap.className = "play-cpu-seats";
  wrap.style.marginTop = "10px";

  const caption = document.createElement("div");
  caption.className = "muted";
  caption.style.fontSize = "12px";
  caption.style.marginBottom = "6px";
  caption.textContent =
    state.cpuSeats.length === 0
      ? "No CPU seats. Add one to play solo against a bot that takes the first legal action."
      : `CPU seats: ${state.cpuSeats.length}. Each CPU fills one seat; the rest wait for humans.`;
  wrap.appendChild(caption);

  for (let i = 0; i < state.cpuSeats.length; i++) {
    const seat = state.cpuSeats[i];
    const row = document.createElement("div");
    row.className = "play-row";
    row.style.marginBottom = "4px";

    const label = document.createElement("span");
    label.textContent = `🤖 CPU ${i + 1}`;
    label.style.fontSize = "12px";
    row.appendChild(label);

    const nameInput = document.createElement("input");
    nameInput.className = "play-input";
    nameInput.placeholder = "name (optional)";
    nameInput.style.width = "160px";
    nameInput.value = seat.name;
    nameInput.addEventListener("input", () => { seat.name = nameInput.value; });
    row.appendChild(nameInput);

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
        if (seat.deckKey === opt.value) opt.selected = true;
        group.appendChild(opt);
      }
      select.appendChild(group);
    }
    select.addEventListener("change", () => {
      seat.deckKey = select.value;
    });
    row.appendChild(select);

    const removeBtn = document.createElement("button");
    removeBtn.className = "play-btn";
    removeBtn.textContent = "✕";
    removeBtn.title = "Remove CPU seat";
    removeBtn.addEventListener("click", () => {
      state.cpuSeats.splice(i, 1);
      renderShell();
    });
    row.appendChild(removeBtn);

    wrap.appendChild(row);
  }

  const addBtn = document.createElement("button");
  addBtn.className = "play-btn";
  addBtn.textContent = "+ Add CPU player";
  addBtn.disabled = state.cpuSeats.length >= 2;
  addBtn.addEventListener("click", () => {
    state.cpuSeats.push({ name: "", deckKey: "" });
    renderShell();
  });
  wrap.appendChild(addBtn);

  return wrap;
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

    const cpuSeats: RoomCpuSeatSpec[] = state.cpuSeats.map((c) => ({
      name: c.name.trim() || null,
      deck: c.deckKey.startsWith("preset:")
        ? { preset: c.deckKey.slice(7) }
        : null,
    }));

    const created = await api.createRoom({
      files,
      seed: state.seed,
      playerSlots: 2,
      cpuSeats: cpuSeats.length > 0 ? cpuSeats : undefined,
    });
    if (!created.ok) throw new Error("POST /api/rooms failed");
    window.location.hash = buildHash(`/play/tabletop/${created.body.roomId}`);
  } catch (err) {
    state.error = String(err);
  } finally {
    state.creating = false;
    renderShell();
  }
}
