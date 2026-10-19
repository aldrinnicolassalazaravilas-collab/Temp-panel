<script>
  import { onMount } from "svelte";
  import { app, cmd, initBridge, loadUiPrefs, saveUiPrefs, setPage, togglePause, toggleImmersive, syncOsTheme } from "./lib/state.svelte.js";
  import TitleBar from "./components/TitleBar.svelte";
  import Sidebar from "./components/Sidebar.svelte";
  import TopBar from "./components/TopBar.svelte";
  import Toasts from "./components/Toasts.svelte";
  import Welcome from "./components/Welcome.svelte";
  import Resumen from "./pages/Resumen.svelte";
  import Cpu from "./pages/Cpu.svelte";
  import Gpu from "./pages/Gpu.svelte";
  import Ram from "./pages/Ram.svelte";
  import Bateria from "./pages/Bateria.svelte";
  import Refrigeracion from "./pages/Refrigeracion.svelte";
  import Sensores from "./pages/Sensores.svelte";
  import Historial from "./pages/Historial.svelte";
  import Ayuda from "./pages/Ayuda.svelte";
  import Config from "./pages/Config.svelte";

  const NAV_KEYS = {
    "1": "resumen", "2": "cpu", "3": "gpu", "4": "ram", "5": "bateria",
    "6": "refrigeracion", "7": "sensores", "8": "historial", "9": "ayuda", "0": "config"
  };

  onMount(() => {
    loadUiPrefs();
    initBridge();
    const onKey = (e) => {
      if (e.ctrlKey || e.altKey || e.metaKey) return;
      const t = e.target;
      if (t && (t.tagName === "INPUT" || t.tagName === "SELECT" || t.tagName === "TEXTAREA" || t.isContentEditable)) return;
      const k = (e.key || "").toLowerCase();
      if (NAV_KEYS[k]) { setPage(NAV_KEYS[k]); e.preventDefault(); }
      else if (k === "f") toggleImmersive();
      else if (k === "p") togglePause();
      else if (k === "r") cmd.getSnapshot();
      else if (k === "m") cmd.openMini();
      else if (k === "escape" && app.immersive) toggleImmersive();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  // Aspecto: cristal/no cristal, modo inmersivo y variables del fondo animado.
  $effect(() => {
    const glass = !!app.cfg.glass;
    document.body.classList.toggle("glass", glass);
    document.body.classList.toggle("no-glass", !glass);
  });
  $effect(() => { document.body.classList.toggle("immersive", !!app.immersive); });
  // v3.0.0: alto contraste + color de acento que envía Windows.
  $effect(() => { syncOsTheme(); });
  $effect(() => {
    const st = document.documentElement.style;
    const a = app.uiAlpha;
    st.setProperty("--panel-a", a.toFixed(3));
    st.setProperty("--scrim-a", Math.max(0.12, a * 1.15).toFixed(3));
    st.setProperty("--aur-a", app.aurora.a);
    st.setProperty("--aur-b", app.aurora.b);
    st.setProperty("--aur-c", app.aurora.c);
  });
  $effect(() => {
    // Guarda las preferencias de interfaz (solo locales) cuando cambian.
    app.uiAlpha; app.immersive;
    saveUiPrefs();
  });
</script>

<div class="bg" aria-hidden="true">
  <div class="aurora a"></div>
  <div class="aurora b"></div>
  <div class="aurora c"></div>
  <div class="grid-bg"></div>
  <div class="sheen"></div>
</div>

<TitleBar />

<div id="content">
  <Sidebar />
  <main id="main">
    <TopBar />
    {#if app.warns.length}
      <div id="warn-bar" class="warnbar">{app.warns.join(" · ")}</div>
    {/if}
    {#key app.page}
      <section class="page" id="page-{app.page}">
        {#if app.page === "cpu"}<Cpu />
        {:else if app.page === "gpu"}<Gpu />
        {:else if app.page === "ram"}<Ram />
        {:else if app.page === "bateria"}<Bateria />
        {:else if app.page === "refrigeracion"}<Refrigeracion />
        {:else if app.page === "sensores"}<Sensores />
        {:else if app.page === "historial"}<Historial />
        {:else if app.page === "ayuda"}<Ayuda />
        {:else if app.page === "config"}<Config />
        {:else}<Resumen />{/if}
      </section>
    {/key}
  </main>
</div>

{#if app.cfg.firstRun}<Welcome />{/if}
<Toasts />
