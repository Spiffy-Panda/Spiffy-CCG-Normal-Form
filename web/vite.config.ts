import { defineConfig } from "vite";

export default defineConfig({
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: "http://localhost:19397",
        changeOrigin: false,
      },
    },
  },
  build: {
    target: "es2022",
    emptyOutDir: true,
  },
});
