export interface RouteMatch {
  path: string;
  query: URLSearchParams;
}

export type RouteRenderer = (container: HTMLElement, match: RouteMatch) => void | Promise<void>;

export interface RouteDef {
  path: string;
  render: RouteRenderer;
  /** When true, match any path that starts with <c>path + "/"</c>. */
  prefix?: boolean;
}

export interface Router {
  routes: RouteDef[];
  fallback: RouteRenderer;
  mount: (container: HTMLElement) => void;
}

function parseHash(): RouteMatch {
  const raw = window.location.hash || "#/interpreter";
  const stripped = raw.startsWith("#") ? raw.slice(1) : raw;
  const qIndex = stripped.indexOf("?");
  if (qIndex < 0) return { path: stripped, query: new URLSearchParams() };
  return {
    path: stripped.slice(0, qIndex),
    query: new URLSearchParams(stripped.slice(qIndex + 1)),
  };
}

export function createRouter(routes: RouteDef[], fallback: RouteRenderer): Router {
  const match = (): { route: RouteDef | null; routeMatch: RouteMatch } => {
    const routeMatch = parseHash();
    const hit =
      routes.find((r) => r.path === routeMatch.path) ??
      routes.find((r) => r.prefix && routeMatch.path.startsWith(`${r.path}/`)) ??
      null;
    return { route: hit, routeMatch };
  };

  const router: Router = {
    routes,
    fallback,
    mount(container: HTMLElement) {
      const render = async () => {
        const { route, routeMatch } = match();
        const r = route?.render ?? fallback;
        await r(container, routeMatch);
      };
      window.addEventListener("hashchange", render);
      if (!window.location.hash) {
        window.location.hash = "#/interpreter";
      }
      void render();
    },
  };
  return router;
}

export function buildHash(path: string, query?: URLSearchParams): string {
  const qs = query?.toString();
  return qs ? `#${path}?${qs}` : `#${path}`;
}
