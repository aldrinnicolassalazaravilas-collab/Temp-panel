<script>
  import { app } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Kv from "../components/Kv.svelte";
  import { degT, txt, tempClass } from "../lib/format.js";

  let s = $derived(app.snap);
  let unit = $derived(app.cfg.unit);
</script>

{#if s}
  <div class="grid cards-4" id="gpu-cards">
    <Card title="GPU"><div class="big accent">{txt(s.gpu.name)}</div>
      <div class="muted small">{s.gpu.available ? "Detectada" : "No detectada"}</div>
    </Card>
    <Card title="Temperatura">
      <div class="big {tempClass(s.gpu.temp, app.cfg.gpuWarnTemp, app.cfg.gpuCritTemp)}">{degT(s.gpu.temp, unit)}</div>
      <div class="muted small">{txt(s.gpu.tempSource)}</div>
    </Card>
    <Card title="Carga">
      <div class="big">{#if s.gpu.load != null}{s.gpu.load.toFixed(1)} <span class="unit">%</span>{:else}No disponible{/if}</div>
      <div class="muted small">Núcleo GPU</div>
    </Card>
    <Card title="Frecuencia">
      <div class="big">{#if s.gpu.clock != null}{Math.round(s.gpu.clock)} <span class="unit">MHz</span>{:else}No disponible{/if}</div>
      <div class="muted small">{s.gpu.memoryUsedMb != null ? `Memoria: ${Math.round(s.gpu.memoryUsedMb)} MB` : ""}</div>
    </Card>
  </div>

  <Card title="Estado">
    <Kv rows={[{ k: "Estado", v: txt(s.gpu.note || (s.gpu.available ? "Sensores GPU disponibles arriba." : "Sin GPU detectable.")) }]} />
  </Card>

  <Card title="Sesión GPU">
    <Kv rows={[
      { k: "Temp. mínima (sesión)", v: degT(s.session?.gpuTempMin, unit) },
      { k: "Temp. máxima (sesión)", v: degT(s.session?.gpuTempMax, unit) }
    ]} />
  </Card>
{:else}
  <Card title="GPU"><div class="muted">Esperando la primera lectura…</div></Card>
{/if}
