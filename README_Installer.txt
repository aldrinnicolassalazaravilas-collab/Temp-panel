TempPanel — Monitor de hardware en tiempo real (v1.0.0)
=====================================================

Gracias por instalar TempPanel. No necesitas saber programar
ni abrir ninguna terminal para usarlo.

INSTALACIÓN
-----------
1. Haz doble clic en TempPanel-Setup-x64.exe.
2. Elige la carpeta de instalación (por defecto Archivos de programa).
3. Marca "Crear icono en el escritorio" si lo deseas.
4. Pulsa Instalar y luego Finalizar.
5. Abre TempPanel desde el escritorio o el menú Inicio.

REQUISITOS
----------
- Windows 10/11 de 64 bits.
- Microsoft Edge WebView2 Runtime (viene preinstalado en la
  mayoría de equipos; si falta, el instalador te avisa con el
  enlace oficial de Microsoft).
- No necesitas instalar .NET: la aplicación es autocontenida.

USO BÁSICO
----------
- La app se actualiza sola cada 2 segundos.
- Usa el menú lateral: Resumen, CPU, GPU, RAM, Batería,
  Refrigeración, Sensores, Historial y Configuración.
- "No disponible" significa que tu equipo no expone ese sensor.
  No es un error: los portátiles no publican todos los sensores.
- En Historial puedes cambiar el rango, pausar y exportar a CSV.
- En Configuración puedes cambiar la frecuencia (1-10 s), las
  unidades (°C/°F) y los umbrales de alerta térmica (avisos
  informativos con histéresis).
- Cerrar la ventana minimiza a la bandeja (sigue funcionando).
  Usa el icono de la bandeja para abrir o salir del todo.

ALERTA DE BATERÍA
-----------------
- Mientras TempPanel se ejecuta, vigila la batería cada 15 s.
- Al 80 % cargando muestra un aviso con botón OK (o notificación
  de Windows, según ajustes). Solo avisa una vez por carga.
- El aviso NO detiene la carga física: solo te recuerda que
  puedes desenchufar para cuidar la batería.

PERMISOS
--------
- TempPanel funciona sin ser administrador.
- Si faltan sensores, prueba el botón "Reiniciar como
  administrador" en Configuración.

DESINSTALACIÓN
--------------
Panel de control > Programas > TempPanel > Desinstalar,
o menú Inicio > TempPanel > Desinstalar. Solo se eliminan los
archivos propios de TempPanel.

PROBLEMAS FRECUENTES
---------------------
- "No se pudo iniciar WebView2": instala el runtime desde
  https://go.microsoft.com/fwlink/p/?LinkId=2124703 y reabre.
- "Sin sensores de CPU": ejecuta como administrador una vez.
- Ventiladores "No disponible": normal en IdeaPad Slim; el
  controlador embebido no publica RPM al sistema.
- GPU sin temperatura: normal en Intel UHD integrada; no expone
  sensor térmico independiente.
- Antivirus bloquea la instalación: TempPanel solo lee sensores,
  no modifica voltajes ni ventiladores. Permite la app firmada
  como local o consulta al editor.

PRIVACIDAD
----------
Todo funciona en local. No se envía nada a Internet.
Ajustes: %AppData%\TempPanel\config.json
Errores: %AppData%\TempPanel\logs\
