<script>
  // Barras por núcleo (temperatura o carga). Sin lecturas no se inventa nada.
  import { shortName, isNil } from "../lib/format.js";
  let { values = [], names = [], unit = "", warn = 80, crit = 95, fallback = "Núcleo", percentBase = null } = $props();

  let rows = $derived.by(() => {
    const arr = values || [];
    if (!arr.length) return null;
    const vals = arr.filter((v) => !isNil(v));
    const max = vals.length ? Math.max(...vals) : 1;
    return arr.map((v, i) => {
      const label = names && names[i] ? shortName(names[i]) : `${fallback} ${i}`;
      if (isNil(v)) return { label, v: null, pct: 0, col: "" };
      const base = percentBase != null ? percentBase : max;
      const pct = Math.min(100, (v / (base || 1)) * 100);
      const col = v >= crit ? "linear-gradient(90deg,#f87171,#ef4444)"
        : v >= warn ? "linear-gradient(90deg,#fbbf24,#f59e0b)"
          : "linear-gradient(90deg,#2f7bff,#22d3ee)";
      return { label, v, pct, col };
    });
  });
</script>

{#if rows}
  <div class="bars">
    {#each rows as r, i (i)}
      <div class="bar-row">
        <span class="bar-name">{r.label}</span>
        <div class="bar">
          <div style="width:{r.pct.toFixed(0)}%;background:{r.col}"></div>
        </div>
        <span class="bar-val">{r.v == null ? '—' : r.v.toFixed(1) + " " + unit}</span>
      </div>
    {/each}
  </div>
{:else}
  <span class="nd">Sin lecturas por núcleo disponibles.</span>
{/if}
