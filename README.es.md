# Juicebar

*[Read in English](README.md)* · *Antes se llamaba MonitBat.*

App de bandeja que muestra la batería de tus periféricos inalámbricos (ratones, auriculares, teclados, mandos) y, si quieres, el uso y la temperatura de CPU, RAM, disco y GPU. Funciona en **Windows 10/11, Linux y macOS**. Habla con los dispositivos directamente por HID o lee lo que el sistema ya sabe, así que no necesita el software del fabricante (y no se pelea con él).

El idioma por defecto es inglés; el español se elige en *Settings > General > Language*.

## Funciones

- **Un icono por dispositivo** en la bandeja con su porcentaje y una barra del color del dispositivo (rojo si está bajo, verde cargando, atenuado en reposo). Otros estilos: *Pilas*, *Anillos dobles* y *Glifos* (dos dispositivos en un icono).
- **Ventana de detalles** (clic izquierdo) en cuatro estilos: *Cristal*, *Anillos*, *Segmentos* e *Historial* (curva de 24 h y tiempo restante estimado). Un bloque **Sistema** muestra CPU, RAM, disco, GPU y temperaturas.
- **Configuración** (engranaje o menú del icono), en cinco pestañas:
  - **Apariencia**: estilo de icono y de ventana con vistas previas.
  - **Dispositivos**: renombrar, cambiar color, ordenar, ocultar u olvidar cada dispositivo, y **salud de la batería** (ciclos de carga estimados, descarga media por hora y cuánto dura una carga completa).
  - **Avisos**: umbral de batería baja (5-50 %), aviso de carga completa, sonido opcional, botón de prueba.
  - **Sistema**: uso del sistema en la ventana y un icono extra en la bandeja para CPU, RAM, GPU o una temperatura.
  - **General**: idioma (inglés o español), iniciar con el sistema, atajo de teclado, widget de escritorio, buscar actualizaciones.
- **Widget de escritorio**: lista compacta y arrastrable (recuerda la posición). Doble clic abre la ventana de detalles.
- **Atajo de teclado** en Windows: Ctrl+Alt+B (o Ctrl+Alt+M / Ctrl+Shift+Alt+B si está ocupado). En Linux y macOS, asigna `juicebar --show` a un atajo en la configuración del sistema.
- **Actualizaciones**: compara con las versiones publicadas en GitHub; si hay una nueva, el menú del icono ofrece descargarla.

## Fuentes de batería

| Fuente | Qué cubre | Sistemas | Probado con hardware |
|---|---|---|---|
| Compx | Scyrox V8 y otros ratones con chip Compx | todos | sí |
| Corsair Void | Void Elite y modelos con el mismo protocolo | todos | sí (versión 1) |
| Logitech HID++ 2.0 | ratones y teclados (Unifying, Bolt, Lightspeed, cable) | Windows, macOS | no |
| HeadsetControl | auriculares SteelSeries, HyperX, Logitech G, Roccat, Audeze... si está instalado | todos | no |
| Bluetooth de Windows | lo que Windows muestra con batería en Configuración | Windows | no |
| UPower y OpenRazer | Bluetooth, Logitech (kernel), mandos, Razer | Linux | no |
| `system_profiler` | ratones, teclados, auriculares Bluetooth, AirPods | macOS | no |

Aura Sync, RGB Fusion e iCUE controlan luces y no informan baterías; Juicebar puede funcionar al mismo tiempo. Si un dispositivo no se puede abrir, otro programa puede tenerlo en exclusiva.

## Monitor del sistema

| Valor | Windows | Linux | macOS |
|---|---|---|---|
| Uso de CPU, RAM y disco | sí | sí | sí |
| Uso y temperatura de GPU | NVIDIA (`nvidia-smi`), o cualquier GPU con LibreHardwareMonitor abierto | NVIDIA, AMD (sysfs) | uso (`ioreg`) |
| Temperatura de CPU | con LibreHardwareMonitor abierto (o ACPI como administrador) | sí (hwmon) | macOS no la expone |

## Instalar

- **Windows**: descomprime y ejecuta `Juicebar.exe`. Windows 11 pone los iconos nuevos en la zona oculta: arrástralos a la barra.
- **Linux**: descomprime y ejecuta `bash install.sh` (copia a `~/.local/bin/juicebar` e instala la regla udev). En GNOME hace falta la extensión AppIndicator.
- **macOS**: sigue `LEEME.txt` dentro del zip (la app no está firmada por Apple).

Los datos están en `%LOCALAPPDATA%\Juicebar`, `~/.config/juicebar` o `~/Library/Application Support/Juicebar`.

## Compilar

SDK de .NET 8: `dotnet build app/Juicebar.App`. Paquetes para todos los sistemas: `pwsh publish.ps1` (quedan en `dist/`). Al subir un tag `v2.1.0`, el workflow `.github/workflows/release.yml` publica los cuatro paquetes en GitHub.

La versión 1, que todavía se llama MonitBat (solo Windows, .NET Framework, interfaz en español), sigue en `src/`, `build.cmd` y `bin/`. Al iniciar por primera vez, Juicebar copia la configuración, el historial y los dispositivos de MonitBat.
