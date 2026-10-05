import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@warp/plex": path.resolve(__dirname, "../../packages/plex/src/index.ts"),
      "@warp/ui": path.resolve(__dirname, "../../packages/ui/src/index.ts"),
      "@warp/display-lab": path.resolve(__dirname, "../../packages/display-lab/src/index.ts"),
    },
  },
  base: "./",
  build: {
    target: "chrome85",
    outDir: "dist",
    cssCodeSplit: false,
    sourcemap: false,
    rollupOptions: {
      output: {
        inlineDynamicImports: true,
        entryFileNames: "assets/app.js",
        assetFileNames: "assets/app.[ext]",
      },
    },
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
  },
});
