<script>
  // Tarjeta de cristal con "foco" que sigue al cursor (interactivo).
  let { title = "", cls = "", children } = $props();
  let el = $state(null);
  function spotlight(e) {
    if (!el) return;
    const r = el.getBoundingClientRect();
    el.style.setProperty("--mx", (((e.clientX - r.left) / r.width) * 100).toFixed(1) + "%");
    el.style.setProperty("--my", (((e.clientY - r.top) / r.height) * 100).toFixed(1) + "%");
  }
</script>

<!-- El resaltado que sigue al cursor es decorativo: no cambia el significado del contenido. -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="card {cls}" bind:this={el} onmousemove={spotlight}>
  {#if title}<h3>{title}</h3>{/if}
  {#if children}{@render children()}{/if}
</div>
