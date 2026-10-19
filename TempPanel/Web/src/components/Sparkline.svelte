<script>
  // Minigráfico suave con relleno degradado y punto actual palpitante.
  import { hexA } from "../lib/format.js";
  let { values = [], color = "#22d3ee", height = 42 } = $props();
  const W = 220, H = 44, PAD = 5;

  let geo = $derived.by(() => {
    const vals = values || [];
    const pts = vals.filter((v) => v !== null && v !== undefined);
    if (pts.length < 2) return null;
    const min = Math.min(...pts), max = Math.max(...pts);
    const rg = max - min < 1e-6 ? 1 : max - min;
    const X = (i) => PAD + ((W - 2 * PAD) * i) / Math.max(1, vals.length - 1);
    const Y = (v) => H - PAD - (H - 2 * PAD) * ((v - min) / rg);
    const coords = [];
    vals.forEach((v, i) => { if (v !== null && v !== undefined) coords.push([X(i), Y(v)]); });
    if (coords.length < 2) return null;
    let d = `M ${coords[0][0].toFixed(1)} ${coords[0][1].toFixed(1)}`;
    for (let i = 0; i < coords.length - 1; i++) {
      const p0 = coords[Math.max(0, i - 1)], p1 = coords[i], p2 = coords[i + 1], p3 = coords[Math.min(coords.length - 1, i + 2)];
      d += ` C ${(p1[0] + (p2[0] - p0[0]) / 6).toFixed(1)} ${(p1[1] + (p2[1] - p0[1]) / 6).toFixed(1)},` +
        ` ${(p2[0] - (p3[0] - p1[0]) / 6).toFixed(1)} ${(p2[1] - (p3[1] - p1[1]) / 6).toFixed(1)},` +
        ` ${p2[0].toFixed(1)} ${p2[1].toFixed(1)}`;
    }
    const last = coords[coords.length - 1];
    return { d, fill: `${d} L ${last[0].toFixed(1)} ${H} L ${coords[0][0].toFixed(1)} ${H} Z`, last };
  });
</script>

{#if geo}
  <svg viewBox="0 0 {W} {H}" class="spark" preserveAspectRatio="none" style="height:{height}px" aria-hidden="true">
    <path d={geo.fill} fill={hexA(color, 0.16)} stroke="none"/>
    <path d={geo.d} fill="none" stroke={color} stroke-width="2" stroke-linejoin="round"
          stroke-linecap="round" vector-effect="non-scaling-stroke"/>
    <circle class="pulse-dot" cx={geo.last[0]} cy={geo.last[1]} r="2.6" fill={color}/>
  </svg>
{/if}
