import { defineConfig } from "vite";
import { svelte } from "@sveltejs/vite-plugin-svelte";
import { readFileSync } from "node:fs";

// Versión real de Svelte que se está compilando: se muestra en Configuración
// (transparencia total: lo que dice la interfaz es lo que se usó de verdad).
const svelteVersion = JSON.parse(
  readFileSync(new URL("./node_modules/svelte/package.json", import.meta.url), "utf8")
).version;

// La interfaz se escribe en Svelte y se COMPILA a la carpeta TempPanel/Frontend
// (HTML + JS + CSS planos, sin CDN ni dependencias en tiempo de ejecución): la
// app sigue funcionando 100 % sin Internet y el resultado se puede revisar en
// un navegador normal.
//
// - base "./"  -> rutas relativas: funciona en la app (https://temppanel.local)
//   y también al abrir el HTML desde el disco en una vista previa.
// - formato "iife" -> script clásico (sin módulos ES): así la vista previa y
//   las pruebas de DOM pueden cargarlo igual que antes.
// El bundle ya sale como script clásico (iife). Quitar type="module" y
// crossorigin permite abrir el HTML directamente desde el disco (file://, para
// la vista previa) y cargarlo en las pruebas de DOM; dentro de la app
// (https://temppanel.local) funciona exactamente igual.
function classicScript() {
  return {
    name: "temppanel-classic-script",
    enforce: "post",
    transformIndexHtml(html) {
      let out = html
        .replace('<script type="module" crossorigin', "<script")
        .replace('<link rel="stylesheet" crossorigin', '<link rel="stylesheet"');
      // Un script clásico se ejecuta al instante: hay que bajarlo al final del
      // body (Vite lo pone en <head>) para que #app ya exista.
      const moved = [];
      out = out.replace(/(\s*)<script src="[^"]*"><\/script>/g, (m) => {
        moved.push(m.trim());
        return "";
      });
      if (moved.length) out = out.replace("</body>", moved.join("\n") + "\n</body>");
      return out;
    }
  };
}

export default defineConfig({
  plugins: [svelte(), classicScript()],
  root: "src",
  base: "./",
  publicDir: false,
  define: { __SVELTE_VERSION__: JSON.stringify(svelteVersion) },
  build: {
    // outDir se resuelve respecto a root (src/) -> TempPanel/Frontend
    outDir: "../../Frontend",
    emptyOutDir: true,
    target: "chrome120",
    cssCodeSplit: false,
    assetsInlineLimit: 0,
    modulePreload: false,
    reportCompressedSize: false,
    rollupOptions: {
      output: {
        format: "iife",
        inlineDynamicImports: true,
        entryFileNames: "assets/app.js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: "assets/[name][extname]"
      }
    }
  }
});
