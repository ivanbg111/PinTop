using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PinTop
{
    /// <summary>Casilla que captura una combinación de teclas al pulsarla.</summary>
    internal sealed class HotkeyBox : TextBox
    {
        Hotkey value;

        public event EventHandler ValueChanged;

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            TextAlign = HorizontalAlignment.Center;
            Cursor = Cursors.Hand;
        }

        public Hotkey Value
        {
            get { return value; }
            set
            {
                this.value = value;
                Text = value.ToString();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Tab sin modificadores sigue sirviendo para moverse entre casillas.
            return keyData != Keys.Tab && keyData != (Keys.Tab | Keys.Shift);
        }

        static bool WinDown()
        {
            return Native.GetKeyState(Native.VK_LWIN) < 0 || Native.GetKeyState(Native.VK_RWIN) < 0;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Keys key = e.KeyCode;
            uint mods = 0;
            if (e.Control) mods |= Native.MOD_CONTROL;
            if (e.Alt) mods |= Native.MOD_ALT;
            if (e.Shift) mods |= Native.MOD_SHIFT;
            if (WinDown()) mods |= Native.MOD_WIN;

            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
            {
                Text = Hotkey.Describe(mods, 0) + " + …";
                return;
            }
            if (mods == 0 && (key == Keys.Back || key == Keys.Delete))
            {
                SetValue(Hotkey.None);
                return;
            }
            if (mods == 0 && key == Keys.Escape)
            {
                Text = value.ToString();
                return;
            }
            bool functionKey = key >= Keys.F1 && key <= Keys.F24;
            if (mods == 0 && !functionKey)
            {
                Text = "Usa Ctrl, Alt, Mayús o Win";
                return;
            }
            SetValue(new Hotkey(mods, (uint)key));
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.Modifiers == Keys.None && !WinDown() && Text.EndsWith("…")) Text = value.ToString();
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            Text = value.ToString();
        }

        public void SetValue(Hotkey hotkey)
        {
            Value = hotkey;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>Lista editable de apps (nombre del programa) con un desplegable de las apps abiertas.</summary>
    internal sealed class AppListEditor : TableLayoutPanel
    {
        readonly HashSet<string> target;
        readonly ListBox list = new ListBox();
        readonly ComboBox picker = new ComboBox();

        public AppListEditor(HashSet<string> target, List<string> runningApps)
        {
            this.target = target;
            AutoSize = true;
            ColumnCount = 3;
            Dock = DockStyle.Fill;

            list.Height = 72;
            list.Width = 340;
            list.Sorted = true;
            foreach (string app in target) list.Items.Add(app);
            Controls.Add(list);
            SetColumnSpan(list, 3);

            picker.Width = 200;
            picker.DropDownStyle = ComboBoxStyle.DropDown;   // se puede escribir un nombre a mano
            foreach (string app in runningApps) picker.Items.Add(app);
            Controls.Add(picker);

            var add = new Button();
            add.Text = "Añadir";
            add.AutoSize = true;
            add.Click += delegate
            {
                string app = picker.Text.Trim();
                if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app.Substring(0, app.Length - 4);
                if (app.Length == 0 || target.Contains(app)) return;
                target.Add(app);
                list.Items.Add(app);
                picker.Text = "";
            };
            Controls.Add(add);

            var remove = new Button();
            remove.Text = "Quitar";
            remove.AutoSize = true;
            remove.Click += delegate
            {
                string app = list.SelectedItem as string;
                if (app == null) return;
                target.Remove(app);
                list.Items.Remove(app);
            };
            Controls.Add(remove);
        }
    }

    /// <summary>Ventana de ajustes (sigue el modo claro/oscuro de Windows).</summary>
    internal sealed class SettingsForm : Form
    {
        static readonly KeyValuePair<HotkeyAction, string>[] Rows =
        {
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.TogglePin, "Fijar / soltar la ventana activa"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.OpacityDown, "Más transparente"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.OpacityUp, "Menos transparente"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.Ghost, "Modo fantasma (activar / quitar)"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.ToggleButton, "Mostrar / ocultar el botón"),
        };

        static readonly Color ErrorLight = Color.Firebrick, ErrorDark = Color.FromArgb(255, 120, 120);
        static readonly Color OkLight = Color.SeaGreen, OkDark = Color.FromArgb(120, 210, 150);

        readonly Settings working;
        readonly List<string> runningApps;
        readonly bool dark = Theme.AppsUseDark();
        readonly Dictionary<HotkeyAction, HotkeyBox> boxes = new Dictionary<HotkeyAction, HotkeyBox>();
        readonly Dictionary<HotkeyAction, Label> states = new Dictionary<HotkeyAction, Label>();
        readonly CheckBox showButton = new CheckBox();
        readonly CheckBox startup = new CheckBox();
        readonly ComboBox sizeBox = new ComboBox();
        readonly Button resetOffsets = new Button();
        readonly Button save = new Button();
        readonly Label error = new Label();

        public Settings Result { get { return working; } }

        public SettingsForm(Settings current, List<string> runningApps)
        {
            working = current.Clone();
            this.runningApps = runningApps;

            Text = "Ajustes de PinTop " + Program.Version;
            Icon = Icons.CreateTrayIcon();
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            // Dos columnas: atajos y botón a la izquierda; resto a la derecha.
            var root = new TableLayoutPanel();
            root.AutoSize = true;
            root.ColumnCount = 2;
            root.Dock = DockStyle.Fill;
            Controls.Add(root);

            var left = NewColumn();
            left.Controls.Add(BuildHotkeys());
            left.Controls.Add(BuildButtonGroup());
            var right = NewColumn();
            right.Controls.Add(BuildFadeGroup());
            right.Controls.Add(BuildAppsGroup());
            right.Controls.Add(BuildGeneralGroup());
            root.Controls.Add(left);
            root.Controls.Add(right);
            Control footer = BuildFooter();
            root.Controls.Add(footer);
            root.SetColumnSpan(footer, 2);

            ValidateHotkeys();
            if (dark) ApplyDark(this);
            // Que no aparezca la primera casilla de atajo seleccionada al abrir.
            Shown += delegate { ActiveControl = showButton; };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (dark) Theme.UseDarkTitleBar(Handle, true);
        }

        static TableLayoutPanel NewColumn()
        {
            var col = new TableLayoutPanel();
            col.AutoSize = true;
            col.ColumnCount = 1;
            col.Dock = DockStyle.Fill;
            col.Margin = new Padding(0, 0, 10, 0);
            return col;
        }

        Control BuildHotkeys()
        {
            GroupBox group = NewGroup("Atajos de teclado");
            var table = new TableLayoutPanel();
            table.AutoSize = true;
            table.ColumnCount = 4;
            table.Dock = DockStyle.Fill;

            foreach (KeyValuePair<HotkeyAction, string> row in Rows)
            {
                HotkeyAction action = row.Key;
                var label = new Label();
                label.Text = row.Value;
                label.AutoSize = true;
                label.Anchor = AnchorStyles.Left;

                var box = new HotkeyBox();
                box.Width = 170;
                box.Value = working.Hotkeys.ContainsKey(action) ? working.Hotkeys[action] : Hotkey.None;
                box.ValueChanged += delegate
                {
                    working.Hotkeys[action] = box.Value;
                    ValidateHotkeys();
                };
                boxes[action] = box;

                var clear = new Button();
                clear.Text = "Quitar";
                clear.AutoSize = true;
                clear.Click += delegate { box.SetValue(Hotkey.None); };

                var state = new Label();
                state.AutoSize = true;
                state.Anchor = AnchorStyles.Left;
                state.MinimumSize = new Size(120, 0);
                states[action] = state;

                table.Controls.Add(label);
                table.Controls.Add(box);
                table.Controls.Add(clear);
                table.Controls.Add(state);
            }

            Label hint = Hint("Haz clic en una casilla y pulsa la combinación. Supr o Retroceso la borra.\n"
                + "Evita Ctrl + Alt con teclas que usas con AltGr (@, #, €, [ ]...).");
            table.Controls.Add(hint);
            table.SetColumnSpan(hint, 4);

            group.Controls.Add(table);
            return group;
        }

        Control BuildButtonGroup()
        {
            GroupBox group = NewGroup("Botón");
            var flow = NewFlow();

            showButton.Text = "Mostrar el botón de la chincheta en la barra de título";
            showButton.AutoSize = true;
            showButton.Checked = working.ShowButton;
            showButton.CheckedChanged += delegate { working.ShowButton = showButton.Checked; };
            flow.Controls.Add(showButton);
            flow.SetFlowBreak(showButton, true);

            var allWindows = new CheckBox();
            allWindows.Text = "En todas las ventanas (si no, solo en la activa y en las fijadas)";
            allWindows.AutoSize = true;
            allWindows.Margin = new Padding(20, 0, 3, 3);
            allWindows.Checked = working.AllWindows;
            allWindows.Enabled = working.ShowButton;
            allWindows.CheckedChanged += delegate { working.AllWindows = allWindows.Checked; };
            showButton.CheckedChanged += delegate { allWindows.Enabled = showButton.Checked; };
            flow.Controls.Add(allWindows);
            flow.SetFlowBreak(allWindows, true);

            var border = new CheckBox();
            border.Text = "Borde de color en las ventanas fijadas (Windows 11)";
            border.AutoSize = true;
            border.Checked = working.PinnedBorder;
            border.CheckedChanged += delegate { working.PinnedBorder = border.Checked; };
            flow.Controls.Add(border);
            flow.SetFlowBreak(border, true);

            var sizeLabel = new Label();
            sizeLabel.Text = "Tamaño:";
            sizeLabel.AutoSize = true;
            sizeLabel.Margin = new Padding(3, 7, 3, 3);
            flow.Controls.Add(sizeLabel);

            sizeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sizeBox.Items.AddRange(new object[] { "Pequeño", "Normal", "Grande" });
            sizeBox.SelectedIndex = working.ButtonSize <= 20 ? 0 : working.ButtonSize >= 28 ? 2 : 1;
            sizeBox.SelectedIndexChanged += delegate { working.ButtonSize = new[] { 20, 24, 28 }[sizeBox.SelectedIndex]; };
            flow.Controls.Add(sizeBox);
            flow.SetFlowBreak(sizeBox, true);

            resetOffsets.AutoSize = true;
            resetOffsets.Click += delegate
            {
                working.Offsets.Clear();
                UpdateResetText();
            };
            UpdateResetText();
            flow.Controls.Add(resetOffsets);
            flow.SetFlowBreak(resetOffsets, true);

            flow.Controls.Add(Hint("Rueda del ratón sobre el botón: cambia la transparencia.\n"
                + "Arrastra el botón para moverlo; la posición se recuerda en cada app."));

            group.Controls.Add(flow);
            return group;
        }

        void UpdateResetText()
        {
            resetOffsets.Text = "Restablecer botones movidos a mano (" + working.Offsets.Count + " apps)";
            resetOffsets.Enabled = working.Offsets.Count > 0;
        }

        Control BuildFadeGroup()
        {
            GroupBox group = NewGroup("Atenuar ventanas fijadas");
            var flow = NewFlow();

            var enable = new CheckBox();
            enable.Text = "Atenuar las ventanas fijadas cuando no las uso";
            enable.AutoSize = true;
            enable.Checked = working.FadeEnabled;
            flow.Controls.Add(enable);
            flow.SetFlowBreak(enable, true);

            var levelLabel = new Label();
            levelLabel.Text = "Opacidad al atenuar:";
            levelLabel.AutoSize = true;
            levelLabel.Margin = new Padding(20, 7, 3, 3);
            flow.Controls.Add(levelLabel);

            var level = new ComboBox();
            level.DropDownStyle = ComboBoxStyle.DropDownList;
            level.Width = 80;
            int[] levels = { 20, 30, 40, 50, 60, 70 };
            int selected = 2;
            for (int i = 0; i < levels.Length; i++)
            {
                level.Items.Add(levels[i] + " %");
                if (levels[i] == working.FadePercent) selected = i;
            }
            level.SelectedIndex = selected;
            level.SelectedIndexChanged += delegate { working.FadePercent = levels[level.SelectedIndex]; };
            flow.Controls.Add(level);
            flow.SetFlowBreak(level, true);

            level.Enabled = levelLabel.Enabled = enable.Checked;
            enable.CheckedChanged += delegate
            {
                working.FadeEnabled = enable.Checked;
                level.Enabled = levelLabel.Enabled = enable.Checked;
            };

            flow.Controls.Add(Hint("Se vuelven transparentes mientras el ratón no está encima\n"
                + "y recuperan su opacidad al pasar el ratón o activarlas."));
            group.Controls.Add(flow);
            return group;
        }

        Control BuildAppsGroup()
        {
            GroupBox group = NewGroup("Apps");
            var table = new TableLayoutPanel();
            table.AutoSize = true;
            table.ColumnCount = 1;
            table.Dock = DockStyle.Fill;

            table.Controls.Add(Title("Fijar siempre (se fijan solas al abrirse):"));
            table.Controls.Add(new AppListEditor(working.AutoPinApps, runningApps));
            table.Controls.Add(Title("No mostrar el botón en:"));
            table.Controls.Add(new AppListEditor(working.ExcludedApps, runningApps));
            table.Controls.Add(Hint("Elige una app abierta en el desplegable o escribe su nombre."));

            group.Controls.Add(table);
            return group;
        }

        Control BuildGeneralGroup()
        {
            GroupBox group = NewGroup("General");
            var flow = NewFlow();
            startup.Text = "Iniciar PinTop con Windows";
            startup.AutoSize = true;
            startup.Checked = Startup.IsEnabled();
            flow.Controls.Add(startup);
            flow.SetFlowBreak(startup, true);

            var updates = new CheckBox();
            updates.Text = "Avisarme si hay una versión nueva (consulta GitHub)";
            updates.AutoSize = true;
            updates.Checked = working.CheckUpdates;
            updates.CheckedChanged += delegate { working.CheckUpdates = updates.Checked; };
            flow.Controls.Add(updates);

            group.Controls.Add(flow);
            return group;
        }

        Control BuildFooter()
        {
            var panel = new TableLayoutPanel();
            panel.AutoSize = true;
            panel.ColumnCount = 3;
            panel.Dock = DockStyle.Fill;
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Margin = new Padding(0, 8, 0, 0);

            var info = new FlowLayoutPanel();
            info.AutoSize = true;
            info.FlowDirection = FlowDirection.TopDown;
            var link = new LinkLabel();
            link.Text = "github.com/ivanbg111/PinTop";
            link.AutoSize = true;
            link.LinkClicked += delegate { Process.Start("https://github.com/ivanbg111/PinTop"); };
            info.Controls.Add(link);
            error.AutoSize = true;
            info.Controls.Add(error);
            panel.Controls.Add(info);

            save.Text = "Guardar";
            save.AutoSize = true;
            save.MinimumSize = new Size(90, 0);
            save.Click += delegate
            {
                try
                {
                    if (startup.Checked != Startup.IsEnabled()) Startup.Set(startup.Checked);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "No se pudo cambiar el inicio con Windows: " + ex.Message, "PinTop");
                }
                DialogResult = DialogResult.OK;
                Close();
            };
            AcceptButton = save;
            panel.Controls.Add(save);

            var cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.AutoSize = true;
            cancel.MinimumSize = new Size(90, 0);
            cancel.Click += delegate { Close(); };   // ventana no modal: hay que cerrarla a mano
            CancelButton = cancel;
            panel.Controls.Add(cancel);
            return panel;
        }

        /// <summary>Marca combinaciones repetidas o ya usadas por otra app y bloquea Guardar si las hay.</summary>
        void ValidateHotkeys()
        {
            bool ok = true;
            foreach (KeyValuePair<HotkeyAction, HotkeyBox> pair in boxes)
            {
                Hotkey hk = pair.Value.Value;
                Label state = states[pair.Key];
                if (hk.IsEmpty)
                {
                    state.Text = "";
                    continue;
                }
                bool duplicate = false;
                foreach (KeyValuePair<HotkeyAction, HotkeyBox> other in boxes)
                    if (other.Key != pair.Key && other.Value.Value.SameAs(hk)) duplicate = true;

                if (duplicate)
                {
                    state.Text = "Repetido";
                    state.ForeColor = dark ? ErrorDark : ErrorLight;
                    ok = false;
                }
                else if (!HotkeyWindow.IsAvailable(hk))
                {
                    state.Text = "Ya la usa otra app";
                    state.ForeColor = dark ? ErrorDark : ErrorLight;
                    ok = false;
                }
                else
                {
                    state.Text = "✓ Disponible";
                    state.ForeColor = dark ? OkDark : OkLight;
                }
            }
            save.Enabled = ok;
            error.Text = ok ? "" : "Corrige los atajos marcados en rojo para poder guardar.";
            error.ForeColor = dark ? ErrorDark : ErrorLight;
        }

        Label Hint(string text)
        {
            var hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(520, 0);
            hint.ForeColor = dark ? Theme.DarkMuted : SystemColors.GrayText;
            hint.Margin = new Padding(3, 6, 3, 0);
            hint.Text = text;
            hint.Tag = "hint";
            return hint;
        }

        static Label Title(string text)
        {
            var label = new Label();
            label.AutoSize = true;
            label.Margin = new Padding(3, 6, 3, 2);
            label.Text = text;
            return label;
        }

        static GroupBox NewGroup(string title)
        {
            var group = new GroupBox();
            group.Text = title;
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.Dock = DockStyle.Fill;
            group.Padding = new Padding(10, 6, 10, 8);
            group.Margin = new Padding(0, 0, 0, 10);
            return group;
        }

        static FlowLayoutPanel NewFlow()
        {
            var flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.LeftToRight;
            return flow;
        }

        /// <summary>Colores oscuros para toda la ventana (WinForms no los aplica solo).</summary>
        static void ApplyDark(Control control)
        {
            if (control is Form || control is GroupBox || control is Panel || control is Label || control is CheckBox)
            {
                control.BackColor = Theme.DarkBack;
                if (!(control is Label && (string)control.Tag == "hint")) control.ForeColor = Theme.DarkText;
            }
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Theme.DarkBorder;
                button.BackColor = Theme.DarkSurface;
                button.ForeColor = Theme.DarkText;
            }
            if (control is TextBox || control is ComboBox || control is ListBox)
            {
                control.BackColor = Theme.DarkSurface;
                control.ForeColor = Theme.DarkText;
                var combo = control as ComboBox;
                if (combo != null) combo.FlatStyle = FlatStyle.Flat;
                var list = control as ListBox;
                if (list != null) list.BorderStyle = BorderStyle.FixedSingle;
            }
            var link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = Color.FromArgb(120, 180, 255);
                link.ActiveLinkColor = Color.FromArgb(160, 205, 255);
            }
            foreach (Control child in control.Controls) ApplyDark(child);
        }
    }
}
