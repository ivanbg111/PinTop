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

    /// <summary>Ventana de ajustes.</summary>
    internal sealed class SettingsForm : Form
    {
        static readonly KeyValuePair<HotkeyAction, string>[] Rows =
        {
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.TogglePin, "Fijar / soltar la ventana activa"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.OpacityDown, "Más transparente"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.OpacityUp, "Menos transparente"),
            new KeyValuePair<HotkeyAction, string>(HotkeyAction.ToggleButton, "Mostrar / ocultar el botón"),
        };

        readonly Settings working;
        readonly Dictionary<HotkeyAction, HotkeyBox> boxes = new Dictionary<HotkeyAction, HotkeyBox>();
        readonly Dictionary<HotkeyAction, Label> states = new Dictionary<HotkeyAction, Label>();
        readonly CheckBox showButton = new CheckBox();
        readonly CheckBox startup = new CheckBox();
        readonly ComboBox sizeBox = new ComboBox();
        readonly Button resetOffsets = new Button();
        readonly Button save = new Button();
        readonly Label error = new Label();

        public Settings Result { get { return working; } }

        public SettingsForm(Settings current)
        {
            working = current.Clone();

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

            var root = new TableLayoutPanel();
            root.AutoSize = true;
            root.ColumnCount = 1;
            root.Dock = DockStyle.Fill;
            Controls.Add(root);

            root.Controls.Add(BuildHotkeys());
            root.Controls.Add(BuildButtonGroup());
            root.Controls.Add(BuildGeneralGroup());
            root.Controls.Add(BuildFooter());

            ValidateHotkeys();
            // Que no aparezca la primera casilla de atajo seleccionada al abrir.
            Shown += delegate { ActiveControl = showButton; };
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
                box.Width = 200;
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
                state.MinimumSize = new Size(150, 0);
                states[action] = state;

                table.Controls.Add(label);
                table.Controls.Add(box);
                table.Controls.Add(clear);
                table.Controls.Add(state);
            }

            var hint = new Label();
            hint.AutoSize = true;
            hint.MaximumSize = new Size(560, 0);
            hint.ForeColor = SystemColors.GrayText;
            hint.Margin = new Padding(3, 8, 3, 0);
            hint.Text = "Haz clic en una casilla y pulsa la combinación. Supr o Retroceso la borra.\n"
                + "Evita Ctrl + Alt con teclas que usas con AltGr (@, #, €, [ ]...): dejarías de poder escribirlas.";
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

            var sizeLabel = new Label();
            sizeLabel.Text = "Tamaño:";
            sizeLabel.AutoSize = true;
            sizeLabel.Anchor = AnchorStyles.Left;
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

            group.Controls.Add(flow);
            return group;
        }

        void UpdateResetText()
        {
            resetOffsets.Text = "Restablecer posiciones movidas a mano (" + working.Offsets.Count + " apps)";
            resetOffsets.Enabled = working.Offsets.Count > 0;
        }

        Control BuildGeneralGroup()
        {
            GroupBox group = NewGroup("General");
            var flow = NewFlow();
            startup.Text = "Iniciar PinTop con Windows";
            startup.AutoSize = true;
            startup.Checked = Startup.IsEnabled();
            flow.Controls.Add(startup);
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
            error.ForeColor = Color.Firebrick;
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
                    state.ForeColor = Color.Firebrick;
                    ok = false;
                }
                else if (!HotkeyWindow.IsAvailable(hk))
                {
                    state.Text = "Ya la usa otra app";
                    state.ForeColor = Color.Firebrick;
                    ok = false;
                }
                else
                {
                    state.Text = "✓ Disponible";
                    state.ForeColor = Color.SeaGreen;
                }
            }
            save.Enabled = ok;
            error.Text = ok ? "" : "Corrige los atajos marcados en rojo para poder guardar.";
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
    }
}
