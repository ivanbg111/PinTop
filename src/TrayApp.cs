using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace PinTop
{
    /// <summary>
    /// Núcleo de la app: icono de bandeja, seguimiento de ventanas, un botón flotante por
    /// ventana (la activa + todas las fijadas) y atajos de teclado globales.
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
            public Color Background;
        }

        public const string ShowSettingsEvent = "PinTop.ShowSettings";
        public const string ExitEvent = "PinTop.Exit";

        readonly Settings settings;
        readonly NotifyIcon tray;
        readonly Control invoker = new Control();   // para volver al hilo de la interfaz
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
        readonly Dictionary<IntPtr, PinButton> buttons = new Dictionary<IntPtr, PinButton>();
        readonly Stack<PinButton> spareButtons = new Stack<PinButton>();
        readonly HashSet<IntPtr> buttonHandles = new HashSet<IntPtr>();
        readonly ToolStripMenuItem showButtonItem;
        SettingsForm settingsForm;

        IntPtr pendingHwnd = IntPtr.Zero;  // ventana cuya colocación se está calculando
        Size pendingSize;
        int ticket;
        Rectangle? probeResult;
        IntPtr menuTarget = IntPtr.Zero;
        string hotkeyText = "";

        public TrayApp()
        {
            settings = Settings.Load();
            try { Startup.RefreshPath(); } catch (Exception) { }

            invoker.CreateControl();
            buttonMenu = BuildButtonMenu();

            probe = new CaptionProbe();
            probe.Completed += (t, hwnd, result) => Post(() => OnProbeResult(t, hwnd, result));

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

            // Red de seguridad por si se pierde algún evento (y para reordenar capas).
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

        void Post(Action action)
        {
            try
            {
                if (!invoker.IsDisposed) invoker.BeginInvoke(action);
            }
            catch (InvalidOperationException) { }
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
                var source = menu.SourceControl as PinButton;
                menuTarget = source != null ? source.Target : IntPtr.Zero;
                if (!Native.IsWindow(menuTarget)) { e.Cancel = true; return; }
                pinItem.Text = WindowManager.IsPinned(menuTarget) ? "Soltar ventana" : "Fijar encima de todo";
                int currentPercent = (int)Math.Round(WindowManager.GetOpacity(menuTarget) * 100 / 255.0);
                foreach (ToolStripMenuItem item in opacityItems)
                    item.Checked = Math.Abs((int)item.Tag - currentPercent) < 5;
                Placement placement;
                placements.TryGetValue(menuTarget, out placement);
                resetItem.Enabled = placement != null && placement.App != null && settings.Offsets.ContainsKey(placement.App);
            };
            return menu;
        }

        // ───────────────────────────── Botones ─────────────────────────────

        PinButton ButtonFor(IntPtr hwnd)
        {
            PinButton b;
            if (buttons.TryGetValue(hwnd, out b)) return b;
            if (spareButtons.Count > 0)
            {
                b = spareButtons.Pop();
            }
            else
            {
                b = new PinButton();
                b.PinClicked += sender => TogglePin(sender.Target);
                b.DragEnded += OnDragEnded;
                b.ContextMenuStrip = buttonMenu;
                buttonHandles.Add(b.Handle);
            }
            b.Target = hwnd;
            b.SetHotkeyText(hotkeyText);
            buttons[hwnd] = b;
            return b;
        }

        void ReleaseButton(IntPtr hwnd)
        {
            PinButton b;
            if (!buttons.TryGetValue(hwnd, out b)) return;
            b.HideButton();
            b.Target = IntPtr.Zero;
            buttons.Remove(hwnd);
            if (spareButtons.Count < 4)
            {
                spareButtons.Push(b);
            }
            else
            {
                buttonHandles.Remove(b.Handle);
                b.Close();
                b.Dispose();
            }
        }

        void ReleaseAllButtons()
        {
            foreach (IntPtr hwnd in new List<IntPtr>(buttons.Keys)) ReleaseButton(hwnd);
        }

        /// <summary>
        /// La ventana más cercana por encima de <paramref name="hwnd"/> que de verdad tapa la zona
        /// del botón (o Zero). Se ignoran nuestras ventanas, las invisibles, las transparentes al
        /// ratón y las auxiliares del sistema (que viven en otras "bandas" de Windows).
        /// </summary>
        IntPtr CoveringWindow(IntPtr hwnd, Rectangle buttonBounds)
        {
            IntPtr prev = Native.GetWindow(hwnd, Native.GW_HWNDPREV);
            while (prev != IntPtr.Zero && !CoversButton(prev, buttonBounds))
                prev = Native.GetWindow(prev, Native.GW_HWNDPREV);
            return prev;
        }

        bool AnyButtonIntersects(Rectangle area)
        {
            foreach (PinButton b in buttons.Values)
                if (b.IsShown && b.Bounds.IntersectsWith(area)) return true;
            return false;
        }

        bool CoversButton(IntPtr window, Rectangle buttonBounds)
        {
            if (buttonHandles.Contains(window) || !Native.IsWindowVisible(window) || WindowManager.IsOwnWindow(window))
                return false;
            if ((Native.GetWindowLong(window, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0) return false;
            if (WindowManager.IsCloaked(window)) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(window, out r) || r.Width <= 1 || r.Height <= 1) return false;
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).IntersectsWith(buttonBounds);
        }

        void ShowButtonFor(IntPtr hwnd, Rectangle frame, Placement p)
        {
            PinButton b = ButtonFor(hwnd);
            b.SetBaseColor(p.Background);
            b.DragMin = frame.Left;
            b.DragMax = frame.Right - p.Size;
            bool pinned = WindowManager.IsPinned(hwnd);
            var bounds = new Rectangle(frame.Right - p.FromRight, frame.Top + p.FromTop, p.Size, p.Size);

            // Los botones viven siempre en la capa "siempre visible" (Windows no deja a una app en
            // segundo plano subir ventanas por encima de la activa en la capa normal).
            IntPtr cover = CoveringWindow(hwnd, bounds);
            IntPtr insertAfter;
            if (cover == IntPtr.Zero)
            {
                insertAfter = Native.HWND_TOPMOST;
            }
            else if (pinned && (Native.GetWindowLong(cover, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0)
            {
                insertAfter = cover;   // justo debajo de la ventana fijada que la tapa
            }
            else
            {
                b.HideButton();        // la tapa otra ventana fijada: el botón no debe verse
                return;
            }
            b.ShowAt(bounds, pinned, insertAfter);
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
            if (eventType == Native.EVENT_OBJECT_DESTROY)
            {
                placements.Remove(hwnd);
                ReleaseButton(hwnd);
            }
            if (eventType == Native.EVENT_SYSTEM_FOREGROUND || buttons.ContainsKey(hwnd) || hwnd == pendingHwnd)
            {
                Sync();
            }
            else if (settings.AllWindows && Native.GetAncestor(hwnd, Native.GA_ROOT) == hwnd)
            {
                // Otra ventana se movió, apareció o desapareció: puede tapar o destapar botones.
                RequestSync();
            }
        }

        bool syncQueued;

        /// <summary>Agrupa muchos eventos seguidos en una sola sincronización.</summary>
        void RequestSync()
        {
            if (syncQueued) return;
            syncQueued = true;
            Post(() => { syncQueued = false; Sync(); });
        }

        /// <summary>
        /// Muestra un botón en cada ventana que lo necesita (todas las visibles, o solo la activa y
        /// las fijadas, según ajustes) y quita los demás.
        /// </summary>
        void Sync()
        {
            if (!settings.ShowButton || settingsForm != null)
            {
                CancelPending();
                ReleaseAllButtons();
                return;
            }
            if (buttonMenu.Visible) return;   // no mover nada mientras el menú del botón está abierto

            IntPtr fg = Native.GetForegroundWindow();
            var wanted = new List<IntPtr>();
            if (WindowManager.IsEligible(fg)) wanted.Add(fg);
            foreach (IntPtr hwnd in WindowManager.PinnedWindows())
                if (hwnd != fg && WindowManager.IsEligible(hwnd)) wanted.Add(hwnd);
            if (settings.AllWindows)
                foreach (IntPtr hwnd in WindowManager.AppWindows())
                    if (!wanted.Contains(hwnd)) wanted.Add(hwnd);

            IntPtr needs = IntPtr.Zero;
            Size needsSize = Size.Empty;
            var shown = new HashSet<IntPtr>();
            foreach (IntPtr hwnd in wanted)
            {
                Rectangle frame = WindowManager.GetFrameRect(hwnd);
                Placement p;
                if (placements.TryGetValue(hwnd, out p) && p.FrameSize == frame.Size)
                {
                    ShowButtonFor(hwnd, frame, p);
                    shown.Add(hwnd);
                }
                else if (needs == IntPtr.Zero)
                {
                    // Las ventanas de fondo con la esquina tapada aún no necesitan botón:
                    // se calculará cuando se destapen. La activa y las fijadas, siempre.
                    bool important = hwnd == fg || WindowManager.IsPinned(hwnd);
                    Rectangle corner = WindowManager.EstimateCaption(frame, WindowManager.GetScale(hwnd));
                    corner = Rectangle.FromLTRB(corner.Left - corner.Width, corner.Top, corner.Right, corner.Bottom);
                    if (!important && CoveringWindow(hwnd, corner) != IntPtr.Zero) continue;
                    needs = hwnd;   // la activa tiene prioridad (va primera en la lista)
                    needsSize = frame.Size;
                }
            }
            foreach (IntPtr hwnd in new List<IntPtr>(buttons.Keys))
                if (!shown.Contains(hwnd)) ReleaseButton(hwnd);

            if (needs == IntPtr.Zero)
            {
                CancelPending();
                return;
            }
            // Ya se está calculando para esta ventana con este tamaño: esperar.
            if (pendingHwnd == needs && pendingSize == needsSize
                && (settleTimer.Enabled || probeTimeout.Enabled || captureTimer.Enabled))
                return;

            // Ventana nueva o que ha cambiado de tamaño: recalcular cuando se quede quieta.
            pendingHwnd = needs;
            pendingSize = needsSize;
            probeTimeout.Stop();
            captureTimer.Stop();
            settleTimer.Stop();
            settleTimer.Start();
        }

        void CancelPending()
        {
            pendingHwnd = IntPtr.Zero;
            settleTimer.Stop();
            probeTimeout.Stop();
            captureTimer.Stop();
        }

        bool StillPending()
        {
            return pendingHwnd != IntPtr.Zero && WindowManager.IsEligible(pendingHwnd)
                && WindowManager.GetFrameRect(pendingHwnd).Size == pendingSize;
        }

        void StartProbe()
        {
            if (!StillPending()) return;
            ticket++;
            probeResult = null;
            probe.Request(ticket, pendingHwnd, WindowManager.GetFrameRect(pendingHwnd), WindowManager.GetScale(pendingHwnd));
            probeTimeout.Start();
        }

        void OnProbeResult(int t, IntPtr hwnd, Rectangle? result)
        {
            if (t != ticket) return;   // respuesta de una petición antigua
            if (probeTimeout.Enabled && hwnd == pendingHwnd)
            {
                probeTimeout.Stop();
                probeResult = result;
                captureTimer.Start();
                return;
            }
            // Llegó tarde: ya se colocó con la estimación. Si UIA sabe más, recolocar.
            Placement known;
            if (result.HasValue && placements.TryGetValue(hwnd, out known) && !known.FromUia && WindowManager.IsEligible(hwnd))
            {
                placements.Remove(hwnd);
                ReleaseButton(hwnd);
                probeResult = result;
                pendingHwnd = hwnd;
                pendingSize = WindowManager.GetFrameRect(hwnd).Size;
                settleTimer.Stop();
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
            bool estimated = false;
            if (fromUia) caption = probeResult.Value;
            else if (!WindowManager.TryGetDwmCaption(hwnd, out caption))
            {
                caption = WindowManager.EstimateCaption(frame, scale);
                estimated = true;
            }

            // ¿De dónde sacamos la imagen de la barra de título? De la pantalla si se ve entera
            // (es lo más fiel); si otra ventana o un botón nuestro la tapa, se la pedimos a la ventana.
            Rectangle titleRow = Rectangle.FromLTRB(frame.Left, caption.Top, frame.Right, caption.Bottom);
            bool fromScreen = CoveringWindow(hwnd, titleRow) == IntPtr.Zero && !AnyButtonIntersects(titleRow);
            WindowGrab windowGrab = fromScreen ? null : new WindowGrab(hwnd);
            ScreenScan.Grab grab = fromScreen ? new ScreenScan.Grab(ScreenScan.FromScreen) : windowGrab.Grab;

            int size, y, x;
            Color background;
            try
            {
                if (estimated)
                {
                    int anchor = ScreenScan.RefineEstimatedAnchor(caption, frame.Right, scale, grab);
                    caption = Rectangle.FromLTRB(anchor, caption.Top, caption.Right, caption.Bottom);
                }

                size = (int)Math.Round(settings.ButtonSize * scale);
                size = Math.Max((int)Math.Round(16 * scale), Math.Min(size, caption.Height - (int)Math.Round(4 * scale)));
                int gap = (int)Math.Round(4 * scale);
                y = Math.Max(frame.Top, caption.Top + (caption.Height - size) / 2);
                x = ScreenScan.FindFreeSlot(caption.Left, y, size, gap, frame.Left, scale, grab, out background);
            }
            finally
            {
                if (windowGrab != null) windowGrab.Dispose();
            }

            string app = WindowManager.GetAppName(hwnd);
            int offset;
            if (app != null && settings.Offsets.TryGetValue(app, out offset))
                x = frame.Right - (int)Math.Round(offset * scale);
            x = Math.Max(frame.Left, Math.Min(frame.Right - size, x));
            Debug.Log("place app=" + app + " class=" + WindowManager.GetClassName(hwnd) + " frame=" + frame
                + " caption=" + caption + (fromUia ? " (UIA)" : " (DWM/estimada)") + (fromScreen ? " [pantalla]" : " [PrintWindow]")
                + " x=" + x + " y=" + y + " size=" + size);

            var placement = new Placement();
            placement.FrameSize = frame.Size;
            placement.FromRight = frame.Right - x;
            placement.FromTop = y - frame.Top;
            placement.Size = size;
            placement.FromUia = fromUia;
            placement.App = app;
            placement.Background = background;

            if (placements.Count > 200) PrunePlacements();
            placements[hwnd] = placement;
            pendingHwnd = IntPtr.Zero;
            Sync();   // muestra este botón y, si hace falta, calcula el de la siguiente ventana fijada
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
            CancelPending();
            ReleaseAllButtons();
            Sync();
        }

        void OnDragEnded(PinButton b, int left)
        {
            Placement p;
            if (!placements.TryGetValue(b.Target, out p) || p.App == null) return;
            Rectangle frame = WindowManager.GetFrameRect(b.Target);
            float scale = WindowManager.GetScale(b.Target);
            settings.Offsets[p.App] = (int)Math.Round((frame.Right - left) / scale);
            settings.Save();
            InvalidatePlacements();   // aplica la nueva posición a todas las ventanas de esa app
        }

        void ResetPosition(IntPtr hwnd)
        {
            Placement p;
            if (!placements.TryGetValue(hwnd, out p) || p.App == null) return;
            settings.Offsets.Remove(p.App);
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
            hotkeyText = pin.IsEmpty ? "" : pin.ToString();
            foreach (PinButton b in buttons.Values) b.SetHotkeyText(hotkeyText);
            foreach (PinButton b in spareButtons) b.SetHotkeyText(hotkeyText);
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
                    settings.AllWindows = result.AllWindows;
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
                waits.Add(ThreadPool.RegisterWaitForSingleObject(signal, delegate { Post(action); }, null, -1, false));
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
            CancelPending();
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
            ReleaseAllButtons();
            foreach (PinButton b in spareButtons) b.Close();
            invoker.Dispose();
            base.ExitThreadCore();
        }
    }
}
