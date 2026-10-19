<script>
  // Lista clave/valor. `rows` = [{ k, v, cls }]
  let { rows = [] } = $props();
</script>

<div class="kv">
  {#each rows as r, i (i)}
    <div class="k">{r.k}</div>
    <div class={r.cls || ""}>{r.v}</div>
  {/each}
</div>
