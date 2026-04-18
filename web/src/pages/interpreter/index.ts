import "./style.css";
import { api } from "../../api/client";
import type { PipelineStage, SourceFileDto } from "../../api/dtos";

const DEFAULT_SOURCE = `Entity Game {
  kind: Game
  characteristics: { round: 1, first_player: Unbound }
  abilities: []
}

Entity Player[i] for i \u2208 {1, 2} {
  kind: Player
  characteristics: {
    aether_cap_schedule: [3, 4, 5, 6, 7, 8, 9, 10, 10, 10],
    max_mulligans: 2
  }
  counters: { aether: 0, debt: 0 }
  zones: {
    Arsenal: Zone(order: sequential),
    Hand:    Zone(capacity: 10, order: unordered),
    Cache:   Zone(order: FIFO),
    ResonanceField: Zone(capacity: 5, order: FIFO),
    Void:    Zone(order: unordered)
  }
  abilities: []
}`;

export function renderInterpreter(container: HTMLElement): void {
  container.innerHTML = "";

  const wrap = document.createElement("div");
  wrap.className = "interpreter";
  wrap.innerHTML = `
    <aside>
      <label for="src">.ccgnf source</label>
      <textarea id="src" spellcheck="false"></textarea>

      <div class="row">
        <label for="seed">seed</label>
        <input id="seed" type="number" value="42" style="width: 70px" />
        <label for="inputs">inputs</label>
        <input id="inputs" type="text" value="pass,pass,pass,pass" style="flex: 1; min-width: 120px" />
      </div>

      <div class="row" data-row="stages">
        <button data-action="health">Health</button>
        <button data-action="preprocess">Preprocess</button>
        <button data-action="parse">Parse</button>
        <button data-action="ast">AST</button>
        <button data-action="validate">Validate</button>
        <button class="primary" data-action="run">Run</button>
      </div>

      <div class="row">
        <button data-action="createSession">Create session</button>
        <button data-action="listSessions">List sessions</button>
      </div>
    </aside>

    <main>
      <div class="stage">
        <h2>Output <span data-status></span></h2>
        <pre data-out>idle</pre>
      </div>
    </main>
  `;

  container.appendChild(wrap);

  const src = wrap.querySelector<HTMLTextAreaElement>("#src")!;
  const seed = wrap.querySelector<HTMLInputElement>("#seed")!;
  const inputsEl = wrap.querySelector<HTMLInputElement>("#inputs")!;
  const status = wrap.querySelector<HTMLElement>("[data-status]")!;
  const out = wrap.querySelector<HTMLPreElement>("[data-out]")!;

  src.value = DEFAULT_SOURCE;

  const show = (value: unknown, ok: boolean) => {
    status.textContent = ok ? "ok" : "error";
    status.className = ok ? "status-ok" : "status-err";
    out.textContent = typeof value === "string" ? value : JSON.stringify(value, null, 2);
  };

  const files = (): SourceFileDto[] => [{ path: "playground.ccgnf", content: src.value }];
  const parsedInputs = (): string[] =>
    inputsEl.value
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);

  const dispatch: Record<string, () => Promise<void>> = {
    health: async () => {
      const { ok, body } = await api.health();
      show(body, ok);
    },
    preprocess: async () => runStage("preprocess"),
    parse: async () => runStage("parse"),
    ast: async () => runStage("ast"),
    validate: async () => runStage("validate"),
    run: async () => {
      const { ok, body } = await api.run({
        files: files(),
        seed: parseInt(seed.value, 10),
        inputs: parsedInputs(),
      });
      show(body, ok && body.ok !== false);
    },
    createSession: async () => {
      const { ok, body } = await api.createSession({
        files: files(),
        seed: parseInt(seed.value, 10),
        inputs: parsedInputs(),
      });
      show(body, ok);
    },
    listSessions: async () => {
      const { ok, body } = await api.listSessions();
      show(body, ok);
    },
  };

  async function runStage(stage: PipelineStage): Promise<void> {
    const req = { files: files() };
    const { ok, body } =
      stage === "preprocess"
        ? await api.preprocess(req)
        : stage === "parse"
          ? await api.parse(req)
          : stage === "ast"
            ? await api.ast(req)
            : await api.validate(req);
    show(body, ok && body.ok !== false);
  }

  wrap.addEventListener("click", async (ev) => {
    const target = ev.target as HTMLElement | null;
    if (!target || target.tagName !== "BUTTON") return;
    const action = target.getAttribute("data-action");
    if (!action) return;
    const fn = dispatch[action];
    if (!fn) return;
    try {
      await fn();
    } catch (err) {
      show(String(err), false);
    }
  });
}
