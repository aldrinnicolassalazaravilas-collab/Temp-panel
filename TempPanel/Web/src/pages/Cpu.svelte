<script>
  import { app, cmd, slopePerMin } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Kv from "../components/Kv.svelte";
  import BarList from "../components/BarList.svelte";
  import { degT, txt, avg, shortName, isNil, toUnit, unitSym } from "../lib/format.js";

  let s = $derived(app.snap);
  let unit = $derived(app.cfg.unit);

  let trend = $derived.by(() => {
    const sl = slopePerMin("cpuTemp", 300, (v) => toUnit(v, unit));
    if (sl == null) return "Reuniendo datos…";
    const arrow = sl > 0.05 ? "▲" : sl < -0.05 ? "▼" : "▬";
    return `${arrow} ${sl > 0 ? "+" : ""}${sl.toFixed(2)} ${unitSym(unit)}/min (observado)`;
  });

  // Lista ligera que viaja siempre (sensorOptions); respaldo: tabla completa.
  let sensorOpts = $derived.by(() => {
    if (!s) return [];
    if (s.sensorOptions && s.sensorOptions.length) return s.sensorOptions;
    return (s.allSensors || [])
      .filter((x) => x.hardwareType === "Cpu" && x.type === "Temperature")
      .map((x) => ({ identifier: x.identifier || x.name, name: x.name, value: x.value }));
  });

  let headRows = $derived.by(() => {
    if (!s) return [];
    const c = s.cpu;
    return [
      { k: "Procesador", v: txt(c.processorName) },
      { k: "Núcleos", v: c.coresPhysical != null ? `${c.coresPhysical} físicos / ${c.coresLogical ?? "?"} lógicos` : "No disponible" },
      { k: "Temperatura paquete", v: degT(c.packageTemp, unit) },
      { k: "Media de núcleos (calculada)", v: c.coreTempAvg != null ? degT(c.coreTempAvg, unit) : "No disponible" },
      { k: "Rango entre núcleos", v: c.coreTempMin != null && c.coreTempMax != null ? `${degT(c.coreTempMin, unit)} – ${degT(c.coreTempMax, unit)}` : "No disponible" },
      { k: "Tendencia (5 min)", v: trend },
      { k: "Carga total", v: c.totalLoad != null ? c.totalLoad.toFixed(1) + " %" : "No disponible" },
      { k: "Frecuencia media", v: c.clockAvgMhz != null ? Math.round(c.clockAvgMhz) + " MHz" : "No disponible" },
      { k: "Potencia paquete", v: c.power != null ? c.power.toFixed(2) + " W" : "No disponible (sensor no expuesto)" },
      { k: "Límite térmico (TjMax)", v: c.tjMaxC != null ? degT(c.tjMaxC, unit) + " (del procesador)" : "No disponible" },
      { k: "Limitación térmica", v: c.throttling ? "⚠ Detectada" : "No detectada", cls: c.throttling ? "warn-t" : "" }
    ];
  });

  let sessionRows = $derived.by(() => {
    if (!s) return [];
    const sess = s.session || {};
    const c = s.cpu;
    let mx = null, mi = -1, mn = null, ni = -1;
    (c.coreTemps || []).forEach((v, i) => {
      if (v == null) return;
      if (mx === null || v > mx) { mx = v; mi = i; }
      if (mn === null || v < mn) { mn = v; ni = i; }
    });
    const nameOf = (i) => (c.coreTempNames && c.coreTempNames[i]) ? shortName(c.coreTempNames[i]) : "Núcleo " + i;
    return [
      { k: "Temp. mínima (sesión)", v: degT(sess.cpuTempMin, unit) },
      { k: "Temp. máxima (sesión)", v: degT(sess.cpuTempMax, unit) },
      { k: "Temp. promedio (sesión)", v: degT(sess.cpuTempAvg, unit) },
      { k: "Núcleo más caliente", v: mx !== null ? `${nameOf(mi)} · ${degT(mx, unit)}` : "—" },
      { k: "Núcleo más frío", v: mn !== null ? `${nameOf(ni)} · ${degT(mn, unit)}` : "—" },
      { k: "Carga máx. (sesión)", v: sess.cpuLoadMax != null ? sess.cpuLoadMax.toFixed(1) + " %" : "No disponible" },
      { k: "Carga promedio (sesión)", v: sess.cpuLoadAvg != null ? sess.cpuLoadAvg.toFixed(1) + " %" : "No disponible" },
      { k: "Muestras", v: sess.samples != null ? String(sess.samples) : "—" }
    ];
  });

  let coreTemps = $derived((s?.cpu?.coreTemps || []).map((v) => (isNil(v) ? null : toUnit(v, unit))));
  let tjRows = $derived.by(() => {
    if (!s) return null;
    const d = s.cpu.tjMaxDistances || [], n = s.cpu.tjMaxNames || [];
    if (!d.length) return null;
    return n.map((nm, i) => ({ k: nm, v: d[i] != null ? degT(d[i], unit) + " de margen" : "No disponible" }));
  });
</script>

{#if s}
  <Card title="Procesador">
    <Kv rows={headRows} />
    <label class="fld">Sensor principal de temperatura:
      <select id="cpu-sensor-select" value={s.meta?.preferredCpuSensor || "auto"}
              onchange={(e) => cmd.setCpuSensor(e.currentTarget.value)}>
        <option value="auto">Automático (Package)</option>
        {#each sensorOpts as o (o.identifier || o.name)}
          <option value={o.identifier || o.name}>
            {o.name} — {o.value != null ? degT(o.value, unit) + " (sensor)" : "sin lectura"}
          </option>
        {/each}
      </select>
    </label>
    <div class="muted small" id="cpu-source-note">
      {s.cpu.packageTempSource ? "Sensor seleccionado: " + s.cpu.packageTempSource : "Sin sensor de temperatura de CPU disponible."}
    </div>
  </Card>

  <Card title="Sesión (desde que se abrió la app)">
    <Kv rows={sessionRows} />
    <div class="muted small">Máximos y promedios de la sesión. No confundir con las lecturas actuales.</div>
  </Card>

  <div class="grid cards-3" id="cpu-cards">
    <Card title="Temperatura">
      <div class="big">{degT(s.cpu.packageTemp, unit)}</div>
      <div class="muted small">Paquete</div>
    </Card>
    <Card title="Carga">
      <div class="big">{#if s.cpu.totalLoad != null}{s.cpu.totalLoad.toFixed(1)} <span class="unit">%</span>{:else}No disponible{/if}</div>
      <div class="muted small">Total</div>
    </Card>
    <Card title="Frecuencia media">
      <div class="big">
        {#if avg(s.cpu.coreClocks) != null}{Math.round(avg(s.cpu.coreClocks))} <span class="unit">MHz</span>{:else}No disponible{/if}
      </div>
      <div class="muted small">Promedio de núcleos</div>
    </Card>
  </div>

  <Card title="Temperaturas por núcleo">
    <BarList values={coreTemps} names={s.cpu.coreTempNames} unit={unitSym(unit)}
             warn={toUnit(app.cfg.cpuWarnTemp, unit)} crit={toUnit(app.cfg.cpuCritTemp, unit)} />
  </Card>

  <Card title="Carga por núcleo e hilo">
    <BarList values={s.cpu.coreLoads} names={s.cpu.coreLoadNames} unit="%" warn={80} crit={95} percentBase={100} />
    <div class="muted small">Los núcleos P muestran sus 2 hilos; los núcleos E, una entrada por núcleo.</div>
  </Card>

  <Card title="Margen térmico (distancia a TjMax)">
    {#if tjRows}
      <Kv rows={tjRows} />
    {:else}
      <Kv rows={[{ k: "Margen térmico", v: "No disponible (el firmware no lo expone en este modo)." }]} />
    {/if}
    <div class="muted small">Margen hasta el límite térmico del procesador. Solo visible si el
      firmware lo expone (suele requerir administrador).</div>
  </Card>
{:else}
  <Card title="CPU"><div class="muted">Esperando la primera lectura…</div></Card>
{/if}
