<script>
  import { app, TITLES, cmd } from "../lib/state.svelte.js";
</script>

<header id="titlebar">
  <div class="tb-left">
    <span class="tb-logo">◈</span>
    <span class="tb-name">TempPanel</span>
    <span class="tb-sep"></span>
    <span id="tb-page" class="tb-sub">{TITLES[app.page] || ""}</span>
  </div>
  <div class="tb-drag" title="Arrastra para mover la ventana · doble clic para maximizar"></div>
  <div class="tb-right">
    <button id="win-min" class="tb-btn" title="Minimizar" aria-label="Minimizar" onclick={cmd.winMin}>─</button>
    <button id="win-max" class="tb-btn" class:os-hover={app.osHover === "win-max"} title="Maximizar o restaurar (pasa el ratón para Snap Layouts)" aria-label="Maximizar" onclick={cmd.winMax}>▢</button>
    <button id="win-close" class="tb-btn tb-close" title="Ocultar a la bandeja (la app sigue)" aria-label="Cerrar" onclick={cmd.winClose}>✕</button>
  </div>
</header>
