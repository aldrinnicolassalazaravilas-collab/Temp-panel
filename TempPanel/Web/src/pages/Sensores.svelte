<script>
  import { app } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";

  const COLS = [
    { key: "hardware", label: "Hardware" },
    { key: "hardwareType", label: "Tipo HW" },
    { key: "type", label: "Tipo" },
    { key: "name", label: "Nombre" },
    { key: "value", label: "Valor", num: true },
    { key: "min", label: "Mín", num: true },
    { key: "max", label: "Máx", num: true }
  ];

  // La tabla completa solo llega cuando esta página está abierta (ahorro de RAM);
  // si el snapshot no la trae, se conserva la última lista recibida.
  let last = $state([]);
  $effect(() => {
    const all = app.snap?.allSensors;
    if (all && all.length) last = all;
  });

  let rows = $derived.by(() => {
    const q = (app.filter || "").toLowerCase();
    let out = last.filter((x) => !q ||
      (x.hardware + " " + x.hardwareType + " " + x.type + " " + x.name).toLowerCase().includes(q));
    const k = app.sortKey, dir = app.sortDir;
    if (k) {
      out = [...out].sort((a, b) => {
        const va = a[k], vb = b[k];
        if (va == null && vb == null) return 0;
        if (va == null) return 1;   // sin lectura, al final: no se inventa un cero
        if (vb == null) return -1;
        if (typeof va === "number" && typeof vb === "number") return (va - vb) * dir;
        return String(va).localeCompare(String(vb), "es") * dir;
      });
    }
    return out;
  });

  // v3.0.0 — virtualización: con cientos de sensores solo se montan las filas
  // visibles (menos DOM, menos RAM y desplazamiento fluido). Los huecos de
  // arriba/abajo conservan la altura real de la lista.
  const ROW_H = 34;
  const OVERSCAN = 8;
  let wrapEl = $state(null);
  let scrollTop = $state(0);
  let viewH = $state(560);
  let total = $derived(rows.length);
  let start = $derived(total === 0 ? 0
    : Math.min(total - 1, Math.max(0, Math.floor(scrollTop / ROW_H) - OVERSCAN)));
  let end = $derived(Math.min(total, start + Math.ceil(viewH / ROW_H) + OVERSCAN * 2));
  let visible = $derived(rows.slice(start, end));
  let padTop = $derived(start * ROW_H);
  let padBottom = $derived(Math.max(0, (total - end) * ROW_H));

  function onScroll(e) { scrollTop = e.currentTarget.scrollTop; }

  $effect(() => {
    if (!wrapEl) return;
    viewH = wrapEl.clientHeight || 560;
    if (typeof ResizeObserver === "undefined") return; // JSDOM no lo implementa
    const ro = new ResizeObserver(() => { viewH = wrapEl.clientHeight || 560; });
    ro.observe(wrapEl);
    return () => ro.disconnect();
  });

  // Al filtrar u ordenar se vuelve arriba para no quedar fuera de la lista.
  $effect(() => {
    app.filter; app.sortKey; app.sortDir;
    if (wrapEl) { wrapEl.scrollTop = 0; }
    scrollTop = 0;
  });

  function sortBy(key) {
    if (app.sortKey === key) app.sortDir = -app.sortDir;
    else { app.sortKey = key; app.sortDir = 1; }
  }
</script>

<Card>
  <div class="toolbar">
    <input id="sensor-filter" placeholder="Filtrar por hardware, tipo o nombre…" bind:value={app.filter} />
    <span id="sensor-count" class="muted">{rows.length} / {last.length} sensores</span>
    <span class="muted small">Clic en una columna para ordenar · la lista completa se lee solo en esta página</span>
  </div>
</Card>

<Card>
  <div class="table-wrap" bind:this={wrapEl} onscroll={onScroll}>
    <table id="sensor-table">
      <thead>
        <tr>
          {#each COLS as c (c.key)}
            <th onclick={() => sortBy(c.key)} title="Ordenar por {c.label}">
              {c.label}{#if app.sortKey === c.key}<span class="arrow">{app.sortDir > 0 ? "▲" : "▼"}</span>{/if}
            </th>
          {/each}
        </tr>
      </thead>
      <tbody>
        {#if padTop > 0}
          <tr class="spacer" aria-hidden="true"><td colspan="7" style="height:{padTop}px"></td></tr>
        {/if}
        {#each visible as x, i (start + i)}
          <tr class="row">
            <td>{x.hardware}</td>
            <td>{x.hardwareType}</td>
            <td>{x.type}</td>
            <td>{x.name}</td>
            <td>{x.value != null ? `${x.value} ${x.unit}` : "—"}</td>
            <td>{x.min != null ? x.min : "—"}</td>
            <td>{x.max != null ? x.max : "—"}</td>
          </tr>
        {/each}
        {#if padBottom > 0}
          <tr class="spacer" aria-hidden="true"><td colspan="7" style="height:{padBottom}px"></td></tr>
        {/if}
      </tbody>
    </table>
  </div>
</Card>
