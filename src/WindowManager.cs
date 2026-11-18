using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace PinTop
{
    /// <summary>
    /// Operaciones sobre ventanas de otras aplicaciones: decidir si se les pone botón,
    /// calcular dónde, fijarlas encima y cambiar su opacidad.
    /// </summary>
    internal static class WindowManager
    {
        static readonly uint OwnPid = (uint)Process.GetCurrentProcess().Id;

        // Ventanas que hemos fijado nosotros (para soltarlas al salir).
        static readonly HashSet<IntPtr> pinnedByUs = new HashSet<IntPtr>();

        // Ventanas a las que hemos cambiado la opacidad -> ¿eran ya "layered" antes?
        static readonly Dictionary<IntPtr, bool> opacityTouched = new Dictionary<IntPtr, bool>();

        // Ventanas del sistema en las que nunca se muestra el botón.
        static readonly HashSet<string> IgnoredClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
            "TopLevelWindowForOverflowXamlIsland", "ForegroundStaging",
            "MultitaskingViewFrame", "TaskListThumbnailWnd", "NotifyIconOverflowWindow", "#32768"
        };

        public static bool IsOwnWindow(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return pid == OwnPid;
        }

        public static string GetClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>¿Es una ventana "normal" de aplicación en la que tiene sentido mostrar el botón?</summary>
        public static bool IsEligible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd))
                return false;
            if (IsOwnWindow(hwnd) || IgnoredClasses.Contains(GetClassName(hwnd)))
                return false;

            int style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if ((style & Native.WS_CHILD) != 0) return false;
            if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0) return false;

            bool hasCaption = (style & Native.WS_CAPTION) == Native.WS_CAPTION;
            bool hasButtons = (style & (Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX)) != 0;
            if (!hasCaption && !hasButtons) return false;

            int cloaked;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return false;

            Native.RECT frame = GetFrame(hwnd);
            if (frame.Width < 160 || frame.Height < 60) return false;

            // Juegos/vídeos a pantalla completa: sin barra de título y ocupando todo el monitor.
            if (!hasCaption && IsFullscreen(hwnd, frame)) return false;
            return true;
        }

        /// <summary>Calcula, en píxeles de pantalla, dónde va el botón: a la izquierda de minimizar.</summary>
        public static bool TryGetButtonBounds(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            float scale = GetScale(hwnd);
            Native.RECT frame = GetFrame(hwnd);
            Native.RECT wr;
            if (!Native.GetWindowRect(hwnd, out wr)) return false;

            int capLeft, capTop, capHeight;
            Native.RECT cb;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CAPTION_BUTTON_BOUNDS, out cb,
                    Marshal.SizeOf(typeof(Native.RECT))) == 0 && cb.Width > 0 && cb.Height > 0)
            {
                // Coordenadas relativas a la esquina de la ventana.
                capLeft = wr.Left + cb.Left;
                capTop = wr.Top + cb.Top;
                capHeight = cb.Height;
            }
            else
            {
                // Barra de título personalizada: suponemos los 3 botones estándar de Windows 11.
                int buttonWidth = (int)Math.Round(46 * scale);
                capLeft = frame.Right - 3 * buttonWidth;
                capTop = frame.Top;
                capHeight = (int)Math.Round(32 * scale);
            }

            int size = (int)Math.Round(24 * scale);
            int gap = (int)Math.Round(6 * scale);
            int x = capLeft - gap - size;
            int y = capTop + Math.Max(0, (capHeight - size) / 2);
            if (y < frame.Top) y = frame.Top;
            if (x < frame.Left) return false;

            bounds = new Rectangle(x, y, size, size);
            return true;
        }

        public static bool IsPinned(IntPtr hwnd)
        {
            return (Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
        }

        /// <summary>Fija o suelta la ventana. Devuelve false si Windows no lo permite (p. ej. ventana de administrador).</summary>
        public static bool TogglePin(IntPtr hwnd)
        {
            bool pin = !IsPinned(hwnd);
            SetPinned(hwnd, pin);
            return IsPinned(hwnd) == pin;
        }

        static void SetPinned(IntPtr hwnd, bool pin)
        {
            bool ok = Native.SetWindowPos(hwnd, pin ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
            if (!ok) return;
            if (pin) pinnedByUs.Add(hwnd); else pinnedByUs.Remove(hwnd);
        }

        public static void UnpinAll()
        {
            foreach (IntPtr hwnd in new List<IntPtr>(pinnedByUs))
            {
                if (Native.IsWindow(hwnd) && IsPinned(hwnd)) SetPinned(hwnd, false);
                pinnedByUs.Remove(hwnd);
            }
        }

        /// <summary>Opacidad actual (0-255) aplicada con SetLayeredWindowAttributes; 255 si es opaca.</summary>
        public static byte GetOpacity(IntPtr hwnd)
        {
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_LAYERED) == 0) return 255;
            uint key, flags;
            byte alpha;
            if (Native.GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags) && (flags & Native.LWA_ALPHA) != 0)
                return alpha;
            return 255;
        }

        public static bool SetOpacity(IntPtr hwnd, byte alpha)
        {
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            bool layered = (ex & Native.WS_EX_LAYERED) != 0;

            if (!opacityTouched.ContainsKey(hwnd))
            {
                // Una ventana que ya era "layered" pero sin atributos usa transparencia por píxel:
                // tocarla la estropearía, así que no lo hacemos.
                uint k, f;
                byte a;
                if (layered && !Native.GetLayeredWindowAttributes(hwnd, out k, out a, out f)) return false;
                opacityTouched[hwnd] = layered;
            }

            if (alpha == 255 && !opacityTouched[hwnd])
            {
                // Volvemos a dejar la ventana exactamente como estaba.
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex & ~Native.WS_EX_LAYERED);
                opacityTouched.Remove(hwnd);
                return true;
            }

            if (!layered) Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
            return Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA);
        }

        /// <summary>Deja todas las ventanas tocadas como estaban (al cerrar la app).</summary>
        public static void RestoreAll()
        {
            UnpinAll();
            foreach (IntPtr hwnd in new List<IntPtr>(opacityTouched.Keys))
            {
                if (Native.IsWindow(hwnd)) SetOpacity(hwnd, 255);
            }
            opacityTouched.Clear();
        }

        static Native.RECT GetFrame(IntPtr hwnd)
        {
            Native.RECT r;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r,
                    Marshal.SizeOf(typeof(Native.RECT))) != 0)
                Native.GetWindowRect(hwnd, out r);
            return r;
        }

        static float GetScale(IntPtr hwnd)
        {
            try
            {
                uint dpi = Native.GetDpiForWindow(hwnd);
                return dpi > 0 ? dpi / 96f : 1f;
            }
            catch (EntryPointNotFoundException)
            {
                return 1f;
            }
        }

        static bool IsFullscreen(IntPtr hwnd, Native.RECT frame)
        {
            var mi = new Native.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi))
                return false;
            return frame.Left <= mi.rcMonitor.Left && frame.Top <= mi.rcMonitor.Top
                && frame.Right >= mi.rcMonitor.Right && frame.Bottom >= mi.rcMonitor.Bottom;
        }
    }
}
