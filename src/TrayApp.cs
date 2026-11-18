using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace PinTop
{
    /// <summary>
    /// Núcleo de la app: icono de bandeja, seguimiento de ventanas, un botón flotante por
    /// ventana, atajos globales, fijado automático, atenuación, borde y modo fantasma.
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
        readonly ContextMenuStrip trayMenu;
        readonly ContextMenuStrip buttonMenu;
        readonly HotkeyWindow hotkeys;
        readonly CaptionProbe probe;
        readonly Timer syncTimer = new Timer();
        readonly Timer settleTimer = new Timer();
        readonly Timer probeTimeout = new Timer();
        readonly Timer captureTimer = new Timer();
        readonly Timer fadeTimer = new Timer();
        readonly Timer updateTimer = new Timer();
        readonly Native.WinEventDelegate winEventProc;   // referencia viva para que el GC no la recoja
        readonly List<IntPtr> hooks = new List<IntPtr>();
        readonly List<RegisteredWaitHandle> waits = new List<RegisteredWaitHandle>();
        readonly List<WaitHandle> signals = new List<WaitHandle>();
        readonly Dictionary<IntPtr, Placement> placements = new Dictionary<IntPtr, Placement>();
        readonly Dictionary<IntPtr, PinButton> buttons = new Dictionary<IntPtr, PinButton>();
        readonly Stack<PinButton> spareButtons = new Stack<PinButton>();
        readonly HashSet<IntPtr> buttonHandles = new HashSet<IntPtr>();
        readonly Dictionary<IntPtr, string> appNames = new Dictionary<IntPtr, string>();
        readonly HashSet<IntPtr> autoPinChecked = new HashSet<IntPtr>();
        readonly HashSet<IntPtr> faded = new HashSet<IntPtr>();
        SettingsForm settingsForm;

        // Menú de la bandeja (partes dinámicas)
        ToolStripMenuItem showButtonItem, pinnedListItem, ghostsOffItem, updateItem;

        IntPtr pendingHwnd = IntPtr.Zero;  // ventana cuya colocación se está calculando
        Size pendingSize;
        int ticket;
        Rectangle? probeResult;
        IntPtr menuTarget = IntPtr.Zero;
        string hotkeyText = "";
        Color accent = Theme.Accent();
        int ticks;
        bool ghostHintShown;
        bool capturingHotkey;   // la ventana de Ajustes está escribiendo un atajo
        bool exiting;           // la app se está cerrando
        string newVersion;

        public TrayApp()
        {
            settings = Settings.Load();
            Theme.Mode = settings.ThemeMode;
            try { Startup.RefreshPath(); } catch (Exception) { }

            invoker.CreateControl();
            buttonMenu = BuildButtonMenu();
            trayMenu = BuildTrayMenu();

            probe = new CaptionProbe();
            probe.Completed += (t, hwnd, result) => Post(() => OnProbeResult(t, hwnd, result));

            tray = new NotifyIcon();
            tray.Icon = Icons.CreateTrayIcon();
            tray.Text = "PinTop";
            tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.BalloonTipClicked += delegate { if (newVersion != null) OpenReleasePage(); };
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
            syncTimer.Tick += delegate
            {
                if (++ticks % 40 == 0) accent = Theme.Accent();   // por si cambias el color de Windows
                Sync();
            };
            syncTimer.Start();

            // Espera a que la ventana deje de moverse/redimensionarse antes de calcular la posición.
            settleTimer.Interval = 180;
            settleTimer.Tick += delegate { settleTimer.Stop(); StartProbe(); };
            probeTimeout.Interval = 700;
            probeTimeout.Tick += delegate { probeTimeout.Stop(); probeResult = null; FinishPlacement(); };
            captureTimer.Interval = 60;
            captureTimer.Tick += delegate { captureTimer.Stop(); FinishPlacement(); };

            // Atenuación automática de las ventanas fijadas (solo si se activa en Ajustes).
            fadeTimer.Interval = 120;
            fadeTimer.Tick += delegate { FadeTick(); };
            UpdateFadeTimer();

            // Actualizaciones: al poco de arrancar y luego una vez al día.
            updateTimer.Interval = Debug.Enabled && Environment.GetEnvironmentVariable("PINTOP_FAKE_VERSION") != null ? 2000 : 15000;
            updateTimer.Tick += delegate
            {
                updateTimer.Interval = 24 * 60 * 60 * 1000;
                Debug.Log("update timer: comprobar=" + settings.CheckUpdates);
                if (settings.CheckUpdates) Updater.CheckAsync(v => Post(() => OnNewVersion(v)));
            };
            updateTimer.Start();

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

        string AppOf(IntPtr hwnd)
        {
            string app;
            if (!appNames.TryGetValue(hwnd, out app))
            {
                app = WindowManager.GetAppName(hwnd);
                appNames[hwnd] = app;
            }
            return app;
        }

        // ───────────────────────────── Menús ─────────────────────────────

        ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();
            var settingsItem = new ToolStripMenuItem("Ajustes…", null, delegate { ShowSettings(); });
            settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold);
            menu.Items.Add(settingsItem);

            showButtonItem = new ToolStripMenuItem("Mostrar el botón en las ventanas", null, delegate { SetShowButton(!settings.ShowButton); });
            menu.Items.Add(showButtonItem);
            menu.Items.Add(new ToolStripSeparator());

            pinnedListItem = new ToolStripMenuItem("Ventanas fijadas");
            menu.Items.Add(pinnedListItem);
            ghostsOffItem = new ToolStripMenuItem("Quitar el modo fantasma a todas", null, delegate
            {
                foreach (IntPtr hwnd in WindowManager.GhostWindows()) WindowManager.SetGhost(hwnd, false);
                Sync();
            });
            menu.Items.Add(ghostsOffItem);
            menu.Items.Add(new ToolStripMenuItem("Soltar todas las ventanas fijadas", null, delegate { UnpinAll(); }));

            updateItem = new ToolStripMenuItem("", null, delegate { OpenReleasePage(); });
            updateItem.Visible = false;
            menu.Items.Add(updateItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Salir", null, delegate { ExitThread(); }));

            menu.Opening += delegate
            {
                Theme.StyleMenu(menu);
                Theme.StyleMenu(pinnedListItem.DropDown);
                showButtonItem.Checked = settings.ShowButton;
                ghostsOffItem.Visible = WindowManager.GhostWindows().Count > 0;
                RebuildPinnedList();
            };
            return menu;
        }

        /// <summary>Lista de ventanas fijadas: clic en una para soltarla.</summary>
        void RebuildPinnedList()
        {
            var oldItems = new List<ToolStripItem>();
            foreach (ToolStripItem old in pinnedListItem.DropDownItems) oldItems.Add(old);
            pinnedListItem.DropDownItems.Clear();
            foreach (ToolStripItem old in oldItems) old.Dispose();
            List<IntPtr> pinned = WindowManager.PinnedWindows();
            pinnedListItem.Text = "Ventanas fijadas (" + pinned.Count + ")";
            pinnedListItem.Enabled = pinned.Count > 0;
            foreach (IntPtr hwnd in pinned)
            {
                IntPtr h = hwnd;
                string title = WindowManager.GetTitle(h);
                if (title.Length == 0) title = AppOf(h) ?? "(sin título)";
                if (title.Length > 60) title = title.Substring(0, 57) + "…";
                if (WindowManager.IsGhost(h)) title += "  (fantasma)";
                var item = new ToolStripMenuItem(title, null, delegate { Unpin(h); });
                item.ToolTipText = "Clic para soltarla";
                pinnedListItem.DropDownItems.Add(item);
            }
        }

        ContextMenuStrip BuildButtonMenu()
        {
            var menu = new ContextMenuStrip();
            var pinItem = new ToolStripMenuItem("Fijar encima de todo", null, delegate { TogglePin(menuTarget); });
            menu.Items.Add(pinItem);
            var ghostItem = new ToolStripMenuItem("Modo fantasma (los clics la atraviesan)", null, delegate
            {
                SetGhost(menuTarget, !WindowManager.IsGhost(menuTarget));
            });
            menu.Items.Add(ghostItem);
            menu.Items.Add(new ToolStripSeparator());
            var title = new ToolStripMenuItem("Opacidad (o rueda del ratón sobre el botón)");
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
            var alwaysItem = new ToolStripMenuItem("Fijar siempre esta app", null, delegate { ToggleAutoPin(menuTarget); });
            menu.Items.Add(alwaysItem);
            var excludeItem = new ToolStripMenuItem("No mostrar el botón en esta app", null, delegate { ExcludeApp(menuTarget); });
            menu.Items.Add(excludeItem);
            // Solo aparece si arrastraste el botón a mano en esta app.
            var resetItem = new ToolStripMenuItem("Devolver el botón a su sitio (deshacer arrastre)", null, delegate { ResetPosition(menuTarget); });
            menu.Items.Add(resetItem);
            menu.Items.Add(new ToolStripMenuItem("Ajustes de PinTop…", null, delegate { ShowSettings(); }));

            menu.Opening += (s, e) =>
            {
                var source = menu.SourceControl as PinButton;
                menuTarget = source != null ? source.Target : IntPtr.Zero;
                menuForeground = Native.GetForegroundWindow();
                if (!Native.IsWindow(menuTarget)) { e.Cancel = true; return; }
                Theme.StyleMenu(menu);
                string app = AppOf(menuTarget);
                pinItem.Text = WindowManager.IsPinned(menuTarget) ? "Soltar ventana" : "Fijar encima de todo";
                ghostItem.Checked = WindowManager.IsGhost(menuTarget);
                int currentPercent = (int)Math.Round(WindowManager.GetUserOpacity(menuTarget) * 100 / 255.0);
                foreach (ToolStripMenuItem item in opacityItems)
                    item.Checked = Math.Abs((int)item.Tag - currentPercent) < 5;
                alwaysItem.Text = "Fijar siempre " + (app ?? "esta app");
                alwaysItem.Checked = app != null && settings.AutoPinApps.Contains(app);
                alwaysItem.Enabled = excludeItem.Enabled = app != null;
                excludeItem.Text = "No mostrar el botón en " + (app ?? "esta app");
                resetItem.Visible = app != null && settings.Offsets.ContainsKey(app);
            };

            // PinTop nunca es la app activa (para no robar el foco), así que Windows no cierra
            // solo este menú al hacer clic fuera. Mientras está abierto, unos ganchos de ratón y
            // teclado detectan clics fuera y Esc, y un temporizador vigila cambios de ventana.
            menuMouseProc = OnMenuMouse;
            menuKeyProc = OnMenuKey;
            menu.Opened += delegate
            {
                // Por si acaso quedara algún gancho de una apertura anterior.
                if (menuHook != IntPtr.Zero) Native.UnhookWindowsHookEx(menuHook);
                if (menuKeyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(menuKeyHook);
                IntPtr module = Native.GetModuleHandle(null);
                menuHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, menuMouseProc, module, 0);
                menuKeyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, menuKeyProc, module, 0);
                menuWatch.Start();
            };
            // WinForms cree a veces que "la app perdió el foco" (PinTop nunca lo tiene) y cierra el
            // menú nada más abrirlo. Ese cierre se ignora: los ganchos de arriba ya lo cierran bien.
            menu.Closing += (s, e) =>
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange
                    && Native.GetForegroundWindow() == menuForeground)
                    e.Cancel = true;
            };
            menu.Closed += (s, e) =>
            {
                Debug.Log("menu closed reason=" + e.CloseReason);
                menuWatch.Stop();
                if (menuHook != IntPtr.Zero) Native.UnhookWindowsHookEx(menuHook);
                if (menuKeyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(menuKeyHook);
                menuHook = menuKeyHook = IntPtr.Zero;
            };
            menuWatch.Interval = 100;
            menuWatch.Tick += delegate
            {
                if (!menu.Visible) { menuWatch.Stop(); return; }
                if (Native.GetForegroundWindow() != menuForeground) menu.Close(ToolStripDropDownCloseReason.AppClicked);
            };
            return menu;
        }

        readonly Timer menuWatch = new Timer();
        Native.LowLevelMouseProc menuMouseProc, menuKeyProc;   // referencias vivas para el GC
        IntPtr menuHook = IntPtr.Zero, menuKeyHook = IntPtr.Zero;
        IntPtr menuForeground;

        IntPtr OnMenuMouse(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN)
                {
                    var info = (Native.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam, typeof(Native.MSLLHOOKSTRUCT));
                    var point = new Point(info.X, info.Y);
                    if (buttonMenu.Visible && !buttonMenu.Bounds.Contains(point))
                        Post(() => buttonMenu.Close(ToolStripDropDownCloseReason.AppClicked));
                }
            }
            return Native.CallNextHookEx(menuHook, nCode, wParam, lParam);
        }

        IntPtr OnMenuKey(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // El primer campo de KBDLLHOOKSTRUCT es la tecla virtual.
            if (nCode >= 0 && wParam.ToInt32() == Native.WM_KEYDOWN
                && System.Runtime.InteropServices.Marshal.ReadInt32(lParam) == Native.VK_ESCAPE)
                Post(() => buttonMenu.Close(ToolStripDropDownCloseReason.Keyboard));
            return Native.CallNextHookEx(menuKeyHook, nCode, wParam, lParam);
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
                b.Wheel += (sender, delta) => StepOpacity(sender.Target, delta > 0 ? +1 : -1);
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
            if (buttonHandles.Contains(window) || !Native.IsWindowVisible(window)) return false;
            // De nuestras ventanas solo tapan las que tienen barra de título (Ajustes y sus diálogos);
            // los menús y tooltips no.
            if (WindowManager.IsOwnWindow(window)
                && (Native.GetWindowLong(window, Native.GWL_STYLE) & Native.WS_CAPTION) != Native.WS_CAPTION)
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
                b.HideButton();        // la tapa otra ventana: el botón no debe verse
                return;
            }
            b.ShowAt(bounds, pinned, WindowManager.IsGhost(hwnd), insertAfter);
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
            if (idObject != Native.OBJID_WINDOW || idChild != 0 || exiting) return;
            if (eventType == Native.EVENT_OBJECT_DESTROY)
            {
                // Ventana cerrada: olvidar TODO lo que sabíamos de ella (si Windows reutiliza el
                // identificador, la ventana nueva no debe heredar fijado, opacidad ni fantasma).
                placements.Remove(hwnd);
                appNames.Remove(hwnd);
                autoPinChecked.Remove(hwnd);
                faded.Remove(hwnd);
                WindowManager.Forget(hwnd);
                ReleaseButton(hwnd);
            }
            if (eventType == Native.EVENT_SYSTEM_FOREGROUND || buttons.ContainsKey(hwnd) || hwnd == pendingHwnd)
            {
                Sync();
            }
            else if ((settings.AllWindows || settings.AutoPinApps.Count > 0) && Native.GetAncestor(hwnd, Native.GA_ROOT) == hwnd)
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
        /// Fija las apps marcadas como "fijar siempre", pone/quita bordes y muestra un botón en cada
        /// ventana que lo necesita (todas las visibles, o solo la activa y las fijadas).
        /// </summary>
        void Sync()
        {
            if (exiting) return;               // al salir no se vuelve a fijar ni a pintar nada
            if (buttonMenu.Visible) return;   // no mover nada mientras el menú del botón está abierto

            List<IntPtr> appWindows = (settings.AllWindows || settings.AutoPinApps.Count > 0)
                ? WindowManager.AppWindows() : null;
            if (settings.AutoPinApps.Count > 0) AutoPin(appWindows);
            List<IntPtr> pinned = WindowManager.PinnedWindows();
            WindowManager.SyncBorders(pinned, settings.PinnedBorder, accent);

            // Los botones siguen visibles aunque los Ajustes estén abiertos: así nunca queda una
            // ventana fijada sin forma de soltarla.
            if (!settings.ShowButton)
            {
                CancelPending();
                ReleaseAllButtons();
                return;
            }

            IntPtr fg = Native.GetForegroundWindow();
            var wanted = new List<IntPtr>();
            if (WindowManager.IsEligible(fg)) wanted.Add(fg);
            foreach (IntPtr hwnd in pinned)
                if (hwnd != fg && WindowManager.IsEligible(hwnd)) wanted.Add(hwnd);
            if (settings.AllWindows)
                foreach (IntPtr hwnd in appWindows)
                    if (!wanted.Contains(hwnd)) wanted.Add(hwnd);
            // Las apps excluidas no llevan botón... salvo sus ventanas fijadas, que siempre lo
            // necesitan para poder soltarlas.
            if (settings.ExcludedApps.Count > 0)
                wanted.RemoveAll(hwnd => !WindowManager.IsPinned(hwnd) && settings.ExcludedApps.Contains(AppOf(hwnd) ?? ""));

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

        /// <summary>Fija una vez cada ventana nueva de las apps marcadas como "fijar siempre".</summary>
        void AutoPin(List<IntPtr> appWindows)
        {
            foreach (IntPtr hwnd in appWindows)
            {
                if (autoPinChecked.Contains(hwnd)) continue;
                autoPinChecked.Add(hwnd);   // solo la primera vez: si luego la sueltas, se respeta
                string app = AppOf(hwnd);
                if (app != null && settings.AutoPinApps.Contains(app) && !WindowManager.IsPinned(hwnd))
                    WindowManager.TogglePin(hwnd);
            }
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

            // ¿De dónde sacamos la imagen de la barra de título? De la pantalla si se ve entera y
            // opaca (es lo más fiel); si otra ventana o un botón nuestro la tapa, o es translúcida
            // (se vería lo de detrás), se la pedimos a la propia ventana.
            Rectangle titleRow = Rectangle.FromLTRB(frame.Left, caption.Top, frame.Right, caption.Bottom);
            bool fromScreen = CoveringWindow(hwnd, titleRow) == IntPtr.Zero && !AnyButtonIntersects(titleRow)
                && WindowManager.GetOpacity(hwnd) == 255;
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

            string app = AppOf(hwnd);
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
            Sync();   // muestra este botón y, si hace falta, calcula el de la siguiente ventana
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
            NotifySettingsForm();
            InvalidatePlacements();   // aplica la nueva posición a todas las ventanas de esa app
        }

        void ResetPosition(IntPtr hwnd)
        {
            string app = AppOf(hwnd);
            if (app == null) return;
            settings.Offsets.Remove(app);
            settings.Save();
            NotifySettingsForm();
            InvalidatePlacements();
        }

        /// <summary>
        /// Si los Ajustes están abiertos, que recarguen los valores: así no "pisan" después un cambio
        /// hecho desde una chincheta, su menú o la bandeja.
        /// </summary>
        void NotifySettingsForm()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Reload(settings);
        }

        // ───────────────────────────── Acciones ─────────────────────────────

        void TogglePin(IntPtr hwnd)
        {
            if (!WindowManager.IsEligible(hwnd)) return;
            bool wasPinned = WindowManager.IsPinned(hwnd);
            if (!WindowManager.TogglePin(hwnd))
            {
                Warn("No se pudo cambiar esta ventana. Si la aplicación se ejecuta como administrador, ejecuta también PinTop como administrador.");
            }
            else if (wasPinned)
            {
                AfterUnpin(hwnd);
            }
            Sync();
        }

        void Unpin(IntPtr hwnd)
        {
            if (Native.IsWindow(hwnd) && WindowManager.IsPinned(hwnd) && WindowManager.TogglePin(hwnd)) AfterUnpin(hwnd);
            Sync();
        }

        void UnpinAll()
        {
            foreach (IntPtr hwnd in WindowManager.PinnedWindows()) AfterUnpin(hwnd);
            WindowManager.UnpinAll();
            Sync();
        }

        /// <summary>Una ventana soltada deja de ser fantasma (si no, quedaría "intocable" detrás de otras).</summary>
        void AfterUnpin(IntPtr hwnd)
        {
            if (WindowManager.IsGhost(hwnd)) WindowManager.SetGhost(hwnd, false);
            if (faded.Remove(hwnd)) WindowManager.SetOpacity(hwnd, WindowManager.GetUserOpacity(hwnd));
        }

        void SetGhost(IntPtr hwnd, bool on)
        {
            if (!Native.IsWindow(hwnd)) return;
            bool pinnedHere = false;
            if (on && !WindowManager.IsPinned(hwnd)) pinnedHere = WindowManager.TogglePin(hwnd);   // fantasma implica fijada
            if (!WindowManager.SetGhost(hwnd, on))
            {
                // No se pudo: no dejar la ventana fijada si la fijamos solo para esto.
                if (pinnedHere && WindowManager.IsPinned(hwnd)) WindowManager.TogglePin(hwnd);
                Warn("Esta ventana no admite el modo fantasma.");
            }
            else if (on && !ghostHintShown)
            {
                ghostHintShown = true;
                Hotkey hk;
                settings.Hotkeys.TryGetValue(HotkeyAction.Ghost, out hk);
                tray.ShowBalloonTip(5000, "Modo fantasma activado",
                    "Los clics atraviesan esta ventana. Para quitarlo: clic derecho en su chincheta"
                    + (hk.IsEmpty ? "" : " o " + hk) + ".", ToolTipIcon.Info);
            }
            Sync();
        }

        /// <summary>Atajo del modo fantasma: si hay ventanas fantasma las devuelve a la normalidad; si no, convierte la activa.</summary>
        void ToggleGhostHotkey()
        {
            List<IntPtr> ghosts = WindowManager.GhostWindows();
            if (ghosts.Count > 0)
            {
                foreach (IntPtr hwnd in ghosts) WindowManager.SetGhost(hwnd, false);
                Sync();
                return;
            }
            IntPtr fg = Native.GetForegroundWindow();
            if (WindowManager.IsEligible(fg)) SetGhost(fg, true);
        }

        void SetOpacityPercent(IntPtr hwnd, int percent)
        {
            if (!Native.IsWindow(hwnd)) return;
            percent = Math.Max(20, Math.Min(100, percent));
            if (!WindowManager.SetUserOpacity(hwnd, (byte)Math.Round(percent * 2.55)))
                Warn("Esta ventana no admite transparencia.");
            faded.Remove(hwnd);   // la atenuación se recalcula con la nueva opacidad
        }

        void StepOpacity(IntPtr hwnd, int direction)
        {
            if (!WindowManager.IsEligible(hwnd)) return;
            int percent = (int)Math.Round(WindowManager.GetUserOpacity(hwnd) * 100 / 255.0 / 10.0) * 10;
            SetOpacityPercent(hwnd, percent + direction * 10);
        }

        void ToggleAutoPin(IntPtr hwnd)
        {
            string app = AppOf(hwnd);
            if (app == null) return;
            if (settings.AutoPinApps.Remove(app))
            {
                settings.Save();
                NotifySettingsForm();
                return;
            }
            settings.AutoPinApps.Add(app);
            settings.Save();
            NotifySettingsForm();
            // Fijar ya todas las ventanas abiertas de esa app.
            foreach (IntPtr w in WindowManager.AppWindows())
                if (AppOf(w) == app && !WindowManager.IsPinned(w)) WindowManager.TogglePin(w);
            Sync();
        }

        void ExcludeApp(IntPtr hwnd)
        {
            string app = AppOf(hwnd);
            if (app == null) return;
            settings.ExcludedApps.Add(app);
            settings.Save();
            NotifySettingsForm();
            Sync();
            tray.ShowBalloonTip(4000, "PinTop", "Ya no se mostrará el botón en " + app
                + ". Puedes volver a activarlo en Ajustes.", ToolTipIcon.Info);
        }

        /// <summary>Atenúa las ventanas fijadas que no estás usando (ni activas ni con el ratón encima).</summary>
        void FadeTick()
        {
            if (!settings.FadeEnabled) return;
            IntPtr fg = Native.GetForegroundWindow();
            Point cursor = Cursor.Position;
            byte fadeAlpha = (byte)Math.Round(settings.FadePercent * 2.55);
            var pinned = WindowManager.PinnedWindows();
            foreach (IntPtr hwnd in pinned)
            {
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) continue;
                bool hover = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).Contains(cursor);
                PinButton b;
                if (buttons.TryGetValue(hwnd, out b) && b.IsShown && b.Bounds.Contains(cursor)) hover = true;
                byte user = WindowManager.RememberUserOpacity(hwnd);
                byte want = (hover || hwnd == fg) ? user : Math.Min(user, fadeAlpha);
                if (WindowManager.GetOpacity(hwnd) != want) WindowManager.SetOpacity(hwnd, want);
                if (want != user) faded.Add(hwnd); else faded.Remove(hwnd);
            }
            foreach (IntPtr hwnd in new List<IntPtr>(faded))
                if (!pinned.Contains(hwnd)) RestoreFaded(hwnd);
        }

        void RestoreFaded(IntPtr hwnd)
        {
            faded.Remove(hwnd);
            if (Native.IsWindow(hwnd)) WindowManager.SetOpacity(hwnd, WindowManager.GetUserOpacity(hwnd));
        }

        void UpdateFadeTimer()
        {
            if (settings.FadeEnabled)
            {
                fadeTimer.Start();
            }
            else
            {
                fadeTimer.Stop();
                foreach (IntPtr hwnd in new List<IntPtr>(faded)) RestoreFaded(hwnd);
            }
        }

        void OnNewVersion(string version)
        {
            Debug.Log("nueva versión disponible: " + version + " (actual " + Program.Version + ")");
            if (version == newVersion) return;
            newVersion = version;
            updateItem.Text = "Descargar PinTop " + version + " (nueva versión)";
            updateItem.Visible = true;
            tray.ShowBalloonTip(6000, "Hay una versión nueva de PinTop",
                "Versión " + version + " disponible (tienes la " + Program.Version + "). Haz clic aquí para descargarla.",
                ToolTipIcon.Info);
        }

        void OpenReleasePage()
        {
            try { Process.Start(Updater.ReleasesPage); } catch (Exception) { }
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
                case HotkeyAction.Ghost: ToggleGhostHotkey(); break;
            }
        }

        void SetShowButton(bool value)
        {
            settings.ShowButton = value;
            settings.Save();
            NotifySettingsForm();
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
            settingsForm = new SettingsForm(settings, RunningApps(), ApplySettings, () => WindowManager.PinnedWindows().Count);
            settingsForm.NewVersionFound += OnNewVersion;
            // Los atajos solo se pausan mientras se escribe una combinación (para poder capturarla).
            settingsForm.CapturingHotkeyChanged += capturing =>
            {
                if (exiting || capturing == capturingHotkey) return;
                capturingHotkey = capturing;
                if (capturing)
                {
                    hotkeys.UnregisterAll();
                }
                else
                {
                    List<string> failed = RegisterHotkeys();
                    if (failed.Count > 0) WarnHotkeys(failed);
                }
            };
            Sync();
            settingsForm.FormClosed += delegate
            {
                if (settingsForm != null) settingsForm.Dispose();
                settingsForm = null;
                capturingHotkey = false;
                if (exiting) return;   // saliendo: no volver a registrar atajos ni a fijar nada
                List<string> failed = RegisterHotkeys();
                if (failed.Count > 0) WarnHotkeys(failed);
                Sync();
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        /// <summary>Los ajustes se guardan y aplican al momento, en cuanto cambias algo en la ventana.</summary>
        void ApplySettings(Settings changed)
        {
            bool updatesWereOff = !settings.CheckUpdates;
            bool layoutChanged = changed.ButtonSize != settings.ButtonSize || !SameOffsets(changed.Offsets, settings.Offsets);
            // Las ventanas ya abiertas (también minimizadas, ocultas o en otro escritorio) cuentan
            // como "vistas": una regla nueva de "fijar siempre" solo afecta a las que se abran después.
            foreach (IntPtr hwnd in WindowManager.AllTopLevelWindows()) autoPinChecked.Add(hwnd);
            settings.CopyFrom(changed);
            settings.Save();
            Theme.Mode = settings.ThemeMode;
            // "Fijar siempre" añadido desde Ajustes se aplica a las ventanas que se abran a partir
            // de ahora (no a las ya abiertas, que aparecerían fijadas de golpe). Por eso NO se
            // vacía autoPinChecked aquí.
            UpdateFadeTimer();
            if (!capturingHotkey)
            {
                List<string> failed = RegisterHotkeys();
                if (failed.Count > 0) WarnHotkeys(failed);
            }
            if (layoutChanged) InvalidatePlacements();   // tamaño o posiciones: al momento
            else Sync();
            if (updatesWereOff && settings.CheckUpdates && newVersion == null)
                Updater.CheckAsync(v => Post(() => OnNewVersion(v)));
        }

        static bool SameOffsets(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (KeyValuePair<string, int> pair in a)
            {
                int other;
                if (!b.TryGetValue(pair.Key, out other) || other != pair.Value) return false;
            }
            return true;
        }

        /// <summary>Nombres de las apps con ventanas abiertas (para elegirlas en Ajustes).</summary>
        List<string> RunningApps()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IntPtr hwnd in WindowManager.AppWindows())
            {
                string app = AppOf(hwnd);
                if (app != null) set.Add(app);
            }
            return new List<string>(set);
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
            // Primero se marca la salida y se cierran los Ajustes: así nada (ni el cierre de esa
            // ventana ni un temporizador) puede volver a fijar o pintar después de restaurar.
            exiting = true;
            if (settingsForm != null)
            {
                try { settingsForm.Close(); } catch (Exception) { }
                settingsForm = null;
            }
            syncTimer.Stop();
            fadeTimer.Stop();
            updateTimer.Stop();
            CancelPending();
            foreach (RegisteredWaitHandle w in waits) w.Unregister(null);
            foreach (WaitHandle s in signals) s.Close();
            foreach (IntPtr hook in hooks) Native.UnhookWinEvent(hook);
            hooks.Clear();
            hotkeys.Dispose();
            probe.Dispose();
            WindowManager.RestoreAll();   // nada se queda fijado, transparente, fantasma ni con borde al salir
            tray.Visible = false;
            tray.Dispose();
            ReleaseAllButtons();
            foreach (PinButton b in spareButtons) b.Close();
            invoker.Dispose();
            base.ExitThreadCore();
        }
    }
}
