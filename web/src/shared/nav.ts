import { api } from "../api/client";

export interface NavLink {
  label: string;
  href: string;
}

export function renderShell(root: HTMLElement, links: NavLink[]): HTMLElement {
  root.innerHTML = "";
  const shell = document.createElement("div");
  shell.className = "app-shell";

  const header = document.createElement("header");
  header.className = "app-header";
  shell.appendChild(header);

  const title = document.createElement("h1");
  title.textContent = "CCGNF Playground";
  header.appendChild(title);

  const nav = document.createElement("nav");
  for (const l of links) {
    const a = document.createElement("a");
    a.href = l.href;
    a.textContent = l.label;
    a.dataset.route = l.href.replace(/^#/, "");
    nav.appendChild(a);
  }
  header.appendChild(nav);

  const portPill = document.createElement("span");
  portPill.className = "muted";
  portPill.innerHTML = 'port <span data-port>&mdash;</span>';
  header.appendChild(portPill);

  const healthPill = document.createElement("span");
  healthPill.className = "muted health-pill";
  healthPill.textContent = "health: unknown";
  header.appendChild(healthPill);

  const view = document.createElement("div");
  view.className = "route-view";
  view.id = "route-view";
  shell.appendChild(view);

  root.appendChild(shell);

  updateActiveLink(nav);
  window.addEventListener("hashchange", () => updateActiveLink(nav));

  void refreshHealth(portPill, healthPill);

  return view;
}

function updateActiveLink(nav: HTMLElement): void {
  const current = (window.location.hash || "#/interpreter").replace(/^#/, "");
  nav.querySelectorAll<HTMLAnchorElement>("a").forEach((a) => {
    a.classList.toggle("active", a.dataset.route === current);
  });
}

export async function refreshHealth(portPill: HTMLElement, healthPill: HTMLElement): Promise<void> {
  try {
    const { body } = await api.health();
    const portEl = portPill.querySelector("[data-port]");
    if (portEl) portEl.textContent = String(body.port);
    healthPill.textContent = "health: ok";
    healthPill.classList.remove("status-err");
    healthPill.classList.add("status-ok");
  } catch {
    healthPill.textContent = "health: down";
    healthPill.classList.remove("status-ok");
    healthPill.classList.add("status-err");
  }
}
