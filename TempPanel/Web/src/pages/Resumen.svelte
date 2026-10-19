<script>
  import { app, copySummary, seriesOf, timesOf } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Gauge from "../components/Gauge.svelte";
  import Ring from "../components/Ring.svelte";
  import Sparkline from "../components/Sparkline.svelte";
  import Chart from "../components/Chart.svelte";
  import Stats from "../components/Stats.svelte";
  import Kv from "../components/Kv.svelte";
  import { degT, txt, nf, tempClass, tempColor, batTrendLabel, isNil } from "../lib/format.js";

  let s = $derived(app.snap);
  let unit = $derived(app.cfg.unit);
  const convT = (arr) => (unit === "F" ? arr.map((v) => (isNil(v) ? null : (v * 9) / 5 + 32)) : arr);

  let spCpu = $derived(convT(seriesOf("cpuTemp", 300)));
  let spLoad = $derived(seriesOf("cpuLoad", 300));
  let spRam = $derived(seriesOf("ramUsed", 300));
  let spBat = $derived(seriesOf("batPct", 300));
  let chCpu = $derived(convT(seriesOf("cpuTemp", 120)));
  let chLoad = $derived(seriesOf("cpuLoad", 120));
  let chTimes = $derived(timesOf(120));

  const batRingColor = (p) => (p <= 15 ? "#f87171" : p <= 35 ? "#fbbf24" : "#34d399");

  let sysRows = $derived.by(() => {
    if (!s) return [];
    const m = s.meta || {};
    return [
      { k: "Procesador", v: txt(m.cpuName) },
      { k: "Memoria instalada", v: m.installedRamMb != null ? nf(Math.round(m.installedRamMb)) + " MB" : "No disponible" },
      { k: "Tiempo de app", v: txt(m.appUptime) },
      { k: "Equipo encendido", v: m.bootTimeLocal ? txt(m.bootTimeLocal) : "No disponible" },
      { k: "Frecuencia", v: `cada ${(m.refreshMs || app.cfg.refreshMs) / 1000} s` },
      { k: "Última lectura", v: txt(m.lastReadLocal) },
      { k: "Permisos", v: m.isAdmin ? "Administrador" : "Usuario estándar" },
      { k: "Memoria de la app", v: m.appMemoryMb != null ? m.appMemoryMb.toFixed(1) + " MB (proceso principal)" : "No disponible" }
    ];
  });
</script>

{#if s}
  <div class="rowbar">
    <button id="btn-copy" class="btn ghost" title="Copiar resumen en texto" onclick={copySummary}>⧉ Copiar resumen</button>
  </div>

  <div class="grid cards-4" id="summary-cards">
    <Card cls={s.cpu.packageTemp != null ? (tempClass(s.cpu.packageTemp, app.cfg.cpuWarnTemp, app.cfg.cpuCritTemp) === "crit-t" ? "st-crit" : tempClass(s.cpu.packageTemp, app.cfg.cpuWarnTemp, app.cfg.cpuCritTemp) === "warn-t" ? "st-warn" : "st-ok") : "st-ok"}
          title="CPU · {txt(s.cpu.packageTempSource || 'temperatura')}">
      <div class="gaugewrap">
        {#if s.cpu.packageTemp != null}
          <Gauge frac={Math.min(1, s.cpu.packageTemp / app.cfg.cpuCritTemp)}
                 color={tempColor(s.cpu.packageTemp, app.cfg.cpuWarnTemp, app.cfg.cpuCritTemp)}
                 label={degT(s.cpu.packageTemp, app.cfg.unit)} />
        {/if}
        <div class="big {tempClass(s.cpu.packageTemp, app.cfg.cpuWarnTemp, app.cfg.cpuCritTemp)}">
          {s.cpu.packageTemp != null ? degT(s.cpu.packageTemp, app.cfg.unit) : "No disponible"}
        </div>
      </div>
      <Sparkline values={spCpu} color={tempColor(s.cpu.packageTemp, app.cfg.cpuWarnTemp, app.cfg.cpuCritTemp)} />
      <div class="muted small">{txt(s.cpu.processorName)}</div>
    </Card>

    <Card cls={s.cpu.totalLoad != null && s.cpu.totalLoad >= 90 ? "st-warn" : "st-ok"} title="Carga CPU">
      <div class="big">
        {#if s.cpu.totalLoad != null}{s.cpu.totalLoad.toFixed(1)} <span class="unit">%</span>{:else}No disponible{/if}
      </div>
      <Sparkline values={spLoad} color="#4f8cff" />
      <div class="muted small">{s.cpu.throttling ? "⚠ posible limitación térmica" : "Carga total"}</div>
    </Card>

    <Card cls={s.memory.percentUsed != null && s.memory.percentUsed >= 90 ? "st-warn" : "st-ok"} title="RAM en uso">
      <div class="big">
        {#if s.memory.percentUsed != null}{s.memory.percentUsed.toFixed(1)} <span class="unit">%</span>{:else}No disponible{/if}
      </div>
      <Sparkline values={spRam} color="#a78bfa" />
      <div class="muted small">
        {s.memory.usedMb != null ? `${Math.round(s.memory.usedMb)} / ${Math.round(s.memory.totalMb)} MB` : "Sin datos de memoria"}
      </div>
    </Card>

    <Card cls={s.battery.present && s.battery.percent != null && s.battery.percent <= 15 ? "st-warn" : "st-ok"} title="Batería">
      <div class="gaugewrap">
        {#if s.battery.present && s.battery.percent != null}
          <Ring frac={s.battery.percent / 100} color={batRingColor(s.battery.percent)}
                label={s.battery.percent.toFixed(1) + " %"} />
        {/if}
        <div class="big">
          {#if s.battery.present && s.battery.percent != null}
            {s.battery.percent.toFixed(1)} <span class="unit">%</span>
          {:else}No disponible{/if}
        </div>
      </div>
      <Sparkline values={spBat} color="#34d399" />
      <div class="muted small">{s.battery.present ? batTrendLabel(s.battery) : "Este equipo no expone batería"}</div>
    </Card>
  </div>

  <div class="grid cards-2">
    <Card title="Temperatura CPU (tiempo real)">
      <Chart values={chCpu} times={chTimes} unit={unit === "F" ? "°F" : "°C"}
             color={tempColor(s.cpu.packageTemp, app.cfg.cpuWarnTemp, app.cfg.cpuCritTemp)}
             warn={unit === "F" ? app.cfg.cpuWarnTemp * 9 / 5 + 32 : app.cfg.cpuWarnTemp}
             crit={unit === "F" ? app.cfg.cpuCritTemp * 9 / 5 + 32 : app.cfg.cpuCritTemp}
             height={180} />
      <Stats values={chCpu} unit={unit === "F" ? "°F" : "°C"} />
    </Card>
    <Card title="Carga CPU (tiempo real)">
      <Chart values={chLoad} times={chTimes} unit="%" color="#4f8cff" warn={80} crit={95} height={180} />
      <Stats values={chLoad} unit="%" />
    </Card>
  </div>

  <Card title="Sistema">
    <Kv rows={sysRows} />
  </Card>
{:else}
  <Card title="Esperando la primera lectura">
    <div class="muted">La app nativa todavía no ha enviado datos. Si esto dura más de unos segundos,
      revisa el estado de la conexión abajo a la izquierda.</div>
  </Card>
{/if}
