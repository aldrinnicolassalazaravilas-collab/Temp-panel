<script>
  import { app, setPage } from "../lib/state.svelte.js";
  const items = [
    { page: "resumen", icon: "▤", label: "Resumen", key: "1" },
    { page: "cpu", icon: "◉", label: "CPU", key: "2" },
    { page: "gpu", icon: "▣", label: "GPU", key: "3" },
    { page: "ram", icon: "▦", label: "Memoria RAM", key: "4" },
    { page: "bateria", icon: "⬢", label: "Batería", key: "5" },
    { page: "refrigeracion", icon: "❄", label: "Refrigeración", key: "6" },
    { page: "sensores", icon: "☰", label: "Todos los sensores", key: "7" },
    { page: "historial", icon: "◔", label: "Historial", key: "8" },
    { page: "ayuda", icon: "?", label: "Ayuda", key: "9" },
    { page: "config", icon: "⚙", label: "Configuración", key: "0" }
  ];
</script>

<aside id="sidebar">
  <nav id="nav">
    {#each items as it (it.page)}
      <button class="nav-btn" class:active={app.page === it.page} data-page={it.page}
              onclick={() => setPage(it.page)} title="{it.label} ({it.key})">
        <i>{it.icon}</i><span class="nav-label">{it.label}</span>
        <span class="nav-key">{it.key}</span>
      </button>
    {/each}
  </nav>
  <div class="side-footer">
    <div id="conn-dot" class="conn {app.conn.cls}" title="Estado de conexión"></div>
    <span id="conn-text">{app.conn.text}</span>
  </div>
</aside>
