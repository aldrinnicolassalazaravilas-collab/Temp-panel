<script>
  import { app } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Kv from "../components/Kv.svelte";
  import { degT, txt } from "../lib/format.js";

  let s = $derived(app.snap);
  let unit = $derived(app.cfg.unit);

  let extraRows = $derived.by(() => {
    if (!s) return null;
    const names = s.extraTempNames || [], vals = s.extraTemps || [];
    if (!names.length) return null;
    return names.map((n, i) => ({ k: txt(n), v: vals[i] != null ? degT(vals[i], unit) : "No disponible" }));
  });

  let storageRows = $derived.by(() => {
    if (!s) return null;
    const st = s.storage || [];
    if (!st.length) return null;
    return st.map((d) => ({ k: d.name, v: d.tempC != null ? degT(d.tempC, unit) : "No disponible (sensor no expuesto)" }));
  });
</script>

{#if s}
  <div class="grid cards-3" id="fan-cards">
    {#if (s.fans || []).length}
      {#each s.fans as f, i (i)}
        <Card title="Ventilador · {txt(f.name)}">
          <div class="big">
            {#if f.rpm != null}{Math.round(f.rpm)} <span class="unit">RPM</span>{:else}No disponible{/if}
          </div>
          <div class="muted small">{txt(f.hardware)}</div>
        </Card>
      {/each}
    {:else}
      <Card title="Ventiladores">
        <div class="big">No disponible</div>
        <div class="muted small">El EC del portátil no expone RPM al sistema. Es habitual en IdeaPad Slim.</div>
      </Card>
    {/if}
  </div>

  <Card title="Temperaturas accesibles">
    {#if extraRows}<Kv rows={extraRows} />
    {:else}<div class="muted">No hay sensores de temperatura auxiliares accesibles.</div>{/if}
  </Card>

  <Card title="Almacenamiento">
    {#if storageRows}<Kv rows={storageRows} />
    {:else}<div class="muted">No se detectaron unidades con sensor accesible.</div>{/if}
  </Card>
{:else}
  <Card title="Refrigeración"><div class="muted">Esperando la primera lectura…</div></Card>
{/if}
