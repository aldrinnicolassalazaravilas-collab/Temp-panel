<script>
  import { app, cmd, setPage } from "../lib/state.svelte.js";

  function dismiss() { app.cfg.firstRun = false; cmd.setFirstRunDone(); }
  function openHelp() { dismiss(); setPage("ayuda"); }
</script>

<div id="welcome">
  <div class="welcome-card">
    <h2>◈ Bienvenido a TempPanel</h2>
    <div class="muted">Monitor de hardware en tiempo real. Dos cosas antes de empezar:</div>
    <div class="muted" style="margin-top:8px"><b>1. Permisos.</b> La app pide administrador
      al abrirse porque las temperaturas del procesador solo las entrega Windows a programas
      elevados. Sin eso verás cargas y batería, pero no temperaturas.</div>
    <div class="muted" style="margin-top:8px"><b>2. Driver PawnIO.</b> Si las temperaturas no
      aparecen, instala el driver oficial firmado desde <b>pawnio.eu</b> y reabre la app.
      La sección Ayuda lo detecta sola.</div>
    <div class="toolbar" style="margin-top:12px">
      <button id="btn-welcome-ok" class="btn" onclick={dismiss}>Entendido</button>
      <button id="btn-welcome-help" class="btn ghost" onclick={openHelp}>Ver guía</button>
    </div>
  </div>
</div>
