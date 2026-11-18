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

            if (IsCloaked(hwnd)) return false;

            Native.RECT frame = GetFrame(hwnd);
            if (frame.Width < 160 || frame.Height < 60) return false;

            // Juegos/vídeos a pantalla completa: sin barra de título y ocupando todo el monitor.
            if (!hasCaption && IsFullscreen(hwnd, frame)) return false;
            return true;
        }

        /// <summary>Ventana oculta por Windows (otro escritorio virtual, app suspendida...).</summary>
        public static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            return Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        /// <summary>Borde visible de la ventana en píxeles de pantalla (sin la sombra invisible).</summary>
        public static Rectangle GetFrameRect(IntPtr hwnd)
        {
            Native.RECT r = GetFrame(hwnd);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }

        /// <summary>Zona de botones que informa Windows (solo fiable en ventanas con marco estándar).</summary>
        public static bool TryGetDwmCaption(IntPtr hwnd, out Rectangle caption)
        {
            caption = Rectangle.Empty;
            Native.RECT wr, cb;
            if (!Native.GetWindowRect(hwnd, out wr)) return false;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CAPTION_BUTTON_BOUNDS, out cb,
                    Marshal.SizeOf(typeof(Native.RECT))) != 0 || cb.Width <= 0 || cb.Height <= 0)
                return false;
            // Coordenadas relativas a la esquina de la ventana.
            caption = new Rectangle(wr.Left + cb.Left, wr.Top + cb.Top, cb.Width, cb.Height);
            return true;
        }

        /// <summary>Último recurso: los 3 botones estándar de Windows 11.</summary>
        public static Rectangle EstimateCaption(Rectangle frame, float scale)
        {
            int w = (int)Math.Round(46 * scale);
            return new Rectangle(frame.Right - 3 * w, frame.Top, 3 * w, (int)Math.Round(32 * scale));
        }

        /// <summary>Nombre del programa (para recordar ajustes por app).</summary>
        public static string GetAppName(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);

            // Apps de la Tienda: la ventana es de ApplicationFrameHost; la app real es la ventana hija.
            if (GetClassName(hwnd) == "ApplicationFrameWindow")
            {
                IntPtr core = Native.FindWindowEx(hwnd, IntPtr.Zero, "Windows.UI.Core.CoreWindow", null);
                if (core != IntPtr.Zero) Native.GetWindowThreadProcessId(core, out pid);
            }
            try
            {
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch (Exception)
            {
                return GetClassName(hwnd);
            }
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

        /// <summary>Todas las ventanas de aplicación visibles, de la más alta a la más baja en la pila.</summary>
        public static List<IntPtr> AppWindows()
        {
            var all = new List<IntPtr>();
            Native.EnumWindows((hwnd, l) => { all.Add(hwnd); return true; }, IntPtr.Zero);
            var result = new List<IntPtr>();
            foreach (IntPtr hwnd in all)
                if (Native.IsWindowVisible(hwnd) && IsEligible(hwnd)) result.Add(hwnd);
            return result;
        }

        /// <summary>Ventanas que PinTop ha fijado y siguen fijadas (limpia las cerradas o soltadas por la app).</summary>
        public static List<IntPtr> PinnedWindows()
        {
            var result = new List<IntPtr>();
            foreach (IntPtr hwnd in new List<IntPtr>(pinnedByUs))
            {
                if (Native.IsWindow(hwnd) && IsPinned(hwnd)) result.Add(hwnd);
                else pinnedByUs.Remove(hwnd);
            }
            return result;
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

        public static float GetScale(IntPtr hwnd)
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
