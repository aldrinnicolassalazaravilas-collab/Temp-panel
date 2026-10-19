<script>
  // Medidor semicircular. La aguja es el arco: se mueve con transición CSS.
  let { frac = 0, color = "#22d3ee", label = "" } = $props();
  const LEN = 251.3; // longitud del semicírculo r=80
  let f = $derived(Math.max(0, Math.min(1, frac || 0)));
</script>

<svg viewBox="0 0 200 112" class="gauge">
  <path d="M 10 100 A 80 80 0 0 1 190 100" fill="none" stroke="rgba(148,163,184,.16)"
        stroke-width="14" stroke-linecap="round"/>
  <path class="arc" d="M 10 100 A 80 80 0 0 1 190 100" fill="none" stroke={color}
        stroke-width="14" stroke-linecap="round"
        stroke-dasharray="{(f * LEN).toFixed(1)} {LEN.toFixed(1)}"/>
  <text x="100" y="92" text-anchor="middle" fill="#eef4ff" font-size="17" font-weight="bold">{label}</text>
</svg>
