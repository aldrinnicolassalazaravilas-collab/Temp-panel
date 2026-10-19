**TempPanel 2.5.1** corrige el fallo de la v2.5.0 que podía abrir la ventana **en negro** (sin interfaz).

### Correcciones

- **Ventana en negro (crítica)**: la v2.5.0 no incluía `Microsoft.Windows.SDK.NET.dll`, el ensamblado que necesita el control WebView2 con composición (el que permite ver el cristal acrílico a través de la interfaz). El control fallaba al cargarse y la ventana quedaba vacía. Ahora el ensamblado se publica dentro del ejecutable.
- **Red de seguridad**: si el cristal no se puede inicializar (sistema o runtime sin soporte, tiempo de espera agotado), la app pasa sola al tema oscuro opaco y lo registra. **Nunca** vuelve a quedarse sin interfaz.
- **Inicio más robusto**: el control de cristal se inicializa visible y con tamaño (era la segunda causa del bloqueo) y con un límite de 20 s.
- **Instalador limpio**: al actualizar borra los restos del frontend antiguo (`Frontend\app.js`, `Frontend\styles.css`) y `TempPanel.pdb`.

### Verificación de esta versión

- `dotnet test`: **70/70** pruebas superadas.
- `node uitest\run.js`: **52/52** comprobaciones DOM sobre el bundle compilado.
- CSS compilado y fuente: 0 problemas.
- Nuevo modo `--uicheck` (ventana real, sin elevar): interfaz cargada con **control de cristal (transparencia)** tanto en la app framework-dependent (2,6 s) como en el **exe single-file autocontenido** (6,1 s), sin excepciones en el registro.

### Instalar

1. Descarga `TempPanel-Setup-x64.exe`.
2. Ejecútalo encima de la versión anterior (no hace falta desinstalar).
3. Al abrir la app, Windows pedirá elevación para poder leer temperaturas.

> Si venías de la v2.5.0 con la ventana en negro, esta versión la reemplaza y conserva tus ajustes.
