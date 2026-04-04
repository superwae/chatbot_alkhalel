import { defineConfig } from "vite";

export default defineConfig({
  build: {
    lib: {
      entry: "src/index.ts",
      name: "MunicipalityChatbot",
      formats: ["umd"],
      fileName: () => "widget.js",
    },
    sourcemap: true,
    minify: true,
  },
});

