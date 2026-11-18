using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PinTop
{
    /// <summary>
    /// Núcleo de la app: icono de bandeja, seguimiento de la ventana activa,
    /// botón flotante y atajo de teclado global.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        const int HotkeyId = 1;
        static readonly byte[] OpacityLevels = { 255, 230, 204, 178, 153, 128 };

        readonly NotifyIcon tray;
        readonly PinButton button;
        readonly ContextMenuStrip buttonMenu;
        readonly HotkeyWindow hotkeys;
        readonly Timer timer;
        readonly Native.WinEventDelegate winEventProc;   // referencia viva para que el GC no la recoja
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly ToolStripMenuItem enabledItem;
        readonly ToolStripMenuItem startupItem;

        IntPtr target = IntPtr.Zero;   // ventana sobre la que está el botón
        IntPtr menuTarget = IntPtr.Zero;
        bool enabled = true;

        public TrayApp()
        {
            button = new PinButton();
            button.PinClicked += delegate { TogglePin(target); };
            buttonMenu = BuildButtonMenu();
            button.ContextMenuStrip = buttonMenu;

            enabledItem = new ToolStripMenuItem("Mostrar botón en las ventanas", null, delegate { SetEnabled(!enabled); });
            enabledItem.Checked = true;
            startupItem = new ToolStripMenuItem("Iniciar con Windows", null, delegate
            {
                Startup.Set(!Startup.IsEnabled());
                startupItem.Checked = Startup.IsEnabled();
            });
            startupItem.Checked = Startup.IsEnabled();

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(enabledItem);
            trayMenu.Items.Add(new ToolStripMenuItem("Soltar todas las ventanas fijadas", null, delegate
            {
                WindowManager.UnpinAll();
                Sync();
            }));
            trayMenu.Items.Add(startupItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            var help = new ToolStripMenuItem("Ctrl+Alt+P: fijar/soltar la ventana activa");
            help.Enabled = false;
            trayMenu.Items.Add(help);
            trayMenu.Items.Add(new ToolStripMenuItem("Salir", null, delegate { ExitThread(); }));

            tray = new NotifyIcon();
            tray.Icon = Icons.CreateTrayIcon();
            tray.Text = "PinTop";
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { SetEnabled(!enabled); };
            tray.Visible = true;

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += delegate(int id) { if (id == HotkeyId) TogglePin(Native.GetForegroundWindow()); };
            bool hotkeyOk = hotkeys.Register(HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.P);

            winEventProc = OnWinEvent;
            AddHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
            AddHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND);
            AddHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE);
            AddHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE);

            // Red de seguridad por si se pierde algún evento (cambios de estilo, títulos, etc.).
            timer = new Timer();
            timer.Interval = 250;
            timer.Tick += delegate { Sync(); };
            timer.Start();

            tray.ShowBalloonTip(3000, "PinTop está activo",
                hotkeyOk
                    ? "Pulsa el botón 📌 de cualquier ventana o Ctrl+Alt+P para dejarla encima de todo."
                    : "Pulsa el botón 📌 de cualquier ventana. (Ctrl+Alt+P ya lo usa otra aplicación.)",
                ToolTipIcon.Info);
            Sync();
        }

        ContextMenuStrip BuildButtonMenu()
        {
            var menu = new ContextMenuStrip();
            var pinItem = new ToolStripMenuItem("Fijar encima de todo", null, delegate { TogglePin(menuTarget); });
            menu.Items.Add(pinItem);
            menu.Items.Add(new ToolStripSeparator());
            var title = new ToolStripMenuItem("Opacidad");
            title.Enabled = false;
            menu.Items.Add(title);

            var opacityItems = new List<ToolStripMenuItem>();
            foreach (byte level in OpacityLevels)
            {
                byte alpha = level;
                var item = new ToolStripMenuItem((int)Math.Round(alpha * 100 / 255.0) + " %", null, delegate
                {
                    if (!Native.IsWindow(menuTarget)) return;
                    if (!WindowManager.SetOpacity(menuTarget, alpha))
                        Warn("Esta ventana no admite transparencia.");
                });
                item.Tag = alpha;
                opacityItems.Add(item);
                menu.Items.Add(item);
            }

            menu.Opening += (s, e) =>
            {
                menuTarget = target;
                if (!Native.IsWindow(menuTarget)) { e.Cancel = true; return; }
                pinItem.Text = WindowManager.IsPinned(menuTarget) ? "Soltar ventana" : "Fijar encima de todo";
                byte current = WindowManager.GetOpacity(menuTarget);
                foreach (var item in opacityItems)
                    item.Checked = Math.Abs((byte)item.Tag - current) < 13;
            };
            return menu;
        }

        void AddHook(uint min, uint max)
        {
            IntPtr hook = Native.SetWinEventHook(min, max, IntPtr.Zero, winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero) hooks.Add(hook);
        }

        void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != Native.OBJID_WINDOW || idChild != 0) return;
            if (eventType == Native.EVENT_SYSTEM_FOREGROUND || hwnd == target) Sync();
        }

        /// <summary>Coloca (o esconde) el botón según la ventana activa actual.</summary>
        void Sync()
        {
            if (!enabled)
            {
                button.HideButton();
                return;
            }

            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && WindowManager.IsOwnWindow(fg))
            {
                // Nuestro propio menú está abierto: dejamos el botón donde está.
                if (buttonMenu.Visible) return;
                button.HideButton();
                return;
            }

            Rectangle bounds;
            if (!WindowManager.IsEligible(fg) || !WindowManager.TryGetButtonBounds(fg, out bounds))
            {
                target = IntPtr.Zero;
                button.HideButton();
                return;
            }

            target = fg;
            button.ShowAt(bounds, WindowManager.IsPinned(fg));
        }

        void TogglePin(IntPtr hwnd)
        {
            if (!WindowManager.IsEligible(hwnd)) return;
            if (!WindowManager.TogglePin(hwnd))
                Warn("No se pudo cambiar esta ventana. Si la aplicación se ejecuta como administrador, ejecuta también PinTop como administrador.");
            Sync();
        }

        void SetEnabled(bool value)
        {
            enabled = value;
            enabledItem.Checked = value;
            Sync();
        }

        void Warn(string message)
        {
            tray.ShowBalloonTip(3000, "PinTop", message, ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            foreach (IntPtr hook in hooks) Native.UnhookWinEvent(hook);
            hooks.Clear();
            hotkeys.Dispose();
            WindowManager.RestoreAll();   // nada se queda fijado ni transparente al salir
            tray.Visible = false;
            tray.Dispose();
            button.Close();
            base.ExitThreadCore();
        }
    }
}
