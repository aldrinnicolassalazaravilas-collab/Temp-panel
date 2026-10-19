<script>
  // Línea de mín/máx/promedio de una serie (datos reales, nunca estimaciones).
  import { statsOf } from "../lib/chart.js";
  let { values = [], unit = "", digits = 1 } = $props();
  let st = $derived(statsOf(values));
</script>

<div class="stats">
  {#if st}
    <span>Mín: <b>{st.min.toFixed(digits)} {unit}</b></span>
    <span>Máx: <b>{st.max.toFixed(digits)} {unit}</b></span>
    <span>Promedio: <b>{st.avg.toFixed(digits)} {unit}</b></span>
    <span class="muted">{st.n} lecturas</span>
  {:else}
    <span>Mín: — · Máx: — · Promedio: —</span>
  {/if}
</div>
