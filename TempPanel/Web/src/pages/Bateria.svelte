<script>
  import { app, seriesOf, timesOf } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Chart from "../components/Chart.svelte";
  import Stats from "../components/Stats.svelte";
  import Kv from "../components/Kv.svelte";
  import { txt, nf } from "../lib/format.js";

  let s = $derived(app.snap);
  let vals = $derived(seriesOf("batPct", 300));
  let times = $derived(timesOf(300));

  let b = $derived(s?.battery);

  let trendTxt = $derived.by(() => {
    if (!b) return "Reuniendo datos…";
    if (b.held && b.heldMinutes != null) return `Sin cambios ${Math.round(b.heldMinutes)} min`;
    if (b.trendPctPerHour != null) return `${b.trendPctPerHour > 0 ? "▲ +" : "▼ "}${b.trendPctPerHour.toFixed(2)} %/h`;
    return "Reuniendo datos…";
  });

  let chgTxt = $derived(!b ? "—" : b.charging === true ? "Cargando por red" : b.charging === false ? "No está cargando" : "No expuesto");

  let infoRows = $derived.by(() => {
    if (!b) return [];
    return [
      { k: "Alimentación externa", v: txt(b.powerLine) },
      { k: "Estado real de carga", v: chgTxt },
      { k: "Nivel (capacidades reales)", v: b.percent != null ? b.percent.toFixed(1) + " %" : "No disponible" },
      { k: "Fuente del nivel", v: txt(b.percentSource) },
      { k: "Tendencia observada", v: trendTxt + (b.held && b.heldMinutes != null ? " (el portátil mantiene el nivel: modo conservación típico)" : "") },
      { k: "Nivel de LHM (si existe)", v: b.chargeLevelPct != null ? b.chargeLevelPct.toFixed(1) + " %" : "No expuesto" },
      { k: "Capacidad de diseño", v: b.designCapacityMwh != null ? nf(Math.round(b.designCapacityMwh)) + " mWh" : "No expuesta" },
      { k: "Capacidad de carga total", v: b.fullChargeCapacityMwh != null ? nf(Math.round(b.fullChargeCapacityMwh)) + " mWh" : "No expuesta" },
      { k: "Capacidad restante", v: b.remainingCapacityMwh != null ? nf(Math.round(b.remainingCapacityMwh)) + " mWh" : "No expuesta" },
      { k: "Tasa de carga", v: b.chargeRateW != null ? b.chargeRateW.toFixed(1) + " W" : "No expuesta por el firmware" },
      { k: "Tasa de descarga", v: b.dischargeRateW != null ? b.dischargeRateW.toFixed(1) + " W" : "No disponible" },
      { k: "Autonomía estimada", v: b.remainingMinutes != null ? Math.round(b.remainingMinutes) + " min" : "No disponible" },
      { k: "Tensión", v: b.voltageV != null ? b.voltageV.toFixed(2) + " V" : "No disponible" },
      { k: "Nota", v: txt(b.note) }
    ];
  });
</script>

{#if s && b && !b.present}
  <Card title="Batería">
    <div class="muted">Este equipo no expone una batería (sobremesa o sin batería detectada).
      TempPanel no inventa un nivel: solo representa lo que Windows publica.</div>
  </Card>
{:else if s && b}
  <div class="grid cards-4" id="bat-cards">
    <Card title="Carga" cls={b.charging === true ? "charging" : ""}>
      {#if b.charging === true}
        <div class="flow" title="El firmware confirma que está cargando"></div>
      {/if}
      <div class="big">
        {#if b.present && b.percent != null}{b.percent.toFixed(1)} <span class="unit">%</span>{:else}No disponible{/if}
      </div>
      <div class="muted small">{b.percentSource ? "Fuente: " + txt(b.percentSource) : "Precisión de 1 decimal"}</div>
    </Card>
    <Card title="Estado">
      <div class="big accent">{txt(b.status)}</div>
      <div class="muted small">{chgTxt}</div>
    </Card>
    <Card title="Tendencia">
      <div class="big {b.trendPctPerHour != null && b.trendPctPerHour > 0 ? "ok-t" : ""}">{trendTxt}</div>
      <div class="muted small">Medida en esta sesión (no es una predicción)</div>
    </Card>
    <Card title="Temperatura">
      <div class="big">No disponible</div>
      <div class="muted small">Windows no expone temperatura de batería</div>
    </Card>
  </div>

  <Card title="Detalles">
    <Kv rows={infoRows} />
  </Card>

  <Card title="Carga en tiempo real">
    <Chart values={vals} times={times} unit="%" color="#34d399" height={200} />
    <Stats values={vals} unit="%" />
  </Card>
{:else}
  <Card title="Batería"><div class="muted">Esperando la primera lectura…</div></Card>
{/if}

<style>
  /* Flujo animado mientras la batería carga de verdad (decorativo). */
  .flow {
    height: 8px; margin: -4px 0 10px; border-radius: 99px;
    background: linear-gradient(90deg, transparent, #34d399, transparent);
    background-size: 200% 100%;
    animation: flow 1.6s linear infinite;
  }
  @keyframes flow { from { background-position: 120% 0 } to { background-position: -120% 0 } }
</style>
