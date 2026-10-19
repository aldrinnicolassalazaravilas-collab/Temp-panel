<script>
  import { app, TITLES, cmd, togglePause, toggleImmersive, PERF_LABELS } from "../lib/state.svelte.js";
</script>

<header id="topbar">
  <div class="tb-head">
    <h1 id="page-title">{TITLES[app.page] || ""}</h1>
    <div id="last-read" class="muted">{app.lastRead}</div>
  </div>
  <div class="top-actions">
    {#if app.cfg.perf && app.cfg.perf !== "normal"}
      <span id="perf-chip" class="alert info"
            title="TempPanel reduce la actividad mientras no lo miras (segundo plano, pantalla completa o ahorro de energía). Se puede desactivar en Configuración → Integración con Windows.">
        {PERF_LABELS[app.cfg.perf] || app.cfg.perf}
      </span>
    {/if}
    <span id="health-dot" class="health {app.health}" title="Estado general"></span>
    {#if app.alertCls}
      <span id="alert-banner" class="alert {app.alertCls}">{app.alertText}</span>
    {/if}
    <button id="btn-immersive" class="btn ghost" class:on={app.immersive}
            title="Modo inmersivo (F): la interfaz se aparta y el fondo se ve entero"
            onclick={toggleImmersive}>◐ Inmersivo</button>
    <button id="btn-mini" class="btn ghost" title="Mini ventana siempre visible (M)"
            onclick={cmd.openMini}>🗔 Mini</button>
    <button id="btn-pause" class="btn ghost" class:on={app.paused}
            title="Congela gráficas e historial; los sensores siguen midiendo (P)"
            onclick={togglePause}>{app.paused ? "▶ Reanudar" : "⏸ Pausar"}</button>
    <button id="btn-snapshot" class="btn ghost" title="Pedir una lectura ahora (R)"
            onclick={cmd.getSnapshot}>↻ Actualizar</button>
  </div>
</header>
