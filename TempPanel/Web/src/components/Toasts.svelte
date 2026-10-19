<script>
  import { app } from "../lib/state.svelte.js";
</script>

<div id="toasts" aria-live="polite">
  {#each app.toasts as t (t.id)}
    <div class="toast {t.kind}">{t.text}</div>
  {/each}
</div>
