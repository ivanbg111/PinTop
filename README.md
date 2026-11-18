# 📌 PinTop

Añade un botón con forma de chincheta a la barra de título de cualquier ventana de Windows para
dejarla **siempre encima de todo**, aunque hagas clic en otra aplicación.

Ideal para tener una ventana pequeña de referencia (un PDF, una web, un pedido, una calculadora…)
flotando mientras rellenas datos en otra a pantalla completa, sin pantalla dividida.

![Botón suelto y fijado](docs/preview.png)

## Funciones

- **Botón de chincheta** junto a minimizar / maximizar / cerrar de **todas las ventanas visibles**,
  sin tener que hacer clic en ellas antes (en Ajustes se puede limitar a la activa y las fijadas).
  - Puedes tener 2 o 20 ventanas encima y fijar o soltar cualquiera sin activarla antes.
  - Si otra ventana tapa la barra de título, el botón queda tapado también (no flota por encima).
  - Usa la misma fuente de iconos que los botones de Windows 11, así que se ve nítido.
  - Toma el color de la barra de título de cada app para integrarse.
  - Al fijar la ventana se pinta con el **color de acento de Windows**.
  - Busca un hueco libre: no tapa los iconos propios de apps como Claude, Chrome o Bambu Studio.
  - **Arrástralo** para moverlo; la posición se recuerda para cada app.
  - **Rueda del ratón sobre el botón**: más o menos transparente.
- **Borde de color** (el acento de Windows) alrededor de las ventanas fijadas, para verlas de un
  vistazo. Windows 11; se puede desactivar.
- **Modo fantasma**: la ventana fijada se vuelve semitransparente y **los clics la atraviesan**,
  para trabajar en lo que hay debajo. Se activa y se quita con el atajo o desde el menú del botón
  (el botón sigue respondiendo, así que siempre puedes volver).
- **Atenuar ventanas fijadas** (*desactivado por defecto*, se activa en Ajustes): las ventanas
  fijadas que no estás usando se vuelven transparentes y recuperan su opacidad al pasar el ratón.
- **Fijar siempre una app**: sus ventanas se fijan solas al abrirse (también tras reiniciar).
- **Excluir apps**: lista de programas en los que nunca aparece el botón.
- **Clic derecho en el botón**: fijar/soltar, modo fantasma, opacidad (100 % … 30 %), "Fijar
  siempre esta app", "No mostrar el botón en esta app" y ajustes. Si arrastraste el botón, aparece
  también "Devolver el botón a su sitio". El menú se cierra al hacer clic fuera o pulsar Esc.
- **Icono de la bandeja**: lista de **ventanas fijadas** (clic en una para soltarla), quitar el
  modo fantasma a todas, soltar todas y ajustes.
- **Atajos de teclado configurables** (valores por defecto):
  | Acción | Atajo |
  | --- | --- |
  | Fijar / soltar la ventana activa | `Ctrl + Alt + P` |
  | Más transparente | `Ctrl + Alt + AvPág` |
  | Menos transparente | `Ctrl + Alt + RePág` |
  | Modo fantasma (activar en la ventana activa / quitarlo a todas) | `Ctrl + Alt + G` |
  | Mostrar / ocultar el botón | (sin asignar) |
- **Ventana de ajustes** (doble clic en el icono de la bandeja, o volver a abrir `PinTop.exe`),
  con **modo oscuro** si Windows lo usa.
- **Aviso de actualizaciones**: una vez al día consulta la última Release en GitHub (solo esa
  información pública; no envía datos) y avisa si hay una versión nueva. Se puede desactivar.
- Al salir, **devuelve todas las ventanas a su estado normal** (sin fijar, opacas, sin modo
  fantasma ni borde).
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
En ese modo, `PINTOP_FAKE_VERSION=0.9.0` hace creer a la app que es una versión antigua, para
probar el aviso de actualización.

## Cómo funciona

| Archivo | Qué hace |
| --- | --- |
| `src/TrayApp.cs` | Núcleo: bandeja, menús, atajos y seguimiento de la ventana activa (`SetWinEventHook`). |
| `src/CaptionProbe.cs` | Localiza los botones reales de la ventana con **UI Automation** (en un hilo aparte). |
| `src/ScreenScan.cs` | Analiza los píxeles de la barra de título: hueco libre y color de fondo. |
| `src/PinButton.cs` | El botón flotante: nunca roba el foco (`WS_EX_NOACTIVATE`) y se puede arrastrar. |
| `src/WindowManager.cs` | Qué ventanas admiten botón, fijar (`HWND_TOPMOST`) y opacidad (`SetLayeredWindowAttributes`). |
| `src/SettingsForm.cs` | Ventana de ajustes, captura de atajos y listas de apps. |
| `src/Settings.cs` | Ajustes en `%APPDATA%\PinTop\settings.ini`. |
| `src/Support.cs` | Atajos globales, inicio con Windows, colores, modo oscuro y dibujo de la chincheta. |
| `src/Updater.cs` | Comprueba en GitHub si hay una versión nueva. |

Modo fantasma = `WS_EX_LAYERED` + `WS_EX_TRANSPARENT` en la ventana; borde = `DWMWA_BORDER_COLOR`.

La posición del botón se decide así:
0. La imagen de la barra se toma de la pantalla si se ve entera, o se le pide a la propia ventana
   (`PrintWindow`) si otra ventana la tapa en parte.
1. Botones reales de la ventana según UI Automation (funciona en Chrome, Electron, apps de Windows 11…).
2. Si no, la zona que informa Windows (DWM) o, en barras personalizadas, se busca el icono de minimizar en los píxeles.
3. A la izquierda de esos botones se busca el primer hueco libre para no tapar nada.
4. Si arrastraste el botón en esa app, manda tu posición.

## Limitaciones conocidas

- Las ventanas de programas ejecutados **como administrador** solo se pueden fijar si PinTop
  también se ejecuta como administrador (restricción de seguridad de Windows).
- Si al calcular la posición otra ventana "siempre visible" tapa la barra de título, el botón se
  coloca a su izquierda (puedes arrastrarlo a donde prefieras).
- Algunas ventanas que ya usan transparencia propia no admiten el cambio de opacidad.
- No se muestra en juegos o vídeos a pantalla completa.

## Licencia

[MIT](LICENSE)
