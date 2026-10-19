<script>
  import { app, cmd, toast, saveUiPrefs } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Kv from "../components/Kv.svelte";

  // Formulario local: nada se aplica hasta pulsar su botón (igual que antes).
  let f = $state({ refresh: 2, cpuWarn: 85, cpuCrit: 95, gpuWarn: 80, gpuCrit: 90, batTh: 80, batRe: 75 });
  $effect(() => {
    f.refresh = Math.round((app.cfg.refreshMs || 2000) / 1000);
    f.cpuWarn = app.cfg.cpuWarnTemp; f.cpuCrit = app.cfg.cpuCritTemp;
    f.gpuWarn = app.cfg.gpuWarnTemp; f.gpuCrit = app.cfg.gpuCritTemp;
    f.batTh = app.cfg.batteryThreshold ?? 80; f.batRe = app.cfg.batteryRearm ?? 75;
  });

  const MINI_KEYS = [
    { k: "cpu", l: "CPU", c: "#4f8cff" },
    { k: "temp", l: "Temp", c: "#22d3ee" },
    { k: "bat", l: "Batería", c: "#34d399" },
    { k: "ram", l: "RAM", c: "#a78bfa" },
    { k: "gpu", l: "GPU", c: "#f472b6" }
  ];
  let mini = $state({ cpu: true, temp: true, bat: true, ram: true, gpu: true, expanded: false });
  $effect(() => {
    const set = new Set(String(app.cfg.miniSeries || "").split(",").filter(Boolean));
    MINI_KEYS.forEach((m) => { mini[m.k] = set.has(m.k); });
    mini.expanded = !!app.cfg.miniExpanded;
  });
  function postMini() {
    let series = MINI_KEYS.filter((m) => mini[m.k]).map((m) => m.k).join(",");
    if (!series) { series = "cpu"; mini.cpu = true; } // nunca dejar el gráfico vacío
    cmd.setMiniOptions(series, mini.expanded);
  }

  function applyRefresh() {
    const s = Math.min(10, Math.max(1, parseInt(f.refresh, 10) || 2));
    cmd.setRefresh(s * 1000);
    toast(`Intervalo aplicado: cada ${s} s`, "ok");
  }
  function saveThresholds() {
    cmd.setThresholds({
      cpuWarn: parseFloat(f.cpuWarn) || 80, cpuCrit: parseFloat(f.cpuCrit) || 90,
      gpuWarn: parseFloat(f.gpuWarn) || 80, gpuCrit: parseFloat(f.gpuCrit) || 90
    });
    toast("Umbrales guardados", "ok");
  }
  function saveBattery() {
    let th = Math.min(100, Math.max(50, parseFloat(f.batTh) || 80));
    let re = Math.min(99, Math.max(10, parseFloat(f.batRe) || 75));
    if (re >= th) { toast("El segundo valor debe ser menor que el primero", "err"); return; }
    cmd.setBatteryAlert({
      enabled: app.cfg.batteryEnabled !== false, threshold: th, rearm: re,
      sound: app.cfg.batterySound !== false, mode: app.cfg.batteryMode, full: app.cfg.batteryFull !== false
    });
    toast("Alerta de batería guardada", "ok");
  }
  function toggleStartup(e) {
    const on = e.currentTarget.checked;
    if (on && !confirm("TempPanel se iniciará con Windows. Puedes desactivarlo aquí o en el Administrador de tareas > Aplicaciones de arranque. ¿Continuar?")) {
      e.currentTarget.checked = false;
      return;
    }
    cmd.setStartWithWindows(on);
  }
  function resetAll() {
    if (confirm("¿Restablecer todos los ajustes a sus valores predeterminados?")) cmd.resetSettings();
  }
  function restartAdmin() {
    if (confirm("Se reiniciará TempPanel con permisos de administrador. ¿Continuar?")) cmd.restartAdmin();
  }

  let verRows = $derived([
    { k: "TempPanel", v: `${app.cfg.version} (WPF + WebView2)` },
    { k: "Sensores", v: "LibreHardwareMonitorLib 0.9.6" },
    { k: "Interfaz", v: `Svelte ${__SVELTE_VERSION__} compilada a HTML/JS/CSS local (sin CDN)` },
    { k: "WebView2", v: "Microsoft.Web.WebView2 1.0.4258.31 (runtime del sistema)" },
    { k: "Cristal de Windows", v: app.cfg.glass ? "Activado (acrílico + esquinas redondeadas)" : "No disponible: tema oscuro sólido" },
    { k: "Ahorro de RAM", v: "Motor de interfaz suspendido al minimizar o usar la mini ventana" },
    { k: ".NET", v: "8.0 autocontenido x64" }
  ]);

  function alphaInput(e) {
    app.uiAlpha = Math.min(0.6, Math.max(0.1, Number(e.currentTarget.value) / 100));
    saveUiPrefs();
  }
</script>

<Card title="Actualización">
  <label class="fld">Intervalo de lectura (1–10 s):
    <input id="cfg-refresh" type="number" min="1" max="10" step="1" bind:value={f.refresh}> s
  </label>
  <button id="btn-save-refresh" class="btn" onclick={applyRefresh}>Aplicar</button>
  <div class="muted small">Predeterminado: cada 2 segundos. Para gráficas cada 1 s, pon 1.
    Intervalos muy cortos aumentan la carga del equipo.</div>
  <label class="fld">Unidades de temperatura:
    <select id="cfg-unit" bind:value={app.cfg.unit} onchange={() => cmd.setUnit(app.cfg.unit)}>
      <option value="C">Celsius (°C)</option>
      <option value="F">Fahrenheit (°F)</option>
    </select>
  </label>
</Card>

<Card title="Alertas térmicas (informativas)">
  <div class="grid cards-2">
    <label class="fld">CPU aviso (°C): <input id="cfg-cpu-warn" type="number" bind:value={f.cpuWarn}></label>
    <label class="fld">CPU crítica (°C): <input id="cfg-cpu-crit" type="number" bind:value={f.cpuCrit}></label>
    <label class="fld">GPU aviso (°C): <input id="cfg-gpu-warn" type="number" bind:value={f.gpuWarn}></label>
    <label class="fld">GPU crítica (°C): <input id="cfg-gpu-crit" type="number" bind:value={f.gpuCrit}></label>
  </div>
  <button id="btn-save-th" class="btn" onclick={saveThresholds}>Guardar umbrales</button>
  <div class="muted small">Son avisos visuales con histéresis (3 °C) y pausa de 10 min entre avisos.
    No son límites universales ni garantías de seguridad. TempPanel nunca modifica voltajes,
    ventiladores ni firmware.</div>
</Card>

<Card title="⬢ Alerta de batería">
  <label class="fld"><input type="checkbox" id="cfg-bat-on" bind:checked={app.cfg.batteryEnabled}> Activar alertas de batería</label>
  <label class="fld"><input type="checkbox" id="cfg-bat-full" bind:checked={app.cfg.batteryFull}> Avisar al completar la carga (99 %)</label>
  <label class="fld">Avisar cuando la carga llegue a (%): <input id="cfg-bat-th" type="number" min="50" max="100" bind:value={f.batTh}></label>
  <label class="fld">Volver a avisar cuando baje de (%): <input id="cfg-bat-re" type="number" min="10" max="99" bind:value={f.batRe}></label>
  <label class="fld"><input type="checkbox" id="cfg-bat-sound" bind:checked={app.cfg.batterySound}> Sonido</label>
  <label class="fld">Tipo de aviso:
    <select id="cfg-bat-mode" bind:value={app.cfg.batteryMode}>
      <option value="popup">Ventana emergente con botón OK</option>
      <option value="balloon">Notificación de Windows (bandeja)</option>
    </select>
  </label>
  <button id="btn-save-bat" class="btn" onclick={saveBattery}>Guardar alerta</button>
  <button id="btn-test-bat" class="btn ghost" onclick={cmd.testBatteryAlert}>Probar apariencia</button>
  <div class="muted small">Avisa una vez por ciclo de carga, solo cargando. Vuelve a avisar cuando baja
    del segundo valor. «Probar» muestra el aspecto sin fingir un nivel real. Notificar no detiene la
    carga física.</div>
</Card>

<Card title="Inicio y bandeja">
  <label class="fld"><input type="checkbox" id="cfg-min" bind:checked={app.cfg.startMinimized}
    onchange={() => cmd.setStartMinimized(!!app.cfg.startMinimized)}> Iniciar minimizado en la bandeja</label>
  <label class="fld"><input type="checkbox" id="cfg-startup" checked={!!app.cfg.startWithWindows}
    onchange={toggleStartup}> Iniciar con Windows (desactivado por defecto)</label>
  <div class="muted small">TempPanel sigue monitorizando minimizado (con el motor de interfaz suspendido:
    menos RAM). Cerrar la ventana minimiza a la bandeja; usa el menú de la bandeja para salir.</div>
</Card>

<Card title="Mini ventana flotante">
  <div class="muted small">Elige qué series dibuja el minigráfico (también conmutables con clic en la
    leyenda de la mini ventana).</div>
  <div class="toolbar" style="margin-top:10px">
    {#each MINI_KEYS as m (m.k)}
      <label class="chip" class:on={mini[m.k]} id="chip-mini-{m.k}">
        <input type="checkbox" bind:checked={mini[m.k]} onchange={postMini}>
        <span class="dotchip" style="background:{m.c}"></span>{m.l}
      </label>
    {/each}
  </div>
  <label class="fld"><input type="checkbox" id="mini-expanded" bind:checked={mini.expanded}
    onchange={postMini}> Abrir en tamaño ampliado (con chips y más gráfico)</label>
  <div class="toolbar" style="margin-top:6px">
    <button id="btn-mini-open" class="btn ghost" onclick={cmd.openMini}>🗔 Abrir mini ventana</button>
    <button id="btn-mini-reset-pos" class="btn ghost" onclick={cmd.resetMiniPos}>Centrar de nuevo</button>
  </div>
  <div class="muted small">La posición de la mini ventana se guarda sola. Botón ⤢ dentro de ella para
    ampliar/reducir.</div>
</Card>

<Card title="Apariencia">
  <label class="fld">Transparencia del cristal:
    <input id="cfg-alpha" type="range" min="10" max="60" step="2"
           value={Math.round(app.uiAlpha * 100)} oninput={alphaInput}>
    <span class="muted small">{Math.round(app.uiAlpha * 100)} % (solo interfaz; no cambia ninguna lectura)</span>
  </label>
  <label class="fld"><input id="cfg-immersive" type="checkbox" checked={!!app.immersive}
    onchange={() => { app.immersive = !app.immersive; saveUiPrefs(); }}> Modo inmersivo al abrir (la interfaz se aparta; atajo: F)</label>
  <div class="muted small">El fondo animado cambia de color con la temperatura del equipo y el estado de
    la batería: es decorativo, no un dato. La transparencia real la dibuja Windows 11; con el modo
    inmersivo las auras ganan protagonismo.</div>
</Card>

<Card title="Ajustes">
  <button id="btn-reset" class="btn ghost" onclick={resetAll}>Restablecer ajustes</button>
  <div class="muted small">Vuelve a los valores predeterminados.</div>
  <button id="btn-logs" class="btn ghost" style="margin-top:8px" onclick={cmd.openLogs}>📂 Abrir carpeta de registros</button>
</Card>

<Card title="Permisos">
  <Kv rows={[{ k: "Modo actual", v: app.snap?.meta?.isAdmin ? "Administrador (elevado)" : "Usuario estándar" }]} />
  <button id="btn-admin" class="btn ghost" onclick={restartAdmin}>Reiniciar como administrador</button>
  <div class="muted small">Algunos sensores requieren permisos elevados. Solo úsalo si faltan lecturas
    y entiendes por qué.</div>
</Card>

<Card title="Privacidad y archivos locales">
  <div class="muted">TempPanel funciona 100 % en local. No envía datos a Internet, no incluye analítica
    ni publicidad.<br>
    Archivos locales: <code>%AppData%\TempPanel\config.json</code> (ajustes) y
    <code>%AppData%\TempPanel\logs\</code> (errores).<br>
    El historial solo se guarda en disco si usas «Exportar CSV» o «Guardar registro de sesión»
    (CSV siempre en °C).</div>
</Card>

<Card title="Versión y dependencias">
  <Kv rows={verRows} />
  <div class="toolbar" style="margin-top:8px">
    <button id="btn-check-upd" class="btn ghost" onclick={cmd.checkUpdates}>↻ Buscar actualizaciones</button>
    {#if app.update.showDl}
      <button id="btn-dl-upd" class="btn" onclick={() => {
        if (confirm(`Se descargará TempPanel ${app.update.latest} y se abrirá su instalador. ¿Continuar?`))
          cmd.downloadUpdate(app.update.url, app.update.latest);
      }}>⬇ Descargar e instalar</button>
    {/if}
    {#if app.update.showRetry}
      <button id="btn-retry-upd" class="btn" onclick={cmd.retryLaunch}>↻ Reintentar abrir instalador</button>
    {/if}
  </div>
  <div id="upd-status" class="muted small">{app.update.text}</div>
  <label class="fld"><input type="checkbox" id="cfg-auto-upd" bind:checked={app.cfg.autoCheckUpdates}
    onchange={() => cmd.setAutoUpdates(!!app.cfg.autoCheckUpdates)}> Comprobar automáticamente al abrir
    (usa Internet solo para esto)</label>
</Card>
