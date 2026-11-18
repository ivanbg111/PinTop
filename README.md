# 📌 PinTop

Añade un botón con forma de chincheta a la barra de título de cualquier ventana de Windows para
dejarla **siempre encima de todo**, aunque hagas clic en otra aplicación.

Ideal para tener una ventana pequeña de referencia (un PDF, una web, un pedido, una calculadora…)
flotando mientras rellenas datos en otra a pantalla completa, sin pantalla dividida.

![Botón suelto y fijado](docs/preview.png)

## Funciones

- **Botón de chincheta** junto a minimizar / maximizar / cerrar de la ventana activa.
  - Toma el color de la barra de título de cada app para integrarse.
  - Al fijar la ventana se pinta con el **color de acento de Windows**.
  - Busca un hueco libre: no tapa los iconos propios de apps como Claude, Chrome o Bambu Studio.
  - **Arrástralo** para moverlo; la posición se recuerda para cada app.
- **Clic derecho en el botón**: opacidad (100 % … 30 %), volver a la posición automática y ajustes.
- **Atajos de teclado configurables** (valores por defecto):
  | Acción | Atajo |
  | --- | --- |
  | Fijar / soltar la ventana activa | `Ctrl + Alt + P` |
  | Más transparente | `Ctrl + Alt + AvPág` |
  | Menos transparente | `Ctrl + Alt + RePág` |
  | Mostrar / ocultar el botón | (sin asignar) |
- **Ventana de ajustes** (doble clic en el icono de la bandeja, o volver a abrir `PinTop.exe`):
  atajos, tamaño del botón, mostrar/ocultar el botón e inicio con Windows.
- Al salir, **devuelve todas las ventanas a su estado normal** (sin fijar y opacas).
- Un único `.exe` pequeño, sin instalación ni dependencias.

## Uso

1. Descarga `PinTop.exe` desde [Releases](https://github.com/ivanbg111/PinTop/releases) y guárdalo
   en una carpeta fija (por ejemplo `Documentos\PinTop`).
2. Ábrelo. Aparece un icono junto al reloj y la chincheta en la barra de título de la ventana activa.
3. Púlsala para fijar la ventana. Vuelve a pulsarla para soltarla.

### Aviso de Windows SmartScreen

La primera vez Windows puede decir *"Windows protegió tu PC"*. Pasa con cualquier programa nuevo
que no está firmado con un certificado de pago: no significa que tenga nada malo.
Pulsa **Más información → Ejecutar de todas formas**. Si lo prefieres, puedes compilarlo tú mismo
desde el código (abajo); un `.exe` compilado en tu PC no muestra el aviso.

## Línea de comandos

| Comando | Qué hace |
| --- | --- |
| `PinTop.exe` | Inicia PinTop (si ya está abierto, abre sus ajustes). |
| `PinTop.exe --exit` | Cierra PinTop limpiamente (suelta todas las ventanas). |

## Compilar

No hace falta instalar Visual Studio ni el SDK de .NET: se usa el compilador de C#
que trae Windows (.NET Framework 4.8).

```powershell
.\build.ps1
```

El ejecutable queda en `bin\PinTop.exe`. `tools\make-icon.ps1` regenera `assets\pintop.ico`.

**Diagnóstico:** con la variable de entorno `PINTOP_DEBUG=1`, PinTop escribe en `%TEMP%\PinTop\`
un registro de cómo ha colocado el botón en cada ventana y la franja de barra de título analizada.

## Cómo funciona

| Archivo | Qué hace |
| --- | --- |
| `src/TrayApp.cs` | Núcleo: bandeja, menús, atajos y seguimiento de la ventana activa (`SetWinEventHook`). |
| `src/CaptionProbe.cs` | Localiza los botones reales de la ventana con **UI Automation** (en un hilo aparte). |
| `src/ScreenScan.cs` | Analiza los píxeles de la barra de título: hueco libre y color de fondo. |
| `src/PinButton.cs` | El botón flotante: nunca roba el foco (`WS_EX_NOACTIVATE`) y se puede arrastrar. |
| `src/WindowManager.cs` | Qué ventanas admiten botón, fijar (`HWND_TOPMOST`) y opacidad (`SetLayeredWindowAttributes`). |
| `src/SettingsForm.cs` | Ventana de ajustes y captura de atajos. |
| `src/Settings.cs` | Ajustes en `%APPDATA%\PinTop\settings.ini`. |
| `src/Support.cs` | Atajos globales, inicio con Windows, colores y dibujo vectorial de la chincheta. |

La posición del botón se decide así:
1. Botones reales de la ventana según UI Automation (funciona en Chrome, Electron, apps de Windows 11…).
2. Si no, la zona que informa Windows (DWM) o, en barras personalizadas, se busca el icono de minimizar en los píxeles.
3. A la izquierda de esos botones se busca el primer hueco libre para no tapar nada.
4. Si arrastraste el botón en esa app, manda tu posición.

## Limitaciones conocidas

- Las ventanas de programas ejecutados **como administrador** solo se pueden fijar si PinTop
  también se ejecuta como administrador (restricción de seguridad de Windows).
- Si otra ventana "siempre visible" tapa la barra de título, el botón se coloca a su izquierda.
- Algunas ventanas que ya usan transparencia propia no admiten el cambio de opacidad.
- No se muestra en juegos o vídeos a pantalla completa.

## Licencia

[MIT](LICENSE)
