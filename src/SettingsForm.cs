using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PinTop
{
    /// <summary>
    /// Ventana de ajustes al estilo de Configuración de Windows 11: menú lateral, tarjetas,
    /// interruptores y tema claro/oscuro. Los cambios se aplican y guardan al momento.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        static readonly string[] NavGlyphs = { "", "", "", "", "", "" };
        static readonly string[] NavLabels = { "General", "Botón", "Atajos de teclado", "Transparencia", "Apps", "Acerca de" };

        /// <summary>Color propio de cada sección (identidad de PinTop, no la de Windows).</summary>
        static readonly Color[] NavColors =
        {
            Palette.BrandBlue,                 // General
            Color.FromArgb(0xEC, 0x48, 0x99),  // Botón: rosa
            Palette.BrandViolet,               // Atajos: violeta
            Color.FromArgb(0x14, 0xB8, 0xA6),  // Transparencia: turquesa
            Color.FromArgb(0xF5, 0x9E, 0x0B),  // Apps: ámbar
            Color.FromArgb(0x64, 0x74, 0x8B),  // Acerca de: gris pizarra
        };

        NavList nav;

        static readonly KeyValuePair<HotkeyAction, string>[] HotkeyRows =
        {
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.TogglePin, "Fijar o soltar la ventana activa"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.OpacityDown, "Más transparente"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.OpacityUp, "Menos transparente"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.Ghost, "Modo fantasma (activar / quitar)"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.ToggleButton, "Mostrar u ocultar el botón"),
        };
        static readonly string[] HotkeyGlyphs = { "", "", "", "", "" };

        readonly Settings working;
        readonly List<string> runningApps;
        readonly Action<Settings> apply;
        readonly Func<int> pinnedCount;
        readonly Dictionary<HotkeyAction, Hotkey> shownHotkeys = new Dictionary<HotkeyAction, Hotkey>();
        int pageIndex;
        Panel host;

        /// <summary>Se lanza si "Buscar actualizaciones" encuentra una versión nueva.</summary>
        public event Action<string> NewVersionFound;

        /// <summary>true mientras una casilla de atajo está escribiendo una combinación.</summary>
        public event Action<bool> CapturingHotkeyChanged;

        void RaiseCapturing(bool capturing)
        {
            if (CapturingHotkeyChanged != null) CapturingHotkeyChanged(capturing);
        }

        public SettingsForm(Settings current, List<string> runningApps, Action<Settings> apply, Func<int> pinnedCount)
        {
            working = current.Clone();
            this.runningApps = runningApps;
            this.apply = apply;
            this.pinnedCount = pinnedCount;
            foreach (KeyValuePair<HotkeyAction, Hotkey> pair in working.Hotkeys) shownHotkeys[pair.Key] = pair.Value;

            Text = "Ajustes de PinTop";
            Icon = Icons.CreateWindowIcon();   // todos los tamaños: nítido en la barra de tareas
            Font = Ui.Body;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.S(940), Ui.S(640));
            MinimumSize = new Size(Ui.S(760), Ui.S(520));
            KeyPreview = true;
            // Por encima de las ventanas fijadas (que son "siempre visibles"): si no, te las
            // podrían tapar mientras las configuras.
            TopMost = true;
            BuildUi();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleTitleBar(Handle, Palette.Current);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Esc cierra la ventana (salvo mientras se está escribiendo un atajo).
            if (keyData == Keys.Escape && !(ActiveControl is KeycapBox))
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>(Re)construye toda la interfaz con la paleta actual (al abrir y al cambiar de tema).</summary>
        void BuildUi()
        {
            Palette.Current = Palette.Create(Theme.IsDarkMode());
            SuspendLayout();
            foreach (Control c in new List<Control>(Controls.Cast())) c.Dispose();
            Controls.Clear();
            BackColor = Palette.Current.Window;

            host = new Panel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Palette.Current.Window;
            Controls.Add(host);

            nav = new NavList(NavGlyphs, NavLabels, NavColors, pageIndex);
            nav.Dock = DockStyle.Left;
            nav.Width = Ui.S(250);
            nav.SelectedChanged += delegate { ShowPage(nav.SelectedIndex); };
            Controls.Add(nav);

            ShowPage(pageIndex);
            ResumeLayout();
            if (IsHandleCreated) Theme.StyleTitleBar(Handle, Palette.Current);
        }

        void ShowPage(int index)
        {
            pageIndex = index;
            Palette.Current.SetAccent(NavColors[index]);   // la página entera toma el color de su sección
            host.SuspendLayout();
            foreach (Control c in new List<Control>(host.Controls.Cast())) c.Dispose();
            host.Controls.Clear();
            Page page;
            switch (index)
            {
                case 1: page = BuildButtonPage(); break;
                case 2: page = BuildHotkeysPage(); break;
                case 3: page = BuildTransparencyPage(); break;
                case 4: page = BuildAppsPage(); break;
                case 5: page = BuildAboutPage(); break;
                default: page = BuildGeneralPage(); break;
            }
            page.Dock = DockStyle.Fill;
            host.Controls.Add(page);
            host.ResumeLayout();
            page.Relayout();
        }

        void Changed()
        {
            apply(working.Clone());
        }

        static ToggleSwitch Toggle(bool value, Action<bool> onChange)
        {
            var t = new ToggleSwitch(value);
            t.CheckedChanged += delegate { onChange(t.Checked); };
            return t;
        }

        // ─────────────────────────── Páginas ───────────────────────────

        Page BuildGeneralPage()
        {
            var page = new Page("General");
            int pinned = pinnedCount();
            page.Add(new HeroCard("PinTop", "Fija cualquier ventana encima de todo",
                "● Activo",
                pinned == 1 ? "1 ventana fijada" : pinned + " ventanas fijadas",
                "Versión " + Program.Version), Ui.S(18));

            var theme = new Segmented(working.ThemeMode, "Como Windows", "Claro", "Oscuro");
            theme.SelectedChanged += delegate
            {
                working.ThemeMode = theme.SelectedIndex;
                Theme.Mode = working.ThemeMode;
                Changed();
                BeginInvoke(new Action(BuildUi));   // repintar todo con el nuevo tema
            };
            page.Section("Apariencia").Card(new Card()
                .Add(new Row("", "Tema", "Ventana de ajustes y menús", theme)));

            page.Section("Inicio").Card(new Card()
                .Add(new Row("", "Iniciar PinTop con Windows", "Se abre solo al encender el ordenador",
                    Toggle(Startup.IsEnabled(), on =>
                    {
                        try { Startup.Set(on); }
                        catch (Exception ex) { MessageBox.Show(this, "No se pudo cambiar: " + ex.Message, "PinTop"); }
                    })))
                .Add(new Row("", "Avisarme si hay una versión nueva",
                    "Consulta una vez al día la página de versiones de PinTop en GitHub",
                    Toggle(working.CheckUpdates, on => { working.CheckUpdates = on; Changed(); }))));

            page.Note("Los cambios se guardan automáticamente.");
            return page;
        }

        Page BuildButtonPage()
        {
            var page = new Page("Botón");
            Row allRow = null;
            ToggleSwitch all = Toggle(working.AllWindows, on => { working.AllWindows = on; Changed(); });
            all.Enabled = working.ShowButton;
            ToggleSwitch show = Toggle(working.ShowButton, on =>
            {
                working.ShowButton = on;
                all.Enabled = on;
                allRow.Dimmed = !on;
                allRow.Invalidate();
                Changed();
            });
            allRow = new Row("", "En todas las ventanas", "Si lo desactivas, solo aparece en la ventana activa y en las fijadas", all);
            allRow.Dimmed = !working.ShowButton;

            var size = new Segmented(working.ButtonSize <= 20 ? 0 : working.ButtonSize >= 28 ? 2 : 1, "Pequeño", "Normal", "Grande");
            size.SelectedChanged += delegate { working.ButtonSize = new[] { 20, 24, 28 }[size.SelectedIndex]; Changed(); };

            page.Section("Chincheta").Card(new Card()
                .Add(new Row("", "Mostrar la chincheta en la barra de título", null, show))
                .Add(allRow)
                .Add(new Row("", "Tamaño", null, size)));

            page.Section("Ventanas fijadas").Card(new Card()
                .Add(new Row("", "Borde de color", "Marca las ventanas fijadas con el color de acento de Windows (Windows 11)",
                    Toggle(working.PinnedBorder, on => { working.PinnedBorder = on; Changed(); }))));

            var reset = new ModernButton("Restablecer");
            reset.Enabled = working.Offsets.Count > 0;
            var resetRow = new Row("", "Posiciones movidas a mano",
                working.Offsets.Count == 0 ? "No has movido el botón en ninguna app"
                    : "Has movido el botón en " + working.Offsets.Count + (working.Offsets.Count == 1 ? " app" : " apps")
                      + ": " + string.Join(", ", new List<string>(working.Offsets.Keys).ToArray()),
                reset);
            reset.Click += delegate
            {
                working.Offsets.Clear();
                Changed();
                ShowPage(pageIndex);
            };
            page.Section("Posición").Card(new Card().Add(resetRow));
            page.Note("Arrastra la chincheta para moverla; la posición se recuerda en cada app. "
                + "Con la rueda del ratón sobre ella cambias la transparencia.");
            return page;
        }

        Page BuildHotkeysPage()
        {
            var page = new Page("Atajos de teclado");
            var card = new Card();
            var statuses = new Dictionary<HotkeyAction, Row>();
            var boxes = new Dictionary<HotkeyAction, KeycapBox>();

            for (int i = 0; i < HotkeyRows.Length; i++)
            {
                HotkeyAction action = HotkeyRows[i].Key;
                Hotkey current;
                shownHotkeys.TryGetValue(action, out current);

                var box = new KeycapBox(current);
                var clear = new ModernButton("", false, "");
                boxes[action] = box;

                // Acompañante: teclas + botón de borrar, alineados a la derecha.
                var strip = new Panel();
                strip.BackColor = Color.Transparent;   // hereda el fondo (y el resaltado) de la fila
                strip.Size = new Size(box.Width + Ui.S(8) + clear.Width, box.Height);
                box.Location = new Point(0, 0);
                clear.Location = new Point(box.Right + Ui.S(8), (box.Height - clear.Height) / 2);
                strip.Controls.Add(box);
                strip.Controls.Add(clear);

                box.ValueChanged += delegate { OnHotkeyEdited(action, box.Value, boxes, statuses); };
                // Mientras se escribe la combinación, PinTop suelta sus atajos para poder capturarlos.
                box.GotFocus += delegate { RaiseCapturing(true); };
                box.LostFocus += delegate { RaiseCapturing(false); };
                clear.Click += delegate { box.Value = Hotkey.None; OnHotkeyEdited(action, Hotkey.None, boxes, statuses); };

                // El estado ("✓ Disponible", "Repetido"...) se muestra debajo del nombre.
                var row = new Row(HotkeyGlyphs[i], HotkeyRows[i].Value, " ", strip);
                statuses[action] = row;
                card.Add(row);
            }
            page.Card(card);
            page.Note("Haz clic en una casilla y pulsa la combinación. Supr o Retroceso la borra; Esc cancela.\n"
                + "Evita Ctrl + Alt con teclas que usas con AltGr (@, #, €, [ ]...): dejarías de poder escribirlas.");
            ValidateHotkeys(boxes, statuses);
            return page;
        }

        void OnHotkeyEdited(HotkeyAction action, Hotkey value, Dictionary<HotkeyAction, KeycapBox> boxes,
            Dictionary<HotkeyAction, Row> statuses)
        {
            shownHotkeys[action] = value;
            ValidateHotkeys(boxes, statuses);
        }

        /// <summary>Marca repetidos y ocupados; solo se guardan las combinaciones válidas.</summary>
        void ValidateHotkeys(Dictionary<HotkeyAction, KeycapBox> boxes, Dictionary<HotkeyAction, Row> statuses)
        {
            Palette p = Palette.Current;
            bool changed = false;
            foreach (KeyValuePair<HotkeyAction, KeycapBox> pair in boxes)
            {
                Hotkey hk = pair.Value.Value;
                Row status = statuses[pair.Key];
                bool valid = true;
                if (hk.IsEmpty)
                {
                    status.SetDescription("Sin atajo", null);
                }
                else
                {
                    bool duplicate = false;
                    foreach (KeyValuePair<HotkeyAction, KeycapBox> other in boxes)
                        if (other.Key != pair.Key && other.Value.Value.SameAs(hk)) duplicate = true;
                    // Una combinación que ya es nuestra (registrada por PinTop) no cuenta como "ocupada".
                    bool ours = false;
                    foreach (Hotkey registered in working.Hotkeys.Values)
                        if (registered.SameAs(hk)) ours = true;
                    if (duplicate) { status.SetDescription("Repetido: elige otra combinación", p.Danger); valid = false; }
                    else if (!ours && !HotkeyWindow.IsAvailable(hk)) { status.SetDescription("Ya la usa otra app: elige otra", p.Danger); valid = false; }
                    else status.SetDescription("✓ Disponible", p.Success);
                }
                Hotkey saved;
                working.Hotkeys.TryGetValue(pair.Key, out saved);
                if (valid && !saved.SameAs(hk))
                {
                    working.Hotkeys[pair.Key] = hk;
                    changed = true;
                }
            }
            if (changed) Changed();
        }

        Page BuildTransparencyPage()
        {
            var page = new Page("Transparencia");
            int[] levels = { 20, 30, 40, 50, 60, 70 };
            int selected = Array.IndexOf(levels, working.FadePercent);
            var level = new Segmented(selected < 0 ? 2 : selected, "20 %", "30 %", "40 %", "50 %", "60 %", "70 %");
            level.Enabled = working.FadeEnabled;
            level.SelectedChanged += delegate { working.FadePercent = levels[level.SelectedIndex]; Changed(); };
            var levelRow = new Row("", "Opacidad al atenuar", null, level);
            levelRow.Dimmed = !working.FadeEnabled;

            page.Section("Atenuar ventanas fijadas").Card(new Card()
                .Add(new Row("", "Atenuar las ventanas fijadas cuando no las uso",
                    "Se vuelven transparentes mientras el ratón no está encima y recuperan su opacidad al pasar el ratón o activarlas",
                    Toggle(working.FadeEnabled, on =>
                    {
                        working.FadeEnabled = on;
                        level.Enabled = on;
                        levelRow.Dimmed = !on;
                        levelRow.Invalidate();
                        Changed();
                    })))
                .Add(levelRow));

            Hotkey ghost;
            working.Hotkeys.TryGetValue(HotkeyAction.Ghost, out ghost);
            page.Section("Otras formas").Card(new Card()
                .Add(new Row("", "Rueda del ratón sobre la chincheta",
                    "Sube o baja la transparencia de esa ventana de 10 en 10 %", null))
                .Add(new Row("", "Modo fantasma",
                    "La ventana fijada deja pasar los clics a lo que hay debajo. Clic derecho en su chincheta"
                    + (ghost.IsEmpty ? "" : " o " + ghost) + " para activarlo o quitarlo", null)));
            return page;
        }

        Page BuildAppsPage()
        {
            var page = new Page("Apps");
            Func<List<string>> apps = () => runningApps;
            page.Section("Fijar siempre").Note("Las ventanas que abras de estas apps se fijan solas (también al reiniciar). "
                + "Las que ya están abiertas no se tocan: para fijarlas, usa su chincheta.");
            page.Card(new Card().Add(new ChipList(working.AutoPinApps, apps, Changed)));
            page.Section("No mostrar el botón en").Note("En estas apps nunca aparece la chincheta (por ejemplo juegos o apps donde estorba).");
            page.Card(new Card().Add(new ChipList(working.ExcludedApps, apps, Changed)));
            return page;
        }

        Page BuildAboutPage()
        {
            var page = new Page("Acerca de");

            var github = new ModernButton("Ver en GitHub", true, "");
            github.Click += delegate { OpenUrl("https://github.com/ivanbg111/PinTop"); };

            var status = new TextLabel("", TextLabel.Kind.Sub);
            var check = new ModernButton("Buscar actualizaciones", false, "");
            check.Click += delegate
            {
                status.Set("Comprobando…", TextLabel.Kind.Sub);
                status.Fit(0);
                check.Enabled = false;
                Updater.CheckAsync(
                    v => BeginInvoke(new Action(() =>
                    {
                        status.Set("Hay una versión nueva: " + v, TextLabel.Kind.Success);
                        status.Fit(0);
                        check.Enabled = true;
                        if (NewVersionFound != null) NewVersionFound(v);
                    })),
                    () => BeginInvoke(new Action(() => { status.Set("Tienes la última versión", TextLabel.Kind.Success); status.Fit(0); check.Enabled = true; })),
                    () => BeginInvoke(new Action(() => { status.Set("No se pudo comprobar (¿sin conexión?)", TextLabel.Kind.Danger); status.Fit(0); check.Enabled = true; })));
            };

            var about = new Card()
                .Add(new Row("", "PinTop " + Program.Version,
                    "Fija cualquier ventana encima de todo con un botón en su barra de título. Licencia MIT.", github))
                .Add(new Row("", "Actualizaciones", null, check));
            page.Card(about);
            page.Add(status, Ui.S(10));
            page.Note("Si algo no funciona bien, puedes contarlo en GitHub (pestaña Issues).");
            return page;
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(url); } catch (Exception) { }
        }
    }

    internal static class ControlExtensions
    {
        /// <summary>Copia la colección de controles para poder recorrerla mientras se modifica.</summary>
        public static IEnumerable<Control> Cast(this Control.ControlCollection controls)
        {
            var list = new List<Control>();
            foreach (Control c in controls) list.Add(c);
            return list;
        }
    }
}
