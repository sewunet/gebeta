import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// In development the React app runs on :5173 and /api is proxied to the Gebeta API,
// so the browser sees one origin (no CORS). GEBETA_API defaults to the Docker server container.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": { target: globalThis.process?.env.GEBETA_API ?? "http://localhost:5090", changeOrigin: true },
    },
  },
  test: {
    environment: "node",
  },
});
