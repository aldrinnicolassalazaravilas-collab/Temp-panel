<script>
  import { app, seriesOf, timesOf } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Chart from "../components/Chart.svelte";
  import Stats from "../components/Stats.svelte";
  import { nf, txt } from "../lib/format.js";

  let s = $derived(app.snap);
  let vals = $derived(seriesOf("ramUsed", 300));
  let times = $derived(timesOf(300));
</script>

{#if s}
  <div class="grid cards-4" id="ram-cards">
    <Card title="RAM total">
      <div class="big">{#if s.memory.totalMb != null}{nf(Math.round(s.memory.totalMb))} <span class="unit">MB</span>{:else}No disponible{/if}</div>
    </Card>
    <Card title="RAM usada">
      <div class="big">{#if s.memory.usedMb != null}{nf(Math.round(s.memory.usedMb))} <span class="unit">MB</span>{:else}No disponible{/if}</div>
    </Card>
    <Card title="RAM disponible">
      <div class="big">{#if s.memory.availableMb != null}{nf(Math.round(s.memory.availableMb))} <span class="unit">MB</span>{:else}No disponible{/if}</div>
    </Card>
    <Card title="Uso">
      <div class="big">{#if s.memory.percentUsed != null}{s.memory.percentUsed.toFixed(1)} <span class="unit">%</span>{:else}No disponible{/if}</div>
      <div class="muted small">{txt(s.memory.source)}</div>
    </Card>
  </div>

  <Card title="Uso de memoria">
    <Chart values={vals} times={times} unit="MB" color="#a78bfa" height={200} />
    <Stats values={vals} unit="MB" />
  </Card>
{:else}
  <Card title="Memoria RAM"><div class="muted">Esperando la primera lectura…</div></Card>
{/if}
