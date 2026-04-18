import "./shared/layout.css";
import { renderShell } from "./shared/nav";
import { createRouter } from "./router";
import { renderInterpreter } from "./pages/interpreter";
import { renderCards } from "./pages/cards";
import { renderDecks } from "./pages/decks";
import { renderRaw } from "./pages/raw";
import { renderLobby } from "./pages/play/lobby";
import { renderTabletop } from "./pages/play/tabletop";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app root");

const view = renderShell(root, [
  { label: "Cards", href: "#/cards" },
  { label: "Decks", href: "#/decks" },
  { label: "Interpreter", href: "#/interpreter" },
  { label: "Play", href: "#/play/lobby" },
  { label: "Raw", href: "#/raw" },
]);

const router = createRouter(
  [
    { path: "/interpreter", render: (c) => renderInterpreter(c) },
    { path: "/cards", render: (c, m) => renderCards(c, m) },
    { path: "/decks", render: (c, m) => renderDecks(c, m) },
    { path: "/raw", render: (c, m) => renderRaw(c, m) },
    { path: "/play/lobby", render: (c, m) => renderLobby(c, m) },
    { path: "/play/tabletop", render: (c, m) => renderTabletop(c, m), prefix: true },
  ],
  (c, match) => {
    c.innerHTML = `<main style="padding: 24px"><p class="muted">No route for <code>${match.path}</code></p></main>`;
  },
);

router.mount(view);
