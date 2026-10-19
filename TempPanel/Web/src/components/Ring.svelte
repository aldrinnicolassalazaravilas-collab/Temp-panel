<script>
  // Anillo de progreso (batería, uso). También animado por CSS.
  let { frac = 0, color = "#22d3ee", label = "" } = $props();
  const C = 2 * Math.PI * 54;
  let f = $derived(Math.max(0, Math.min(1, frac || 0)));
</script>

<svg viewBox="0 0 130 130" class="ring">
  <circle cx="65" cy="65" r="54" fill="none" stroke="rgba(148,163,184,.16)" stroke-width="12"/>
  <circle class="arc" cx="65" cy="65" r="54" fill="none" stroke={color} stroke-width="12"
          stroke-linecap="round" transform="rotate(-90 65 65)"
          stroke-dasharray="{(f * C).toFixed(1)} {C.toFixed(1)}"/>
  <text x="65" y="71" text-anchor="middle" fill="#eef4ff" font-size="19" font-weight="bold">{label}</text>
</svg>
