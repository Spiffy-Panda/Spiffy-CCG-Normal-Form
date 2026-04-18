export type RouteRenderer = (container: HTMLElement, path: string) => void | Promise<void>;

export interface RouteDef {
  path: string;
  render: RouteRenderer;
}

export interface Router {
  routes: RouteDef[];
  fallback: RouteRenderer;
  mount: (container: HTMLElement) => void;
}

function currentHashPath(): string {
  const raw = window.location.hash || "#/interpreter";
  return raw.startsWith("#") ? raw.slice(1) : raw;
}

export function createRouter(routes: RouteDef[], fallback: RouteRenderer): Router {
  const match = (path: string): { route: RouteDef | null; path: string } => {
    const hit = routes.find((r) => r.path === path) ?? null;
    return { route: hit, path };
  };

  const router: Router = {
    routes,
    fallback,
    mount(container: HTMLElement) {
      const render = async () => {
        const { route, path } = match(currentHashPath());
        const r = route?.render ?? fallback;
        await r(container, path);
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
