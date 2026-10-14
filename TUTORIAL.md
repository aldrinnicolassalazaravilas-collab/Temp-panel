# Tutorial TempPanel — por qué no sale la temperatura y cómo solucionarlo

## Lo que ya funciona sin tocar nada

Aunque la temperatura no aparezca, TempPanel sí muestra datos reales:
cargas de CPU (total y por núcleo), frecuencia y carga de GPU, RAM usada y
disponible, batería completa (%, estado, autonomía, capacidades) y gráficos.
«No disponible» significa «tu equipo no lo publica», nunca un dato inventado.

## Paso 1 — Probar como administrador (1 minuto)

Las temperaturas del procesador solo las entrega Windows a programas elevados.

1. Abre TempPanel.
2. Ve a **Configuración → Permisos → «Reiniciar como administrador»**.
3. Acepta el aviso de Windows (UAC).
4. Ve a la sección **CPU** y espera unos segundos.

Si aparecen paquete y núcleos: listo, era solo eso. Puedes seguir usando
TempPanel normal; repite este paso cuando quieras ver temperaturas.

## Paso 2 — Si ni como administrador aparecen

En tu IdeaPad la causa verificada es la **integridad de memoria** de Windows
(protección HVCI): bloquea el controlador de bajo nivel que lee el sensor del
procesador, incluso con permisos de administrador. Les pasa a todos los
monitores que usan este método, no es un fallo de TempPanel.

Compruébalo así:

1. Abre **Seguridad de Windows → Seguridad del dispositivo →
   Aislamiento del núcleo**.
2. Mira **Integridad de memoria**: si está **Activada**, ese es el bloqueo.

### Opción A — Dejarlo como está (recomendado)

Mantén la protección activada (es seguridad real contra malware avanzado) y
usa TempPanel para cargas, RAM, batería y GPU. Las alertas térmicas simplemente
no se activarán sin lecturas, y la app lo indica con honestidad.

### Opción B — Desactivarla para ver temperaturas (tu decisión)

1. En **Aislamiento del núcleo**, desactiva **Integridad de memoria**.
2. **Reinicia** el portátil.
3. Abre TempPanel como administrador (Paso 1).

Entonces deberían aparecer paquete, núcleos y margen a TjMax. Ten en cuenta
que desactivar esta protección reduce la seguridad del sistema; puedes volver
a activarla cuando quieras (repite los pasos al revés).

### Si el estado dice «Ni se desplegó» (antivirus)

Aunque seas administrador, el antivirus puede poner en cuarentena el archivo
del driver (WinRing0) en cuanto LibreHardwareMonitor lo extrae, antes de que
Windows lo bloquee siquiera. Compruébalo:

1. Abre **Seguridad de Windows → Protección contra virus y amenazas →
   Historial de protección**.
2. Busca **WinRing0** (puede aparecer como HackTool o controlador vulnerable).
3. Si está: elígela → **Acciones → Permitir/Restaurar** (solo si confías en
   TempPanel; el driver es de código abierto, el mismo que usan LibreHardwareMonitor
   y proyectos afines). Alternativa: añade una exclusión para ese archivo.
4. Cierra TempPanel del todo y **reábelo como administrador**.
5. Mira Ayuda → «Estado del driver»: debería pasar a «Funcionando» y aparecer
   las temperaturas.

## Métodos alternativos buscados en este equipo (2026-10-09)

Se interrogó directamente el portátil; ninguno ofrece la temperatura de CPU
sin el driver de bajo nivel (y sin programas externos, por decisión del usuario):

- `Win32_TemperatureProbe`: existe pero sin lectura (Unknown).
- Contadores `Thermal Zone Information`: no existen.
- `MSAcpi_ThermalZoneTemperature` y `BatteryTemperature`: clases sin instancias.
- WMI de Lenovo (`LENOVO_GAMEZONE_*`, `LENOVO_CPU_METHOD`…): expone control
  (overclock, ventiladores, energía) y eventos, pero ningún lector de
  temperatura de CPU en vivo.
- La vía HWiNFO se descartó a petición del usuario: TempPanel no depende de
  ningún programa externo.

Queda únicamente la vía del driver de bajo nivel (Paso 1 + Opción B). La página
de Ayuda de la app muestra si Windows bloqueó ese driver en tu equipo.

### Lo que NO va a aparecer nunca en este modelo

- **RPM de ventiladores**: el controlador del IdeaPad no los publica.
- **Temperatura propia de la GPU integrada**: la Intel UHD no tiene sensor
  independiente expuesto.
- **Temperatura o ciclos de batería**: Windows no los expone; nadie puede
  mostrarlos sin inventarlos.

## Resumen rápido

| Síntoma | Causa | Solución |
|---|---|---|
| Sin temperaturas en modo normal | Falta permiso elevado | Paso 1 |
| Sin temperaturas como admin | Integridad de memoria bloquea el driver | Opción A o B |
| Sin ventiladores / temp GPU | El hardware no los publica | Ninguna (límite real) |
