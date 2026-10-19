<script>
  import { app, seriesOf, timesOf, clearHistory, exportCsv, exportSession, toast } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Chart from "../components/Chart.svelte";
  import Stats from "../components/Stats.svelte";
  import { unitSym, toUnit, isNil } from "../lib/format.js";

  const METRICS = [
    { id: "cpuTemp", label: "Temperatura CPU", color: "#22d3ee" },
    { id: "cpuLoad", label: "Carga CPU (%)", color: "#4f8cff" },
    { id: "ramUsed", label: "RAM usada (MB)", color: "#a78bfa" },
    { id: "gpuTemp", label: "Temperatura GPU", color: "#f472b6" },
    { id: "batPct", label: "Nivel de batería (%)", color: "#34d399" }
  ];
  const RANGES = [
    { s: 60, label: "1 min" }, { s: 300, label: "5 min" },
    { s: 900, label: "15 min" }, { s: 1800, label: "30 min" }
  ];

  let metric = $state("cpuTemp");
  let range = $state(300);
  let unit = $derived(app.cfg.unit);

  let raw = $derived(seriesOf(metric, range));
  let isTemp = $derived(metric === "cpuTemp" || metric === "gpuTemp");
  let data = $derived(isTemp && unit === "F" ? raw.map((v) => (isNil(v) ? null : (v * 9) / 5 + 32)) : raw);
  let times = $derived(timesOf(range));
  let u = $derived(isTemp ? unitSym(unit) : metric === "ramUsed" ? "MB" : "%");
  let color = $derived((METRICS.find((m) => m.id === metric) || {}).color || "#22d3ee");
  let warn = $derived(metric === "cpuLoad" ? 80 : metric === "cpuTemp" ? toUnit(app.cfg.cpuWarnTemp, unit)
    : metric === "gpuTemp" ? toUnit(app.cfg.gpuWarnTemp, unit) : null);
  let crit = $derived(metric === "cpuLoad" ? 95 : metric === "cpuTemp" ? toUnit(app.cfg.cpuCritTemp, unit)
    : metric === "gpuTemp" ? toUnit(app.cfg.gpuCritTemp, unit) : null);
  let title = $derived(`${(METRICS.find((m) => m.id === metric) || {}).label} — últimos ${(RANGES.find((r) => r.s === range) || {}).label}`);

  function clear() {
    clearHistory();
    toast("Historial en memoria limpiado");
  }
</script>

<Card>
  <div class="toolbar">
    <span class="muted small">Rango:</span>
    {#each RANGES as r (r.s)}
      <button class="chip" class:on={range === r.s} onclick={() => (range = r.s)}>{r.label}</button>
    {/each}
    <span class="muted small" style="margin-left:8px">Métrica:</span>
    {#each METRICS as m (m.id)}
      <button class="chip" class:on={metric === m.id} onclick={() => (metric = m.id)}>
        <span class="dotchip" style="background:{m.color}"></span>{m.label}
      </button>
    {/each}
  </div>
  <div class="toolbar" style="margin-top:10px">
    <button id="btn-csv" class="btn" onclick={exportCsv}>⬇ Exportar CSV</button>
    <button id="btn-log" class="btn ghost" onclick={exportSession}>💾 Guardar registro de sesión</button>
    <button id="btn-clear-hist" class="btn ghost" onclick={clear}>🗑 Limpiar</button>
  </div>
</Card>

<Card title={title}>
  <Chart values={data} times={times} unit={u} color={color} warn={warn} crit={crit} height={260} />
  <Stats values={data} unit={u} />
  <div class="muted small">Historial solo en memoria (máx. 30 min a 2 s ≈ 900 puntos). No se escribe nada en
    disco sin tu consentimiento. El CSV siempre va en °C.</div>
</Card>
