import "./shared/layout.css";
import { renderShell } from "./shared/nav";
import { createRouter } from "./router";
import { renderInterpreter } from "./pages/interpreter";
import { renderCards } from "./pages/cards";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app root");

const view = renderShell(root, [
  { label: "Cards", href: "#/cards" },
  { label: "Interpreter", href: "#/interpreter" },
]);

const router = createRouter(
  [
    { path: "/interpreter", render: (c) => renderInterpreter(c) },
    { path: "/cards", render: (c, m) => renderCards(c, m) },
  ],
  (c, match) => {
    c.innerHTML = `<main style="padding: 24px"><p class="muted">No route for <code>${match.path}</code></p></main>`;
  },
);

router.mount(view);
