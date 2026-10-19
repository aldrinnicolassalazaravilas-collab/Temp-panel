<script>
  import { app, cmd } from "../lib/state.svelte.js";
  import Card from "../components/Card.svelte";
  import Kv from "../components/Kv.svelte";

  let s = $derived(app.snap);

  let diagRows = $derived.by(() => {
    const d = s?.diagnostics;
    if (!d) return [];
    const drv = d.driverBlocked === true ? "Sí, Windows lo bloqueó"
      : d.driverBlocked === false ? "No se encontraron bloqueos"
        : "No comprobable sin administrador";
    const stateTxt = {
      ok: "Funcionando (hay lecturas)", noadmin: "Sin intentar (falta administrador)",
      "no-pawnio": "Falta el driver PawnIO", blocked: "Bloqueado por Windows",
      "loaded-noread": "Cargado pero sin lecturas", "not-deployed": "Ni se desplegó", unknown: "Desconocido"
    }[d.driverState || "unknown"] || "Desconocido";
    return [
      { k: "Permisos", v: d.isAdmin ? "Administrador" : "Usuario estándar" },
      { k: "Integridad de memoria", v: d.hvciEnabled ? "Activada (bloquea el driver de temperatura)" : "Desactivada" },
      { k: "Driver bloqueado", v: drv },
      { k: "Driver PawnIO", v: d.pawnIoInstalled ? "Instalado" + (d.pawnIoVersion ? " (v" + d.pawnIoVersion + ")" : "") : "NO instalado" },
      { k: "Estado del driver", v: stateTxt },
      { k: "Temps. CPU con valor", v: `${d.cpuTempWithValue} de ${d.cpuTempSensors}` },
      { k: "Sensores con valor", v: `${d.sensorsWithValue} de ${d.totalSensors}` }
    ];
  });

  let advice = $derived.by(() => {
    const d = s?.diagnostics;
    if (!d) return "";
    if (d.cpuTempWithValue > 0) return "El equipo publica temperaturas de CPU: las verás en la sección CPU.";
    if (d.driverState === "no-pawnio" || !d.pawnIoInstalled) return "Falta el driver PawnIO (el que usa la librería para leer el procesador). Instala la edición oficial firmada desde pawnio.eu, reabre la app y listo. No hay que desactivar protecciones ni dejar apps abiertas.";
    if (d.driverState === "blocked") return "Confirmado: Windows bloqueó el controlador de lectura. Para ver temperaturas, desactiva la integridad de memoria y reinicia (o mantenla y usa el resto de sensores).";
    if (d.driverState === "loaded-noread") return "El driver carga pero no obtiene valores: posible incompatibilidad con este firmware. Repórtalo con el botón de registros.";
    if (d.driverState === "not-deployed") return "El driver ni se desplegó aun como administrador: lo más probable es que el antivirus lo pusiera en cuarentena al extraerse. Mira Seguridad de Windows → Historial de protección → busca WinRing0, restaúralo o exclúyelo, y reabre como admin.";
    if (d.hvciEnabled) return "Sin valores de temperatura y con integridad activada: es el bloqueo probable. Desactívala y reinicia para comprobarlo.";
    if (!d.isAdmin) return "Prueba «Reiniciar como administrador» en Configuración → Permisos.";
    return "El firmware no entrega temperaturas al sistema. Se muestran el resto de sensores reales.";
  });

  let defText = $derived.by(() => {
    const d = app.defender;
    if (!d) return "Pulsa «Comprobar estado» para ver si la exclusión ya está aplicada.";
    const st = d.ok ? (d.excluded ? "Exclusión ACTIVA" : "Exclusión NO aplicada") : "Sin datos";
    return `${st}. ${d.message || ""}`;
  });

  const confirmHvciOff = () => confirm("Se desactivará la integridad de memoria (baja la protección contra malware avanzado) y hará falta reiniciar. ¿Continuar?") && cmd.setHvci(false);
  const confirmReboot = () => confirm("El equipo se reiniciará en 15 segundos. ¿Continuar?") && cmd.rebootNow();
  const confirmDefAdd = () => confirm("Se añadirá una exclusión en Windows Defender solo para la carpeta de TempPanel y sus instaladores temporales.\n\nNo se desactiva ninguna protección. ¿Continuar?") && cmd.defenderAdd();
</script>

<Card title="¿Por qué no sale la temperatura de CPU?">
  <div class="muted">La temperatura del procesador solo la entrega Windows a programas
    <b>elevados</b>: ve a <b>Configuración → Permisos → Reiniciar como administrador</b> y acepta el aviso.</div>
  <div class="muted" style="margin-top:8px">Si ni como administrador aparece, la causa suele ser la
    <b>integridad de memoria</b> (Seguridad de Windows → Seguridad del dispositivo →
    Aislamiento del núcleo): bloquea el controlador de bajo nivel. La sección
    «Estado de la detección» de abajo te dice si Windows lo bloqueó en este equipo.
    Puedes mantener la protección activada (recomendado, es seguridad real) o
    desactivarla y reiniciar para ver temperaturas.</div>
</Card>

<Card title="Estado de la detección en este equipo">
  <Kv rows={diagRows} />
  <div class="muted" style="margin-top:8px">{advice}</div>
</Card>

<Card title="Integridad de memoria (HVCI)">
  <div class="muted">Si está activada, bloquea el driver de temperaturas. Cambiarla requiere reiniciar el equipo.</div>
  <div class="toolbar" style="margin-top:8px">
    <button id="btn-hvci-off" class="btn" onclick={confirmHvciOff}>Desactivar y poder ver temperaturas</button>
    <button id="btn-hvci-on" class="btn ghost" onclick={() => cmd.setHvci(true)}>Reactivar protección</button>
    <button id="btn-reboot" class="btn ghost" onclick={confirmReboot}>Reiniciar ahora</button>
  </div>
  <div id="hvci-status" class="muted small">{app.hvciText}</div>
</Card>

<Card title="Antivirus, Defender y SmartScreen">
  <div class="muted">Windows Defender puede retener el instalador recién descargado unos segundos
    (bloqueo de archivo) y SmartScreen puede mostrar el aviso azul «Windows protegió tu PC» porque
    el instalador no está firmado con un certificado de pago. Para los bloqueos, TempPanel puede
    pedirle a Defender una <b>exclusión solo para sus propias rutas</b> (carpeta instalada e
    instaladores en la carpeta temporal). Es opcional, reversible y no desactiva ninguna protección.</div>
  <div class="toolbar" style="margin-top:10px">
    <button id="btn-def-check" class="btn ghost" onclick={cmd.defenderStatus}>Comprobar estado</button>
    <button id="btn-def-add" class="btn" onclick={confirmDefAdd}>Excluir TempPanel de Defender</button>
    <button id="btn-def-remove" class="btn ghost" onclick={cmd.defenderRemove}>Quitar exclusión</button>
  </div>
  <div id="def-advice" class="muted small" style="margin-top:8px">{defText}</div>
  <div class="muted small" style="margin-top:8px">Si al abrir el instalador desde el navegador ves el aviso azul:
    pulsa <b>Más información</b> → <b>Ejecutar de todos modos</b>. Desde la propia app («Buscar actualizaciones»)
    ese aviso aparece menos porque se quita la marca de Internet del archivo.</div>
</Card>

<Card title="Qué significan los estados">
  <Kv rows={[
    { k: "Valor con número", v: "Lectura real del equipo en ese momento." },
    { k: "No disponible", v: "Tu equipo no publica ese sensor. No es un error ni un cero." },
    { k: "Alerta térmica", v: "Aviso visual con histéresis (3 °C) y pausa de 10 min. No modifica nada del equipo." },
    { k: "Alerta de batería", v: "Solo avisa al llegar al umbral cargando. Notificar no detiene la carga física." },
    { k: "Nivel de batería con decimales", v: "Sale de las capacidades reales del firmware (mWh). Es el mismo número que usa Windows, pero sin redondear a entero." },
    { k: "Nivel retenido (enchufado)", v: "El portátil limita la carga (modo conservación ~80 %) para cuidar la batería: el nivel no sube aunque esté enchufado. TempPanel lo muestra tal cual, sin inventar que «está cargando»." }
  ]} />
</Card>

<Card title="Rendimiento y consumo">
  <Kv rows={[
    { k: "Interfaz translúcida", v: "El cristal (acrílico) lo dibuja Windows 11 detrás de la interfaz. Sin esa versión de Windows se usa el tema oscuro sólido, igual de funcional." },
    { k: "Interfaz Svelte", v: "La interfaz se escribe en Svelte y se compila a HTML/JS/CSS local: sin CDN ni dependencias en tiempo de ejecución. Los datos y su significado son los mismos que antes." },
    { k: "Motor de interfaz en pausa", v: "Cuando la ventana está en la bandeja o minimizada, el motor de la interfaz se suspende y libera memoria; los sensores siguen midiendo." },
    { k: "Tabla de sensores", v: "La lista completa solo se lee cuando abres «Todos los sensores»: menos trabajo y menos memoria en el resto de páginas." },
    { k: "Memoria de la app", v: "La cifra real del proceso aparece en Resumen → Sistema («Memoria de la app»)." },
    { k: "Atajos de teclado", v: "1-9 y 0 cambian de página, F activa el modo inmersivo, P pausa y R pide una lectura ahora." }
  ]} />
</Card>

<Card title="Límites reales de este portátil">
  <div class="muted">Ventiladores sin RPM, temperatura propia de GPU integrada y temperatura/ciclos
    de batería no están publicados por el hardware o Windows: nadie puede mostrarlos sin inventarlos,
    así que TempPanel no los muestra.</div>
</Card>
