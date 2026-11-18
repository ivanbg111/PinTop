using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace PinTop
{
    /// <summary>
    /// Núcleo de la app: icono de bandeja, seguimiento de la ventana activa,
    /// colocación del botón flotante y atajos de teclado globales.
    /// </summary>
    internal sealed class TrayApp : ApplicationContext
    {
        /// <summary>Dónde va el botón en una ventana concreta (relativo a su esquina superior derecha).</summary>
        sealed class Placement
        {
            public Size FrameSize;
            public int FromRight, FromTop, Size;
            public bool FromUia;
            public string App;
        }

        public const string ShowSettingsEvent = "PinTop.ShowSettings";
        public const string ExitEvent = "PinTop.Exit";

        readonly Settings settings;
        readonly NotifyIcon tray;
        readonly PinButton button;
        readonly ContextMenuStrip buttonMenu;
        readonly HotkeyWindow hotkeys;
        readonly CaptionProbe probe;
        readonly Timer syncTimer = new Timer();
        readonly Timer settleTimer = new Timer();
        readonly Timer probeTimeout = new Timer();
        readonly Timer captureTimer = new Timer();
        readonly Native.WinEventDelegate winEventProc;   // referencia viva para que el GC no la recoja
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly List<RegisteredWaitHandle> waits = new List<RegisteredWaitHandle>();
        readonly List<WaitHandle> signals = new List<WaitHandle>();
        readonly Dictionary<IntPtr, Placement> placements = new Dictionary<IntPtr, Placement>();
        readonly ToolStripMenuItem showButtonItem;
        SettingsForm settingsForm;

        IntPtr target = IntPtr.Zero;       // ventana sobre la que está (o estará) el botón
        Placement current;                 // colocación activa del botón
        IntPtr pendingHwnd = IntPtr.Zero;  // ventana cuya colocación se está calculando
        Size pendingSize;
        int ticket;
        Rectangle? probeResult;
        IntPtr menuTarget = IntPtr.Zero;

        public TrayApp()
        {
            settings = Settings.Load();
            try { Startup.RefreshPath(); } catch (Exception) { }

            button = new PinButton();
            button.PinClicked += delegate { TogglePin(target); };
            button.DragEnded += OnDragEnded;
            buttonMenu = BuildButtonMenu();
            button.ContextMenuStrip = buttonMenu;

            probe = new CaptionProbe();
            probe.Completed += (t, hwnd, result) =>
            {
                try
                {
                    if (!button.IsDisposed) button.BeginInvoke(new Action(() => OnProbeResult(t, result)));
                }
                catch (InvalidOperationException) { }
            };

            showButtonItem = new ToolStripMenuItem("Mostrar el botón en las ventanas", null, delegate { SetShowButton(!settings.ShowButton); });
            showButtonItem.Checked = settings.ShowButton;

            var trayMenu = new ContextMenuStrip();
            var settingsItem = new ToolStripMenuItem("Ajustes…", null, delegate { ShowSettings(); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            trayMenu.Items.Add(settingsItem);
            trayMenu.Items.Add(showButtonItem);
            trayMenu.Items.Add(new ToolStripMenuItem("Soltar todas las ventanas fijadas", null, delegate
            {
                WindowManager.UnpinAll();
                Sync();
            }));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(new ToolStripMenuItem("Salir", null, delegate { ExitThread(); }));

            tray = new NotifyIcon();
            tray.Icon = Icons.CreateTrayIcon();
            tray.Text = "PinTop";
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Visible = true;

            hotkeys = new HotkeyWindow();
            hotkeys.Pressed += OnHotkey;
            List<string> failed = RegisterHotkeys();

            winEventProc = OnWinEvent;
            AddHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND);
            AddHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND);
            AddHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_HIDE);
            AddHook(Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_OBJECT_LOCATIONCHANGE);

            // Red de seguridad por si se pierde algún evento.
            syncTimer.Interval = 250;
            syncTimer.Tick += delegate { Sync(); };
            syncTimer.Start();

            // Espera a que la ventana deje de moverse/redimensionarse antes de calcular la posición.
            settleTimer.Interval = 180;
            settleTimer.Tick += delegate { settleTimer.Stop(); StartProbe(); };
            probeTimeout.Interval = 700;
            probeTimeout.Tick += delegate { probeTimeout.Stop(); probeResult = null; FinishPlacement(); };
            captureTimer.Interval = 60;
            captureTimer.Tick += delegate { captureTimer.Stop(); FinishPlacement(); };

            // Comunicación con otras instancias (abrir ajustes / salir).
            ListenForSignal(ShowSettingsEvent, ShowSettings);
            ListenForSignal(ExitEvent, ExitThread);

            if (settings.IsNew)
            {
                settings.Save();
                tray.ShowBalloonTip(5000, "PinTop está activo",
                    "Pulsa la chincheta de cualquier ventana o " + settings.Hotkeys[HotkeyAction.TogglePin]
                    + " para dejarla encima de todo. Doble clic en este icono para los ajustes.", ToolTipIcon.Info);
            }
            if (failed.Count > 0) WarnHotkeys(failed);
            Sync();
        }

        // ───────────────────────────── Menús ─────────────────────────────

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
            foreach (int percent in new[] { 100, 90, 80, 70, 60, 50, 40, 30 })
            {
                int p = percent;
                var item = new ToolStripMenuItem(p + " %", null, delegate { SetOpacityPercent(menuTarget, p); });
                item.Tag = p;
                opacityItems.Add(item);
                menu.Items.Add(item);
            }

            menu.Items.Add(new ToolStripSeparator());
            var resetItem = new ToolStripMenuItem("Volver a la posición automática", null, delegate { ResetPosition(menuTarget); });
            menu.Items.Add(resetItem);
            menu.Items.Add(new ToolStripMenuItem("Ajustes de PinTop…", null, delegate { ShowSettings(); }));

            menu.Opening += (s, e) =>
            {
                menuTarget = target;
                if (!Native.IsWindow(menuTarget)) { e.Cancel = true; return; }
                pinItem.Text = WindowManager.IsPinned(menuTarget) ? "Soltar ventana" : "Fijar encima de todo";
                int currentPercent = (int)Math.Round(WindowManager.GetOpacity(menuTarget) * 100 / 255.0);
                foreach (ToolStripMenuItem item in opacityItems)
                    item.Checked = Math.Abs((int)item.Tag - currentPercent) < 5;
                string app = current != null ? current.App : null;
                resetItem.Enabled = app != null && settings.Offsets.ContainsKey(app);
            };
            return menu;
        }

        // ───────────────────────── Seguimiento de ventanas ─────────────────────────

        void AddHook(uint min, uint max)
        {
            IntPtr hook = Native.SetWinEventHook(min, max, IntPtr.Zero, winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero) hooks.Add(hook);
        }

        void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (idObject != Native.OBJID_WINDOW || idChild != 0) return;
            if (eventType == Native.EVENT_OBJECT_DESTROY) placements.Remove(hwnd);
            if (eventType == Native.EVENT_SYSTEM_FOREGROUND || hwnd == target) Sync();
        }

        /// <summary>Coloca, esconde o recalcula el botón según la ventana activa.</summary>
        void Sync()
        {
            if (!settings.ShowButton || settingsForm != null)
            {
                Reset();
                return;
            }

            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && WindowManager.IsOwnWindow(fg))
            {
                if (buttonMenu.Visible) return;   // nuestro menú contextual está abierto
                Reset();
                return;
            }
            if (!WindowManager.IsEligible(fg))
            {
                Reset();
                return;
            }

            Rectangle frame = WindowManager.GetFrameRect(fg);
            Placement known;
            if (placements.TryGetValue(fg, out known) && known.FrameSize == frame.Size)
            {
                target = fg;
                current = known;
                pendingHwnd = IntPtr.Zero;
                ShowButton(frame);
                return;
            }

            // Ya se está calculando para esta ventana con este tamaño: esperar.
            if (pendingHwnd == fg && pendingSize == frame.Size
                && (settleTimer.Enabled || probeTimeout.Enabled || captureTimer.Enabled))
                return;

            // Ventana nueva o que ha cambiado de tamaño: esconder y recalcular cuando se quede quieta.
            target = fg;
            current = null;
            pendingHwnd = fg;
            pendingSize = frame.Size;
            button.HideButton();
            probeTimeout.Stop();
            captureTimer.Stop();
            settleTimer.Stop();
            settleTimer.Start();
        }

        void Reset()
        {
            target = IntPtr.Zero;
            current = null;
            pendingHwnd = IntPtr.Zero;
            settleTimer.Stop();
            probeTimeout.Stop();
            captureTimer.Stop();
            button.HideButton();
        }

        bool StillPending()
        {
            if (pendingHwnd == IntPtr.Zero || pendingHwnd != target) return false;
            if (Native.GetForegroundWindow() != pendingHwnd || !WindowManager.IsEligible(pendingHwnd)) return false;
            return WindowManager.GetFrameRect(pendingHwnd).Size == pendingSize;
        }

        void StartProbe()
        {
            if (!StillPending()) return;
            ticket++;
            probeResult = null;
            probe.Request(ticket, pendingHwnd, WindowManager.GetFrameRect(pendingHwnd), WindowManager.GetScale(pendingHwnd));
            probeTimeout.Start();
        }

        void OnProbeResult(int t, Rectangle? result)
        {
            if (t != ticket) return;   // respuesta de una petición antigua
            if (probeTimeout.Enabled)
            {
                probeTimeout.Stop();
                probeResult = result;
                captureTimer.Start();
                return;
            }
            // Llegó tarde: ya se colocó con la estimación. Si UIA sabe más, recolocar.
            Placement known;
            if (result.HasValue && target != IntPtr.Zero && placements.TryGetValue(target, out known) && !known.FromUia)
            {
                placements.Remove(target);
                probeResult = result;
                pendingHwnd = target;
                pendingSize = WindowManager.GetFrameRect(target).Size;
                current = null;
                button.HideButton();
                captureTimer.Start();
            }
        }

        /// <summary>Decide la posición final: botones reales → hueco libre en la barra → ajuste manual del usuario.</summary>
        void FinishPlacement()
        {
            if (!StillPending()) return;
            IntPtr hwnd = pendingHwnd;
            Rectangle frame = WindowManager.GetFrameRect(hwnd);
            float scale = WindowManager.GetScale(hwnd);

            Rectangle caption;
            bool fromUia = probeResult.HasValue;
            if (fromUia) caption = probeResult.Value;
            else if (!WindowManager.TryGetDwmCaption(hwnd, out caption))
            {
                caption = WindowManager.EstimateCaption(frame, scale);
                int anchor = ScreenScan.RefineEstimatedAnchor(caption, frame.Right, scale);
                caption = Rectangle.FromLTRB(anchor, caption.Top, caption.Right, caption.Bottom);
            }

            int size = (int)Math.Round(settings.ButtonSize * scale);
            size = Math.Max((int)Math.Round(16 * scale), Math.Min(size, caption.Height - (int)Math.Round(4 * scale)));
            int gap = (int)Math.Round(4 * scale);
            int y = Math.Max(frame.Top, caption.Top + (caption.Height - size) / 2);

            Color background;
            int x = ScreenScan.FindFreeSlot(caption.Left, y, size, gap, frame.Left, scale, out background);

            string app = WindowManager.GetAppName(hwnd);
            int offset;
            if (app != null && settings.Offsets.TryGetValue(app, out offset))
                x = frame.Right - (int)Math.Round(offset * scale);
            x = Math.Max(frame.Left, Math.Min(frame.Right - size, x));
            Debug.Log("place app=" + app + " class=" + WindowManager.GetClassName(hwnd) + " frame=" + frame
                + " caption=" + caption + (fromUia ? " (UIA)" : " (DWM/estimada)") + " x=" + x + " y=" + y + " size=" + size);

            var placement = new Placement();
            placement.FrameSize = frame.Size;
            placement.FromRight = frame.Right - x;
            placement.FromTop = y - frame.Top;
            placement.Size = size;
            placement.FromUia = fromUia;
            placement.App = app;

            if (placements.Count > 200) PrunePlacements();
            placements[hwnd] = placement;
            current = placement;
            pendingHwnd = IntPtr.Zero;
            button.SetBaseColor(background);
            ShowButton(frame);
        }

        void ShowButton(Rectangle frame)
        {
            if (current == null) return;
            button.DragMin = frame.Left;
            button.DragMax = frame.Right - current.Size;
            button.ShowAt(new Rectangle(frame.Right - current.FromRight, frame.Top + current.FromTop, current.Size, current.Size),
                WindowManager.IsPinned(target));
        }

        void PrunePlacements()
        {
            foreach (IntPtr hwnd in new List<IntPtr>(placements.Keys))
                if (!Native.IsWindow(hwnd)) placements.Remove(hwnd);
        }

        /// <summary>Recalcula la posición del botón en todas las ventanas (p. ej. tras cambiar ajustes).</summary>
        void InvalidatePlacements()
        {
            placements.Clear();
            Reset();
            Sync();
        }

        void OnDragEnded(int left)
        {
            if (target == IntPtr.Zero || current == null || current.App == null) return;
            Rectangle frame = WindowManager.GetFrameRect(target);
            float scale = WindowManager.GetScale(target);
            settings.Offsets[current.App] = (int)Math.Round((frame.Right - left) / scale);
            settings.Save();
            InvalidatePlacements();   // aplica la nueva posición a todas las ventanas de esa app
        }

        void ResetPosition(IntPtr hwnd)
        {
            if (current == null || current.App == null) return;
            settings.Offsets.Remove(current.App);
            settings.Save();
            InvalidatePlacements();
        }

        // ───────────────────────────── Acciones ─────────────────────────────

        void TogglePin(IntPtr hwnd)
        {
            if (!WindowManager.IsEligible(hwnd)) return;
            if (!WindowManager.TogglePin(hwnd))
                Warn("No se pudo cambiar esta ventana. Si la aplicación se ejecuta como administrador, ejecuta también PinTop como administrador.");
            Sync();
        }

        void SetOpacityPercent(IntPtr hwnd, int percent)
        {
            if (!Native.IsWindow(hwnd)) return;
            percent = Math.Max(20, Math.Min(100, percent));
            if (!WindowManager.SetOpacity(hwnd, (byte)Math.Round(percent * 2.55)))
                Warn("Esta ventana no admite transparencia.");
        }

        void StepOpacity(IntPtr hwnd, int direction)
        {
            if (!WindowManager.IsEligible(hwnd)) return;
            int percent = (int)Math.Round(WindowManager.GetOpacity(hwnd) * 100 / 255.0 / 10.0) * 10;
            SetOpacityPercent(hwnd, percent + direction * 10);
        }

        void OnHotkey(int id)
        {
            IntPtr fg = Native.GetForegroundWindow();
            switch ((HotkeyAction)(id - 1))
            {
                case HotkeyAction.TogglePin: TogglePin(fg); break;
                case HotkeyAction.OpacityDown: StepOpacity(fg, -1); break;
                case HotkeyAction.OpacityUp: StepOpacity(fg, +1); break;
                case HotkeyAction.ToggleButton: SetShowButton(!settings.ShowButton); break;
            }
        }

        void SetShowButton(bool value)
        {
            settings.ShowButton = value;
            showButtonItem.Checked = value;
            settings.Save();
            Sync();
        }

        List<string> RegisterHotkeys()
        {
            hotkeys.UnregisterAll();
            var failed = new List<string>();
            foreach (KeyValuePair<HotkeyAction, Hotkey> pair in settings.Hotkeys)
                if (!hotkeys.Register((int)pair.Key + 1, pair.Value)) failed.Add(pair.Value.ToString());

            Hotkey pin;
            settings.Hotkeys.TryGetValue(HotkeyAction.TogglePin, out pin);
            button.SetHotkeyText(pin.IsEmpty ? "" : pin.ToString());
            return failed;
        }

        void WarnHotkeys(List<string> failed)
        {
            Warn("Otra aplicación ya usa " + string.Join(", ", failed.ToArray())
                + ". Cámbialo en Ajustes (doble clic en el icono de PinTop).");
        }

        void ShowSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            hotkeys.UnregisterAll();   // para poder capturar las combinaciones actuales en la ventana
            settingsForm = new SettingsForm(settings);
            Sync();
            settingsForm.FormClosed += delegate
            {
                bool saved = settingsForm.DialogResult == DialogResult.OK;
                Settings result = settingsForm.Result;
                settingsForm.Dispose();
                settingsForm = null;
                if (saved)
                {
                    settings.ShowButton = result.ShowButton;
                    settings.ButtonSize = result.ButtonSize;
                    settings.Hotkeys.Clear();
                    foreach (KeyValuePair<HotkeyAction, Hotkey> pair in result.Hotkeys) settings.Hotkeys[pair.Key] = pair.Value;
                    settings.Offsets.Clear();
                    foreach (KeyValuePair<string, int> pair in result.Offsets) settings.Offsets[pair.Key] = pair.Value;
                    settings.Save();
                    showButtonItem.Checked = settings.ShowButton;
                }
                List<string> failed = RegisterHotkeys();
                if (failed.Count > 0) WarnHotkeys(failed);
                InvalidatePlacements();
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        void ListenForSignal(string name, Action action)
        {
            try
            {
                var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
                signals.Add(signal);
                waits.Add(ThreadPool.RegisterWaitForSingleObject(signal, delegate
                {
                    try
                    {
                        if (!button.IsDisposed) button.BeginInvoke(action);
                    }
                    catch (InvalidOperationException) { }
                }, null, -1, false));
            }
            catch (Exception) { }
        }

        void Warn(string message)
        {
            tray.ShowBalloonTip(4000, "PinTop", message, ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            syncTimer.Stop();
            settleTimer.Stop();
            probeTimeout.Stop();
            captureTimer.Stop();
            foreach (RegisteredWaitHandle w in waits) w.Unregister(null);
            foreach (WaitHandle s in signals) s.Close();
            foreach (IntPtr hook in hooks) Native.UnhookWinEvent(hook);
            hooks.Clear();
            hotkeys.Dispose();
            probe.Dispose();
            WindowManager.RestoreAll();   // nada se queda fijado ni transparente al salir
            if (settingsForm != null) settingsForm.Close();
            tray.Visible = false;
            tray.Dispose();
            button.Close();
            base.ExitThreadCore();
        }
    }
}
