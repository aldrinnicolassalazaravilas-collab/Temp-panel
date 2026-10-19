<script>
  // Gráfica interactiva: se dibuja sola, se anima al abrirse, muestra una guía con
  // hora y valor exacto al pasar el ratón y se puede FIJAR un punto con un clic.
  import { onMount } from "svelte";
  import { hexA } from "../lib/format.js";
  import { traceSmooth, segments, timeLabel } from "../lib/chart.js";

  let {
    values = [], times = [], unit = "", color = "#22d3ee", warn = null, crit = null,
    height = 180, animate = true
  } = $props();

  const PAD_X = 10, PAD_TOP = 10, PAD_BOTTOM = 12;

  let wrap = $state(null), canvas = $state(null);
  let w = $state(0), h = $state(0);
  let hover = $state(-1), pin = $state(-1);
  let shown = $state(1);

  function scaleOf(vals, width, hgt) {
    const pts = (vals || []).filter((v) => v !== null && v !== undefined);
    if (pts.length < 2 || width <= 0) return null;
    let min = Math.min(...pts), max = Math.max(...pts);
    if (max - min < 1e-6) { max += 1; min -= 1; }
    const pad = (max - min) * 0.16; min -= pad; max += pad;
    const n = vals.length;
    const X = (i) => PAD_X + ((width - 2 * PAD_X) * i) / Math.max(1, n - 1);
    const Y = (v) => hgt - PAD_BOTTOM - (hgt - PAD_TOP - PAD_BOTTOM) * ((v - min) / (max - min));
    return { min, max, X, Y };
  }

  let geo = $derived.by(() => scaleOf(values, w, h));

  let tipInfo = $derived.by(() => {
    if (hover < 0 || !geo || !values || !values.length) return null;
    const v = values[hover];
    if (v === null || v === undefined) return null;
    return { x: geo.X(hover), y: geo.Y(v), text: `${timeLabel(times[hover])} · ${Number(v).toFixed(1)} ${unit}` };
  });

  let pinInfo = $derived.by(() => {
    if (pin < 0 || !geo || !values || !values.length) return null;
    const v = values[pin];
    if (v === null || v === undefined) return null;
    return { x: geo.X(pin), y: geo.Y(v), text: `📌 ${timeLabel(times[pin])} · ${Number(v).toFixed(1)} ${unit}` };
  });

  function resize() {
    if (!wrap || !canvas) return;
    const dpr = window.devicePixelRatio || 1;
    w = wrap.clientWidth;
    h = wrap.clientHeight;
    canvas.width = Math.max(1, Math.round(w * dpr));
    canvas.height = Math.max(1, Math.round(h * dpr));
  }

  $effect(() => {
    if (!wrap || !canvas) return;
    resize();
    const ro = new ResizeObserver(() => resize());
    ro.observe(wrap);
    return () => ro.disconnect();
  });

  onMount(() => {
    const reduce = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    if (!animate || reduce) { shown = 1; return; }
    shown = 0;
    const t0 = performance.now();
    let raf = 0;
    const step = (t) => {
      const k = Math.min(1, (t - t0) / 520);
      shown = k * k * (3 - 2 * k);
      if (k < 1) raf = requestAnimationFrame(step); else shown = 1;
    };
    raf = requestAnimationFrame(step);
    return () => cancelAnimationFrame(raf);
  });

  $effect(() => { draw(); });

  function draw() {
    if (!canvas || !w || !h) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);

    const g = scaleOf(values, w, h);
    const col = color || "#22d3ee";
    if (!g) {
      ctx.fillStyle = "#94a7c9";
      ctx.font = '13px "Segoe UI", system-ui, sans-serif';
      ctx.fillText("Esperando datos… (sin lecturas ficticias)", 16, h / 2);
      return;
    }

    // Rejilla sutil
    ctx.strokeStyle = "rgba(148,163,184,.10)";
    ctx.lineWidth = 1;
    for (let k = 1; k <= 3; k++) {
      const gy = PAD_TOP + (h - PAD_TOP - PAD_BOTTOM) * k / 4;
      ctx.beginPath(); ctx.moveTo(PAD_X, gy); ctx.lineTo(w - PAD_X, gy); ctx.stroke();
    }

    // Umbrales informativos (discontinuos)
    [[warn, "rgba(251,191,36,.55)"], [crit, "rgba(248,113,113,.6)"]].forEach(([t, c2]) => {
      if (t !== null && t !== undefined && t > g.min && t < g.max) {
        ctx.strokeStyle = c2;
        ctx.setLineDash([5, 5]);
        ctx.beginPath(); ctx.moveTo(PAD_X, g.Y(t)); ctx.lineTo(w - PAD_X, g.Y(t)); ctx.stroke();
        ctx.setLineDash([]);
      }
    });

    // Revelado progresivo al abrir la página (respeta "reducir movimiento")
    const total = values.length;
    const n = shown >= 1 ? total : Math.max(2, Math.round(total * shown));
    const series = shown >= 1 ? values : values.slice(0, n);
    const segs = segments(series, g.X, g.Y);

    // Relleno degradado bajo el tramo más largo
    const longest = segs.reduce((a, b) => (b.length > (a ? a.length : 0) ? b : a), null);
    if (longest && longest.length >= 2) {
      const grad = ctx.createLinearGradient(0, 0, 0, h);
      grad.addColorStop(0, hexA(col, 0.30));
      grad.addColorStop(1, hexA(col, 0.02));
      ctx.beginPath(); traceSmooth(ctx, longest);
      ctx.lineTo(longest[longest.length - 1][0], h);
      ctx.lineTo(longest[0][0], h);
      ctx.closePath();
      ctx.fillStyle = grad;
      ctx.fill();
    }

    // Halo + línea principal
    segs.forEach((seg) => {
      if (seg.length < 2) return;
      ctx.beginPath(); traceSmooth(ctx, seg);
      ctx.strokeStyle = hexA(col, 0.22); ctx.lineWidth = 5;
      ctx.lineJoin = "round"; ctx.lineCap = "round"; ctx.stroke();
    });
    segs.forEach((seg) => {
      if (seg.length < 2) return;
      ctx.beginPath(); traceSmooth(ctx, seg);
      ctx.strokeStyle = col; ctx.lineWidth = 2.2;
      ctx.lineJoin = "round"; ctx.lineCap = "round"; ctx.stroke();
    });

    // Punto actual con halo: "dónde estamos ahora"
    const lastSeg = segs[segs.length - 1];
    if (lastSeg && lastSeg.length && shown >= 1) {
      const [lx, ly] = lastSeg[lastSeg.length - 1];
      ctx.beginPath(); ctx.arc(lx, ly, 6, 0, Math.PI * 2); ctx.fillStyle = hexA(col, 0.22); ctx.fill();
      ctx.beginPath(); ctx.arc(lx, ly, 3.2, 0, Math.PI * 2); ctx.fillStyle = col; ctx.fill();
    }

    // Guía del ratón y punto fijado (interactivo)
    const guide = (i, color2, dash) => {
      const v = values[i];
      if (v === null || v === undefined) return;
      const x = g.X(i), y = g.Y(v);
      ctx.strokeStyle = color2;
      ctx.lineWidth = 1;
      if (dash) ctx.setLineDash([4, 4]);
      ctx.beginPath(); ctx.moveTo(x, PAD_TOP * 0.6); ctx.lineTo(x, h - PAD_BOTTOM); ctx.stroke();
      ctx.setLineDash([]);
      ctx.beginPath(); ctx.arc(x, y, 4.2, 0, Math.PI * 2);
      ctx.fillStyle = color2; ctx.fill();
      ctx.beginPath(); ctx.arc(x, y, 8, 0, Math.PI * 2);
      ctx.strokeStyle = color2; ctx.lineWidth = 1.5; ctx.stroke();
    };
    if (pin >= 0) guide(pin, "rgba(34,211,238,.85)", false);
    if (hover >= 0 && hover !== pin) guide(hover, "rgba(255,255,255,.45)", true);
  }

  function onMove(e) {
    if (!wrap || !values || values.length < 2) return;
    const r = wrap.getBoundingClientRect();
    const x = e.clientX - r.left;
    const i = Math.round(((x - PAD_X) / Math.max(1, r.width - 2 * PAD_X)) * (values.length - 1));
    hover = Math.max(0, Math.min(values.length - 1, i));
  }
  function onLeave() { hover = -1; }
  function onClick() { if (hover >= 0) pin = pin === hover ? -1 : hover; }
  function onDblClick() { pin = -1; }

  // Accesible con teclado: ← → mueven la guía, Intro fija el punto, Esc lo quita.
  function onKey(e) {
    const n = values?.length || 0;
    if (!n) return;
    const k = e.key;
    if (k === "ArrowLeft" || k === "ArrowRight") {
      e.preventDefault();
      const base = hover < 0 ? n - 1 : hover + (k === "ArrowRight" ? 1 : -1);
      hover = Math.max(0, Math.min(n - 1, base));
    } else if (k === "Enter" || k === " ") {
      e.preventDefault();
      if (hover >= 0) pin = pin === hover ? -1 : hover;
    } else if (k === "Escape") {
      pin = -1;
    }
  }
</script>

<!-- La gráfica es interactiva a propósito (guía, punto fijado y flechas del teclado). -->
<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<div class="chartwrap" bind:this={wrap} style="height:{height}px" role="img" tabindex="0"
     aria-label="Gráfica de {unit || 'valores'}: pasa el ratón o usa las flechas; Intro fija un punto"
     onmousemove={onMove} onmouseleave={onLeave} onclick={onClick} ondblclick={onDblClick}
     onkeydown={onKey}>
  <canvas bind:this={canvas}></canvas>
  {#if tipInfo}
    <div class="chart-tip" style="left:{tipInfo.x}px;top:{tipInfo.y}px">{tipInfo.text}</div>
  {/if}
  {#if pinInfo}
    <div class="chart-pin" style="left:{pinInfo.x}px;top:{pinInfo.y}px;transform:translate(-50%,10px)">{pinInfo.text}</div>
  {/if}
</div>
