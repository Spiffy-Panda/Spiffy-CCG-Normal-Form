import "./shared/layout.css";
import { renderShell } from "./shared/nav";
import { createRouter } from "./router";
import { renderInterpreter } from "./pages/interpreter";

const root = document.getElementById("app");
if (!root) throw new Error("Missing #app root");

const view = renderShell(root, [{ label: "Interpreter", href: "#/interpreter" }]);

const router = createRouter(
  [
    { path: "/interpreter", render: (c) => renderInterpreter(c) },
  ],
  (c, path) => {
    c.innerHTML = `<main style="padding: 24px"><p class="muted">No route for <code>${path}</code></p></main>`;
  },
);

router.mount(view);
