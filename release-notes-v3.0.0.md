**TempPanel 3.0.0** es la actualización de integración con Windows y rendimiento: la app se adapta a lo que estás haciendo, consume menos y añade atajos y controles del sistema, manteniendo el mismo diseño.

### Integración con Windows

- **Atajo global Ctrl+Alt+T**: muestra u oculta TempPanel desde cualquier aplicación. Configuración → Integración con Windows indica si Windows lo concedió y permite desactivarlo.
- **Snap Layouts (Windows 11)**: al pasar el ratón por el botón maximizar (modo cristal) aparece el menú de distribuciones de Windows; el clic sigue maximizando/restaurando.
- **Barra de tareas**: distintivo amarillo/rojo cuando hay alerta térmica y progreso mientras la batería carga.
- **Bandeja**: accesos directos a Sensores y Configuración además de Abrir, Mini ventana y Salir.
- **Tema y acento**: TempPanel usa el color de acento de Windows en los anillos de foco y respeta el alto contraste. El tema claro/oscuro y el acento se actualizan sin reiniciar.
- **Eventos del sistema**: bloqueo de sesión, suspensión/reanudación y cambios de energía reducen la actividad y se reanudan al volver.
- **Ajustes de Windows**: botones directos a Color de acento y Ahorro de energía.

### Menos consumo, más rapidez

- **Gobernador de rendimiento**: primer plano respeta tu intervalo; sin foco 3 s; oculto, minimizado, bloqueado o con otra app a pantalla completa 5 s y el motor WebView2 se suspende. La mini ventana conserva su ritmo. Se puede desactivar.
- **Lector perezoso**: LibreHardwareMonitor se crea después de mostrar la interfaz, no antes. La ventana aparece al instante con una pantalla de carga nativa y los sensores llegan unos segundos después sin bloquear.
- **Tabla de sensores virtualizada**: con cientos de sensores solo se dibujan las filas visibles.
- **Ventana mínima 820×560** y barra lateral compacta por debajo de 980 px.

### Correcciones de esta versión

- No se suspende WebView2 durante su inicialización (detectado en las pruebas: podía retrasar la interfaz).
- Una ventana maximizada con la barra de tareas oculta ya no se confunde con pantalla completa.
- La mini ventana mantiene el intervalo elegido aunque la principal esté oculta.
- Al actualizar se borran los restos del frontend antiguo.

### Verificación

- `dotnet test`: **86/86** pruebas.
- `node uitest\run.js`: **71/71** comprobaciones DOM.
- CSS: 0 problemas.
- Ventana real (`--uicheck`): interfaz + **control de cristal** + sensores listos; sin excepciones en el registro.
- Instalador: 54,7 MB; exe autocontenido: 191,9 MB.

### Instalar

1. **Cierra TempPanel antes de instalar** (si está abierto, el instalador por usuario no puede cerrarlo porque la app se ejecuta elevada).
2. Ejecuta `TempPanel-Setup-x64.exe` encima de la versión anterior; conserva tus ajustes.
3. Al abrir la app, Windows pedirá elevación para leer temperaturas.

> La build es x64 (Windows 10/11). En Windows 11 ARM64 funciona por emulación.

### Para probar en tu equipo

El menú visual de Snap Layouts, la pulsación física de Ctrl+Alt+T y el distintivo de la barra de tareas con una alerta real necesitan interacción de escritorio; la lógica y el registro están verificados, pero la prueba final es tuya.
