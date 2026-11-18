using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PinTop
{
    /// <summary>Colores de la interfaz, al estilo de la app Configuración de Windows 11.</summary>
    internal sealed class Palette
    {
        public bool Dark;
        public Color Window, Card, CardBorder, Divider, Hover, Pressed;
        public Color Text, SubText, Disabled;
        public Color Accent, AccentHover, AccentPressed, AccentText;
        public Color ControlBack, ControlHover, ControlBorder, Keycap;
        public Color Danger, Success;
        /// <summary>Tarjeta con el ratón encima, borde inferior de "relieve", baldosa de icono y menú lateral.</summary>
        public Color CardHover, Elevation, Tile, TileGlyph, NavSelected;

        /// <summary>Paleta en uso por la ventana de ajustes.</summary>
        public static Palette Current = Create(false);

        public static Palette Create(bool dark)
        {
            var p = new Palette();
            p.Dark = dark;
            Color accent = Theme.Accent();
            if (dark)
            {
                p.Window = Rgb(0x20, 0x20, 0x20);
                p.Card = Rgb(0x2B, 0x2B, 0x2B);
                p.CardBorder = Rgb(0x1C, 0x1C, 0x1C);
                p.Divider = Rgb(0x1F, 0x1F, 0x1F);
                p.Hover = Rgb(0x2D, 0x2D, 0x2D);
                p.Pressed = Rgb(0x29, 0x29, 0x29);
                p.Text = Rgb(0xFF, 0xFF, 0xFF);
                p.SubText = Rgb(0xA8, 0xA8, 0xA8);
                p.Disabled = Rgb(0x5E, 0x5E, 0x5E);
                // En modo oscuro Windows usa una versión más clara del acento.
                p.Accent = Theme.Blend(accent, Color.White, 0.38f);
                p.AccentHover = Theme.Blend(p.Accent, Color.Black, 0.1f);
                p.AccentPressed = Theme.Blend(p.Accent, Color.Black, 0.2f);
                p.ControlBack = Rgb(0x37, 0x37, 0x37);
                p.ControlHover = Rgb(0x3D, 0x3D, 0x3D);
                p.ControlBorder = Rgb(0x45, 0x45, 0x45);
                p.Keycap = Rgb(0x45, 0x45, 0x45);
                p.Danger = Rgb(0xFF, 0x99, 0xA4);
                p.Success = Rgb(0x6C, 0xCB, 0x5F);
            }
            else
            {
                p.Window = Rgb(0xF3, 0xF3, 0xF3);
                p.Card = Rgb(0xFB, 0xFB, 0xFB);
                p.CardBorder = Rgb(0xE5, 0xE5, 0xE5);
                p.Divider = Rgb(0xEA, 0xEA, 0xEA);
                p.Hover = Rgb(0xEA, 0xEA, 0xEA);
                p.Pressed = Rgb(0xE3, 0xE3, 0xE3);
                p.Text = Rgb(0x1A, 0x1A, 0x1A);
                p.SubText = Rgb(0x5F, 0x5F, 0x5F);
                p.Disabled = Rgb(0xA6, 0xA6, 0xA6);
                p.Accent = Theme.IsDark(accent) ? accent : Theme.Blend(accent, Color.Black, 0.3f);
                p.AccentHover = Theme.Blend(p.Accent, Color.White, 0.1f);
                p.AccentPressed = Theme.Blend(p.Accent, Color.White, 0.2f);
                p.ControlBack = Rgb(0xFF, 0xFF, 0xFF);
                p.ControlHover = Rgb(0xF6, 0xF6, 0xF6);
                p.ControlBorder = Rgb(0xD5, 0xD5, 0xD5);
                p.Keycap = Rgb(0xEC, 0xEC, 0xEC);
                p.Danger = Rgb(0xC4, 0x2B, 0x1C);
                p.Success = Rgb(0x0F, 0x7B, 0x0F);
            }
            p.AccentText = Theme.InkFor(p.Accent);
            if (dark)
            {
                p.CardHover = Rgb(0x32, 0x32, 0x32);
                p.Elevation = Rgb(0x16, 0x16, 0x16);
                p.Tile = Theme.Blend(p.Card, p.Accent, 0.2f);
                p.TileGlyph = p.Accent;
                p.NavSelected = Theme.Blend(p.Window, p.Accent, 0.16f);
            }
            else
            {
                p.CardHover = Rgb(0xF6, 0xF6, 0xF6);
                p.Elevation = Rgb(0xD8, 0xD8, 0xD8);
                p.Tile = Theme.Blend(p.Card, p.Accent, 0.13f);
                p.TileGlyph = p.Accent;
                p.NavSelected = Theme.Blend(p.Window, p.Accent, 0.12f);
            }
            return p;
        }

        /// <summary>
        /// Cambia el color protagonista (interruptores, selectores, baldosas...). Cada sección de los
        /// ajustes tiene el suyo: es la identidad visual propia de PinTop.
        /// </summary>
        public void SetAccent(Color color)
        {
            Accent = Dark ? Theme.Blend(color, Color.White, 0.12f) : color;
            AccentHover = Theme.Blend(Accent, Dark ? Color.Black : Color.White, 0.12f);
            AccentPressed = Theme.Blend(Accent, Dark ? Color.Black : Color.White, 0.24f);
            AccentText = Theme.InkFor(Accent);
            Tile = Theme.Blend(Card, Accent, Dark ? 0.22f : 0.14f);
            TileGlyph = Dark ? Theme.Blend(Accent, Color.White, 0.2f) : Theme.Blend(Accent, Color.Black, 0.08f);
            NavSelected = Theme.Blend(Window, Accent, Dark ? 0.2f : 0.14f);
        }

        /// <summary>Colores de marca de PinTop.</summary>
        public static readonly Color BrandBlue = Rgb(0x3B, 0x82, 0xF6);
        public static readonly Color BrandViolet = Rgb(0x8B, 0x5C, 0xF6);

        static Color Rgb(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }
    }

    /// <summary>Escala por DPI, fuentes y utilidades de dibujo.</summary>
    internal static class Ui
    {
        public static float Scale = 1f;
        public static Font Body, BodyStrong, Caption, Subtitle, Title, IconSmall, IconNav, Icon, IconLarge;

        public static void Init()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var installed = new InstalledFontCollection())
                foreach (FontFamily f in installed.Families) families.Add(f.Name);

            string text = families.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            string strong = families.Contains("Segoe UI Variable Text Semibold") ? "Segoe UI Variable Text Semibold" : "Segoe UI Semibold";
            string display = families.Contains("Segoe UI Variable Display Semib") ? "Segoe UI Variable Display Semib" : "Segoe UI Semibold";
            string icons = families.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

            Body = new Font(text, 10.5f);
            BodyStrong = new Font(strong, 10.5f);
            Caption = new Font(text, 9f);
            Subtitle = new Font(display, 15f);
            Title = new Font(display, 21f);
            IconSmall = new Font(icons, 7.5f);
            IconNav = new Font(icons, 10f);
            Icon = new Font(icons, 12f);
            IconLarge = new Font(icons, 15f);
        }

        public static int S(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color fill, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(r, radius))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (border.HasValue)
                    using (var pen = new Pen(border.Value)) g.DrawPath(pen, path);
            }
        }

        /// <summary>Color de fondo real de un control (saltando los transparentes).</summary>
        public static Color SurfaceOf(Control c)
        {
            while (c != null && c.BackColor.A < 255) c = c.Parent;
            return c != null ? c.BackColor : Palette.Current.Window;
        }

        /// <summary>
        /// Tarjeta redondeada con "relieve": borde inferior un poco más oscuro, como las tarjetas
        /// elevadas de Windows 11.
        /// </summary>
        public static void PaintCard(Graphics g, Rectangle bounds, Color outside, Color fill)
        {
            Palette p = Palette.Current;
            g.Clear(outside);
            float radius = S(8);
            var shadow = new RectangleF(0.5f, 1.5f, bounds.Width - 1, bounds.Height - 2);
            var card = new RectangleF(0.5f, 0.5f, bounds.Width - 1, bounds.Height - 2);
            FillRound(g, shadow, radius, p.Elevation);
            FillRound(g, card, radius, fill, p.CardBorder);
        }

        public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        public static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Control dibujado a mano que pinta su fondo con el color de su contenedor.</summary>
    internal abstract class ModernControl : Control
    {
        protected bool hover, pressed;

        protected ModernControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
            Font = Ui.Body;
        }

        protected Palette P { get { return Palette.Current; } }

        protected Color Surface { get { return Ui.SurfaceOf(Parent); } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>Interruptor Activado/Desactivado como el de Windows 11.</summary>
    internal sealed class ToggleSwitch : ModernControl
    {
        bool on;
        public event EventHandler CheckedChanged;

        public ToggleSwitch(bool initial)
        {
            on = initial;
            Size = new Size(Ui.Measure("Desactivado", Ui.Body).Width + Ui.S(64), Ui.S(28));
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on != value) { on = value; Invalidate(); } }
        }

        void Toggle()
        {
            if (!Enabled) return;
            Checked = !Checked;
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Toggle(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) Toggle();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surface);
            int w = Ui.S(40), h = Ui.S(20);
            var track = new RectangleF(Width - w - 1, (Height - h) / 2f, w, h);

            Ui.Text(g, on ? "Activado" : "Desactivado", Font, new Rectangle(0, 0, (int)track.Left - Ui.S(12), Height),
                Enabled ? P.Text : P.Disabled, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            float knob = Ui.S(hover && Enabled ? 14 : 12);
            if (on)
            {
                Color fill = !Enabled ? P.Disabled : pressed ? P.AccentPressed : hover ? P.AccentHover : P.Accent;
                Ui.FillRound(g, track, h / 2f, fill);
                using (var brush = new SolidBrush(Enabled ? P.AccentText : P.Card))
                    g.FillEllipse(brush, track.Right - h / 2f - knob / 2f, track.Top + (h - knob) / 2f, knob, knob);
            }
            else
            {
                Color line = Enabled ? P.SubText : P.Disabled;
                Ui.FillRound(g, track, h / 2f, hover && Enabled ? P.ControlHover : Surface, line);
                using (var brush = new SolidBrush(line))
                    g.FillEllipse(brush, track.Left + h / 2f - knob / 2f, track.Top + (h - knob) / 2f, knob, knob);
            }
        }
    }

    /// <summary>Selector de varias opciones en una sola pieza (como "Claro | Oscuro").</summary>
    internal sealed class Segmented : ModernControl
    {
        readonly string[] items;
        readonly int segment;
        int selected, hoverIndex = -1;
        public event EventHandler SelectedChanged;

        public Segmented(int selectedIndex, params string[] items)
        {
            this.items = items;
            selected = selectedIndex;
            int widest = 0;
            foreach (string item in items) widest = Math.Max(widest, Ui.Measure(item, Ui.Body).Width);
            segment = widest + Ui.S(26);
            Size = new Size(segment * items.Length + Ui.S(6), Ui.S(34));
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        int IndexAt(int x)
        {
            int i = (x - Ui.S(3)) / segment;
            return i >= 0 && i < items.Length ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.X);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverIndex = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = IndexAt(e.X);
            if (i < 0 || i == selected || !Enabled) return;
            selected = i;
            Invalidate();
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surface);
            // Forma de "píldora": totalmente redondeado, con la opción elegida en otra píldora.
            Ui.FillRound(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), (Height - 1) / 2f, P.ControlBack, P.ControlBorder);
            for (int i = 0; i < items.Length; i++)
            {
                var r = new Rectangle(Ui.S(3) + i * segment, Ui.S(3), segment, Height - Ui.S(6));
                Color text = Enabled ? P.Text : P.Disabled;
                if (i == selected)
                {
                    Ui.FillRound(g, r, r.Height / 2f, Enabled ? P.Accent : P.Disabled);
                    text = P.AccentText;
                }
                else if (i == hoverIndex && Enabled)
                {
                    Ui.FillRound(g, r, r.Height / 2f, P.ControlHover);
                }
                Ui.Text(g, items[i], i == selected ? Ui.BodyStrong : Font, r, text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Botón redondeado (normal o principal con el color de acento), con icono opcional.</summary>
    internal sealed class ModernButton : ModernControl
    {
        readonly bool primary;
        readonly string glyph;

        public ModernButton(string text, bool primary = false, string glyph = null)
        {
            this.primary = primary;
            this.glyph = glyph;
            Text = text;
            Cursor = Cursors.Hand;
            TabStop = true;
            Fit();
        }

        void Fit()
        {
            int width = Text.Length > 0 ? Ui.Measure(Text, Ui.Body).Width + Ui.S(40) : 0;
            if (glyph != null) width += Text.Length > 0 ? Ui.S(26) : Ui.S(34);
            Size = new Size(Math.Max(width, Ui.S(34)), Ui.S(34));
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surface);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Color back, ink;
            if (primary)
            {
                back = !Enabled ? P.Disabled : pressed ? P.AccentPressed : hover ? P.AccentHover : P.Accent;
                ink = P.AccentText;
                Ui.FillRound(g, r, r.Height / 2f, back);   // píldora
            }
            else
            {
                back = pressed ? P.Pressed : hover && Enabled ? P.ControlHover : P.ControlBack;
                ink = Enabled ? P.Text : P.Disabled;
                Ui.FillRound(g, r, r.Height / 2f, back, Focused ? P.Accent : P.ControlBorder);
            }

            if (glyph != null && Text.Length == 0)
            {
                Ui.Text(g, glyph, Ui.IconSmall, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int x = Ui.S(20);
            if (glyph != null)
            {
                Ui.Text(g, glyph, Ui.IconSmall, new Rectangle(x, 0, Ui.S(18), Height), ink, TextFormatFlags.VerticalCenter);
                x += Ui.S(26);
            }
            Ui.Text(g, Text, Font, new Rectangle(x, 0, Width - x - Ui.S(10), Height), ink, TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Texto simple que respeta el tema (color normal, secundario o de estado).</summary>
    internal sealed class TextLabel : ModernControl
    {
        public enum Kind { Normal, Sub, Danger, Success, Heading, Title }
        Kind kind;

        public TextLabel(string text, Kind kind = Kind.Normal, int width = 0)
        {
            this.kind = kind;
            Text = text;
            Font = kind == Kind.Heading ? Ui.BodyStrong : kind == Kind.Title ? Ui.Title : kind == Kind.Sub ? Ui.Caption : Ui.Body;
            Fit(width);
        }

        public void Set(string text, Kind newKind)
        {
            kind = newKind;
            Text = text;
            Invalidate();
        }

        public void Fit(int width)
        {
            if (width <= 0)
            {
                Size s = Ui.Measure(Text.Length > 0 ? Text : " ", Font);
                Size = new Size(s.Width + Ui.S(2), s.Height + Ui.S(2));
            }
            else
            {
                Size s = TextRenderer.MeasureText(Text.Length > 0 ? Text : " ", Font, new Size(width, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                Size = new Size(width, s.Height + Ui.S(2));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Surface);
            Color c = kind == Kind.Sub ? P.SubText : kind == Kind.Danger ? P.Danger : kind == Kind.Success ? P.Success : P.Text;
            if (!Enabled) c = P.Disabled;
            Ui.Text(e.Graphics, Text, Font, ClientRectangle, c, TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Casilla que captura una combinación de teclas y la muestra como teclas dibujadas.</summary>
    internal sealed class KeycapBox : ModernControl
    {
        Hotkey value;
        string partial;
        public event EventHandler ValueChanged;

        public KeycapBox(Hotkey initial)
        {
            value = initial;
            Size = new Size(Ui.S(230), Ui.S(36));
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public Hotkey Value
        {
            get { return value; }
            set { this.value = value; partial = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }
        protected override void OnLostFocus(EventArgs e) { partial = null; base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) != Keys.Tab; }

        static bool WinDown()
        {
            return Native.GetKeyState(Native.VK_LWIN) < 0 || Native.GetKeyState(Native.VK_RWIN) < 0;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Se capturan aquí para que Alt y compañía no los procese la ventana.
            if (!Focused || (keyData & Keys.KeyCode) == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
            HandleKey(keyData);
            return true;
        }

        void HandleKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= Native.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= Native.MOD_SHIFT;
            if (WinDown()) mods |= Native.MOD_WIN;

            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
            {
                partial = Hotkey.Describe(mods, 0) + " + …";
            }
            else if (mods == 0 && (key == Keys.Back || key == Keys.Delete))
            {
                SetValue(Hotkey.None);
                return;
            }
            else if (mods == 0 && key == Keys.Escape)
            {
                partial = null;
            }
            else if (mods == 0 && !(key >= Keys.F1 && key <= Keys.F24))
            {
                partial = "Usa Ctrl, Alt, Mayús o Win";
            }
            else
            {
                SetValue(new Hotkey(mods, (uint)key));
                return;
            }
            Invalidate();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.Modifiers == Keys.None && !WinDown() && partial != null && partial.EndsWith("…"))
            {
                partial = null;
                Invalidate();
            }
        }

        void SetValue(Hotkey hotkey)
        {
            value = hotkey;
            partial = null;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surface);
            var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Ui.FillRound(g, box, Ui.S(8), hover ? P.ControlHover : P.ControlBack, Focused ? P.Accent : P.ControlBorder);
            if (Focused)
                using (var brush = new SolidBrush(P.Accent))
                    g.FillRectangle(brush, Ui.S(8), Height - Ui.S(2), Width - Ui.S(16), Ui.S(2));

            if (Focused && partial == null)
            {
                Ui.Text(g, "Pulsa la combinación…", Font, new Rectangle(Ui.S(12), 0, Width - Ui.S(16), Height), P.SubText,
                    TextFormatFlags.VerticalCenter);
                return;
            }
            if (partial != null && !partial.EndsWith("…"))
            {
                Ui.Text(g, partial, Ui.Caption, new Rectangle(Ui.S(12), 0, Width - Ui.S(16), Height), P.Danger,
                    TextFormatFlags.VerticalCenter);
                return;
            }
            string text = partial != null ? partial.Replace(" + …", "") : value.IsEmpty ? null : value.ToString();
            if (text == null)
            {
                Ui.Text(g, "Sin asignar", Font, new Rectangle(Ui.S(12), 0, Width - Ui.S(16), Height), P.SubText,
                    TextFormatFlags.VerticalCenter);
                return;
            }
            // Cada tecla, una "tecla" dibujada.
            int x = Ui.S(8);
            int capH = Ui.S(24);
            foreach (string part in text.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries))
            {
                int w = Math.Max(Ui.S(26), Ui.Measure(part, Ui.BodyStrong).Width + Ui.S(16));
                var cap = new Rectangle(x, (Height - capH) / 2, w, capH);
                Ui.FillRound(g, cap, Ui.S(6), P.Keycap);
                using (var pen = new Pen(P.ControlBorder))
                    g.DrawLine(pen, cap.Left + Ui.S(3), cap.Bottom, cap.Right - Ui.S(3), cap.Bottom);
                Ui.Text(g, part, Ui.BodyStrong, cap, P.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += w + Ui.S(6);
            }
        }
    }

    /// <summary>Algo que necesita saber el ancho disponible para calcular su alto.</summary>
    internal interface IWidthAware
    {
        void FitWidth(int width);
    }

    /// <summary>Fila de ajuste: icono, título, descripción y un control a la derecha.</summary>
    internal sealed class Row : Panel, IWidthAware
    {
        readonly string glyph, title;
        string description;
        readonly Control accessory;

        /// <summary>Color especial para la descripción (p. ej. verde/rojo de estado); null = normal.</summary>
        public Color? DescriptionColor;

        /// <summary>Cambia la descripción (se usa para mostrar estados como "✓ Disponible").</summary>
        public void SetDescription(string text, Color? color)
        {
            description = text;
            DescriptionColor = color;
            Invalidate();
        }

        Timer hoverWatch;
        bool hovered;

        public Row(string glyph, string title, string description, Control accessory)
        {
            this.glyph = glyph;
            this.title = title;
            this.description = description;
            this.accessory = accessory;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Palette.Current.Card;
            Height = Ui.S(description == null ? 64 : 74);
            if (accessory != null) Controls.Add(accessory);
        }

        public bool Dimmed;

        int TextLeft { get { return glyph != null ? Ui.S(72) : Ui.S(22); } }

        // ─── Resaltado al pasar el ratón (también encima de los controles hijos) ───

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHover(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) { SetHover(false); return; }
            // El ratón ha pasado a un control hijo: vigilar hasta que salga de la fila.
            if (hoverWatch == null)
            {
                hoverWatch = new Timer();
                hoverWatch.Interval = 120;
                hoverWatch.Tick += delegate
                {
                    if (IsDisposed || !ClientRectangle.Contains(PointToClient(Cursor.Position)))
                    {
                        hoverWatch.Stop();
                        SetHover(false);
                    }
                };
            }
            hoverWatch.Start();
        }

        void SetHover(bool on)
        {
            if (hovered == on || IsDisposed) return;
            hovered = on;
            BackColor = on && !Dimmed ? Palette.Current.CardHover : Palette.Current.Card;   // los hijos lo heredan
            Invalidate(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && hoverWatch != null) { hoverWatch.Stop(); hoverWatch.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Ui.PaintCard(e.Graphics, ClientRectangle, Ui.SurfaceOf(Parent), BackColor);
        }

        int TextWidth
        {
            get { return Math.Max(Ui.S(80), (accessory != null ? accessory.Left - Ui.S(16) : Width - Ui.S(20)) - TextLeft); }
        }

        public void FitWidth(int width)
        {
            Width = width;
            if (accessory != null) accessory.Left = Width - accessory.Width - Ui.S(18);
            int textHeight = Ui.Measure(title, Ui.Body).Height;
            if (description != null)
                textHeight += Ui.S(2) + TextRenderer.MeasureText(description, Ui.Caption, new Size(TextWidth, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height;
            int needed = Math.Max(accessory != null ? accessory.Height : 0, textHeight) + Ui.S(32);
            Height = Math.Max(Ui.S(description == null ? 64 : 74), needed);
            if (accessory != null) accessory.Top = (Height - accessory.Height) / 2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Palette.Current;
            Graphics g = e.Graphics;
            Color titleColor = Dimmed ? p.Disabled : p.Text;
            if (glyph != null)
            {
                // Icono dentro de una baldosa redondeada teñida con el color de acento.
                int tile = Ui.S(36);
                var r = new Rectangle(Ui.S(20), (Height - tile) / 2, tile, tile);
                Ui.FillRound(g, r, Ui.S(9), Dimmed ? p.Hover : p.Tile);
                Ui.Text(g, glyph, Ui.Icon, r, Dimmed ? p.Disabled : p.TileGlyph,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            bool hasDesc = !string.IsNullOrEmpty(description);
            int titleH = Ui.Measure(title, Ui.Body).Height;
            int descH = !hasDesc ? 0 : TextRenderer.MeasureText(description, Ui.Caption, new Size(TextWidth, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height;
            int top = (Height - titleH - (descH > 0 ? descH + Ui.S(2) : 0)) / 2;
            Ui.Text(g, title, Ui.Body, new Rectangle(TextLeft, top, TextWidth, titleH), titleColor, TextFormatFlags.EndEllipsis);
            if (hasDesc)
                Ui.Text(g, description, Ui.Caption, new Rectangle(TextLeft, top + titleH + Ui.S(2), TextWidth, descH),
                    Dimmed ? p.Disabled : DescriptionColor ?? p.SubText, TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Grupo de filas: cada fila es su propia tarjeta redondeada, con un pequeño hueco entre ellas.</summary>
    internal sealed class Card : Panel, IWidthAware
    {
        public Card()
        {
            DoubleBuffered = true;
            BackColor = Palette.Current.Window;
        }

        public Card Add(Control row)
        {
            Controls.Add(row);
            return this;
        }

        public void FitWidth(int width)
        {
            Width = width;
            int y = 0;
            foreach (Control row in Controls)
            {
                row.Left = 0;
                row.Top = y;
                var aware = row as IWidthAware;
                if (aware != null) aware.FitWidth(width);
                else row.Width = width;
                y += row.Height + Ui.S(5);
            }
            Height = Math.Max(1, y - Ui.S(5));
        }
    }

    /// <summary>
    /// Página que apila títulos y tarjetas, con desplazamiento por rueda y una barra fina
    /// moderna que solo aparece si el contenido no cabe.
    /// </summary>
    internal sealed class Page : Panel
    {
        readonly List<KeyValuePair<Control, int>> items = new List<KeyValuePair<Control, int>>();
        int offset, contentHeight;

        public Page(string title)
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Palette.Current.Window;
            Add(new TextLabel(title, TextLabel.Kind.Title), Ui.S(26));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-e.Delta / 120 * Ui.S(48));
        }

        void ScrollBy(int delta)
        {
            int max = Math.Max(0, contentHeight - ClientSize.Height);
            int next = Math.Max(0, Math.Min(max, offset + delta));
            if (next == offset) return;
            offset = next;
            Relayout();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (contentHeight <= ClientSize.Height) return;
            // Indicador de desplazamiento fino y redondeado, como en Windows 11.
            float track = ClientSize.Height - Ui.S(16);
            float thumb = Math.Max(Ui.S(40), track * ClientSize.Height / contentHeight);
            float top = Ui.S(8) + (track - thumb) * offset / Math.Max(1, contentHeight - ClientSize.Height);
            Ui.FillRound(e.Graphics, new RectangleF(ClientSize.Width - Ui.S(8), top, Ui.S(3), thumb), Ui.S(1.5f),
                Palette.Current.SubText);
        }

        /// <summary>Añade un elemento con <paramref name="spaceBefore"/> píxeles de separación.</summary>
        public Page Add(Control control, int spaceBefore)
        {
            items.Add(new KeyValuePair<Control, int>(control, spaceBefore));
            Controls.Add(control);
            return this;
        }

        public Page Section(string heading)
        {
            return Add(new TextLabel(heading, TextLabel.Kind.Heading), Ui.S(24));
        }

        public Page Card(Card card)
        {
            return Add(card, Ui.S(8));
        }

        public Page Note(string text)
        {
            return Add(new TextLabel(text, TextLabel.Kind.Sub, Ui.S(400)), Ui.S(10));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        public void Relayout()
        {
            int left = Ui.S(36), right = Ui.S(30);
            int width = Math.Min(Ui.S(1000), ClientSize.Width - left - right);
            if (width <= 0) return;
            int y = 0;
            foreach (KeyValuePair<Control, int> item in items)
            {
                Control c = item.Key;
                y += item.Value;
                var aware = c as IWidthAware;
                if (aware != null) aware.FitWidth(width);
                var label = c as TextLabel;
                if (label != null && label.Width > Ui.S(110)) label.Fit(width);
                c.Left = left;
                c.Top = y - offset;
                y += c.Height;
            }
            contentHeight = y + Ui.S(30);
            int max = Math.Max(0, contentHeight - ClientSize.Height);
            if (offset > max) { offset = max; Relayout(); }
        }
    }

    /// <summary>Menú lateral con icono, nombre de la app y secciones.</summary>
    internal sealed class NavList : ModernControl
    {
        readonly string[] glyphs, labels;
        readonly Color[] colors;
        int selected, hoverIndex = -1;
        Bitmap logo;
        public event EventHandler SelectedChanged;

        public NavList(string[] glyphs, string[] labels, Color[] colors, int selectedIndex)
        {
            this.glyphs = glyphs;
            this.labels = labels;
            this.colors = colors;
            selected = selectedIndex;
            Cursor = Cursors.Hand;
            logo = Icons.Render(Ui.S(36));
        }

        int Top0 { get { return Ui.S(96); } }
        int ItemH { get { return Ui.S(44); } }

        public int SelectedIndex { get { return selected; } }

        int IndexAt(Point pt)
        {
            if (pt.Y < Top0) return -1;
            int i = (pt.Y - Top0) / ItemH;
            return i < labels.Length ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (i != hoverIndex) { hoverIndex = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverIndex = -1; Invalidate(); }

        // Se elige al pulsar (no al soltar): así responde al primer clic aunque la ventana
        // no estuviera activa.
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Location);
            if (i < 0 || i == selected) return;
            selected = i;
            Invalidate();
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(P.Window);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, Ui.S(18), Ui.S(26), Ui.S(36), Ui.S(36));
            Ui.Text(g, "PinTop", Ui.Subtitle, new Rectangle(Ui.S(66), Ui.S(22), Width - Ui.S(70), Ui.S(28)), P.Text, TextFormatFlags.Left);
            Ui.Text(g, "Versión " + Program.Version, Ui.Caption, new Rectangle(Ui.S(67), Ui.S(50), Width - Ui.S(70), Ui.S(20)),
                P.SubText, TextFormatFlags.Left);

            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rectangle(Ui.S(10), Top0 + i * ItemH + Ui.S(2), Width - Ui.S(20), ItemH - Ui.S(4));
                bool isSelected = i == selected;
                Color color = colors[i];
                // Sección elegida: fondo teñido con SU color (redondeo moderado); al pasar, gris suave.
                if (isSelected) Ui.FillRound(g, r, Ui.S(8), Theme.Blend(P.Window, color, P.Dark ? 0.2f : 0.13f));
                else if (i == hoverIndex) Ui.FillRound(g, r, Ui.S(8), Theme.Blend(P.Window, P.Hover, 0.8f));

                // Baldosa de color propia de cada sección, con degradado y el icono en blanco.
                int tile = Ui.S(28);
                var t = new RectangleF(r.Left + Ui.S(10), r.Top + (r.Height - tile) / 2f, tile, tile);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Ui.Round(t, Ui.S(8)))
                using (var brush = new LinearGradientBrush(new PointF(t.Left, t.Top), new PointF(t.Right, t.Bottom),
                    Theme.Blend(color, Color.White, 0.18f), Theme.Blend(color, Color.Black, 0.12f)))
                    g.FillPath(brush, path);
                Ui.Text(g, glyphs[i], Ui.IconNav, Rectangle.Round(t), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                Ui.Text(g, labels[i], isSelected ? Ui.BodyStrong : Ui.Body,
                    new Rectangle(r.Left + Ui.S(50), r.Top, r.Width - Ui.S(54), r.Height), P.Text, TextFormatFlags.VerticalCenter);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) { logo.Dispose(); logo = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Tarjeta de cabecera con degradado del color de acento, logo grande, nombre y estado
    /// (como la cabecera de "Sistema" en Configuración de Windows 11).
    /// </summary>
    internal sealed class HeroCard : ModernControl, IWidthAware
    {
        readonly string title, subtitle;
        readonly string[] pills;
        Bitmap logo;

        public HeroCard(string title, string subtitle, params string[] pills)
        {
            this.title = title;
            this.subtitle = subtitle;
            this.pills = pills;
            logo = Icons.Render(Ui.S(72));
            Height = Ui.S(150);
        }

        public void FitWidth(int width)
        {
            Width = width;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            // Degradado de marca de PinTop (azul del logo → violeta), igual en cualquier PC.
            Color start = Palette.BrandBlue;
            Color end = Theme.Blend(Palette.BrandViolet, Color.Black, 0.15f);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Ui.Round(r, Ui.S(14)))
            {
                using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(Width, Height), start, end))
                    g.FillPath(brush, path);
                // Marca de agua: chincheta grande y translúcida a la derecha, recortada por la tarjeta.
                Region old = g.Clip;
                g.SetClip(path);
                using (var font = new Font(Ui.Icon.FontFamily, Ui.S(110), GraphicsUnit.Pixel))
                using (var brush = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
                    g.DrawString("", font, brush, Width - Ui.S(190), -Ui.S(6));
                g.Clip = old;
                // Brillo sutil arriba.
                using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255)))
                    g.DrawPath(pen, path);
            }

            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int logoSize = Ui.S(72);
            int logoY = (Height - logoSize) / 2;
            using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            using (GraphicsPath sp = Ui.Round(new RectangleF(Ui.S(28), logoY + Ui.S(4), logoSize, logoSize), logoSize * 0.23f))
                g.FillPath(shadow, sp);
            g.DrawImage(logo, Ui.S(28), logoY, logoSize, logoSize);

            int x = Ui.S(28) + logoSize + Ui.S(22);
            Color textColor = Color.White;
            Color subColor = Theme.Blend(Color.White, end, 0.2f);
            using (var brush = new SolidBrush(textColor))
                g.DrawString(title, Ui.Title, brush, x - Ui.S(3), Ui.S(26));
            using (var brush = new SolidBrush(subColor))
                g.DrawString(subtitle, Ui.Body, brush, x, Ui.S(70));

            // "Pastillas" de estado.
            int px = x;
            int py = Ui.S(102);
            foreach (string pill in pills)
            {
                SizeF s = g.MeasureString(pill, Ui.Caption);
                var pr = new RectangleF(px, py, s.Width + Ui.S(18), Ui.S(24));
                using (GraphicsPath pp = Ui.Round(pr, pr.Height / 2f))
                using (var bg = new SolidBrush(Color.FromArgb(46, 255, 255, 255)))
                    g.FillPath(bg, pp);
                using (var brush = new SolidBrush(Color.White))
                    g.DrawString(pill, Ui.Caption, brush, pr.X + Ui.S(9), pr.Y + (pr.Height - s.Height) / 2f + 1);
                px += (int)pr.Width + Ui.S(8);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && logo != null) { logo.Dispose(); logo = null; }
            base.Dispose(disposing);
        }
    }

    /// <summary>Lista de apps como etiquetas con ✕, y botón para añadir desde las apps abiertas.</summary>
    internal sealed class ChipList : ModernControl, IWidthAware
    {
        readonly HashSet<string> target;
        readonly Func<List<string>> runningApps;
        readonly Action changed;
        readonly ModernButton add = new ModernButton("Añadir app", false, "");
        readonly List<KeyValuePair<string, Rectangle>> chips = new List<KeyValuePair<string, Rectangle>>();
        int hoverClose = -1;

        public ChipList(HashSet<string> target, Func<List<string>> runningApps, Action changed)
        {
            this.target = target;
            this.runningApps = runningApps;
            this.changed = changed;
            BackColor = Palette.Current.Card;   // fondo de la tarjeta (lo heredan los hijos)
            Controls.Add(add);
            add.Click += delegate { ShowAddMenu(); };
        }

        public void FitWidth(int width)
        {
            Width = width;
            chips.Clear();
            int x = Ui.S(18), y = Ui.S(14), h = Ui.S(32), gap = Ui.S(8);
            var sorted = new List<string>(target);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string app in sorted)
            {
                int w = Ui.Measure(app, Ui.Body).Width + Ui.S(48);
                if (x + w > width - Ui.S(18) && x > Ui.S(18)) { x = Ui.S(18); y += h + gap; }
                chips.Add(new KeyValuePair<string, Rectangle>(app, new Rectangle(x, y, w, h)));
                x += w + gap;
            }
            if (sorted.Count == 0)
            {
                x = Ui.S(18) + Ui.Measure("Ninguna app todavía", Ui.Body).Width + Ui.S(16);
            }
            if (x + add.Width > width - Ui.S(18) && x > Ui.S(18)) { x = Ui.S(18); y += h + gap; }
            add.Left = x;
            add.Top = y - Ui.S(1);
            Height = y + h + Ui.S(14);
            Invalidate();
        }

        Rectangle CloseRect(Rectangle chip)
        {
            return new Rectangle(chip.Right - Ui.S(30), chip.Top + Ui.S(4), Ui.S(24), chip.Height - Ui.S(8));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int found = -1;
            for (int i = 0; i < chips.Count; i++)
                if (CloseRect(chips[i].Value).Contains(e.Location)) found = i;
            Cursor = found >= 0 ? Cursors.Hand : Cursors.Default;
            if (found != hoverClose) { hoverClose = found; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < chips.Count; i++)
            {
                if (!CloseRect(chips[i].Value).Contains(e.Location)) continue;
                target.Remove(chips[i].Key);
                Changed();
                return;
            }
        }

        void Changed()
        {
            hoverClose = -1;
            if (changed != null) changed();
            var page = Parent != null ? Parent.Parent as Page : null;
            if (page != null) page.Relayout(); else FitWidth(Width);
        }

        void ShowAddMenu()
        {
            var menu = new ContextMenuStrip();
            int count = 0;
            foreach (string app in runningApps())
            {
                if (target.Contains(app)) continue;
                string a = app;
                menu.Items.Add(new ToolStripMenuItem(a, null, delegate { target.Add(a); Changed(); }));
                count++;
            }
            if (count == 0)
            {
                var none = new ToolStripMenuItem("No hay más apps abiertas");
                none.Enabled = false;
                menu.Items.Add(none);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Escribir el nombre…", null, delegate
            {
                string name = Prompt.Ask(FindForm(), "Añadir app", "Nombre del programa (por ejemplo: chrome, Notepad, bambu-studio):");
                if (name == null) return;
                name = name.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length == 0) return;
                target.Add(name);
                Changed();
            }));
            Theme.StyleMenu(menu);
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(add, new Point(0, add.Height + Ui.S(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Ui.PaintCard(g, ClientRectangle, Surface, BackColor);
            if (chips.Count == 0)
                Ui.Text(g, "Ninguna app todavía", Font, new Rectangle(Ui.S(18), Ui.S(14), Width, Ui.S(32)), P.SubText,
                    TextFormatFlags.VerticalCenter);
            for (int i = 0; i < chips.Count; i++)
            {
                Rectangle r = chips[i].Value;
                Ui.FillRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), r.Height / 2f, P.ControlBack, P.ControlBorder);
                Ui.Text(g, chips[i].Key, Font, new Rectangle(r.Left + Ui.S(14), r.Top, r.Width - Ui.S(44), r.Height), P.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                Rectangle close = CloseRect(r);
                if (i == hoverClose) Ui.FillRound(g, close, close.Height / 2f, P.ControlHover);
                Ui.Text(g, "", Ui.IconSmall, close, i == hoverClose ? P.Danger : P.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Pequeña ventana para escribir un texto, con el mismo estilo.</summary>
    internal static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string message)
        {
            Palette p = Palette.Current;
            using (var form = new Form())
            {
                form.Text = title;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = form.MaximizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;
                form.BackColor = p.Window;
                form.Font = Ui.Body;
                form.ClientSize = new Size(Ui.S(420), Ui.S(150));

                var label = new TextLabel(message, TextLabel.Kind.Normal, Ui.S(380));
                label.Location = new Point(Ui.S(20), Ui.S(16));
                form.Controls.Add(label);

                var box = new TextBox();
                box.BorderStyle = BorderStyle.FixedSingle;
                box.BackColor = p.ControlBack;
                box.ForeColor = p.Text;
                box.Font = Ui.Body;
                box.SetBounds(Ui.S(20), label.Bottom + Ui.S(10), Ui.S(380), Ui.S(30));
                form.Controls.Add(box);

                var ok = new ModernButton("Añadir", true);
                var cancel = new ModernButton("Cancelar");
                cancel.Location = new Point(form.ClientSize.Width - cancel.Width - Ui.S(20), form.ClientSize.Height - cancel.Height - Ui.S(16));
                ok.Location = new Point(cancel.Left - ok.Width - Ui.S(8), cancel.Top);
                ok.Click += delegate { form.DialogResult = DialogResult.OK; };
                cancel.Click += delegate { form.DialogResult = DialogResult.Cancel; };
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { form.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; }
                    if (e.KeyCode == Keys.Escape) { form.DialogResult = DialogResult.Cancel; e.SuppressKeyPress = true; }
                };
                form.HandleCreated += delegate { Theme.StyleTitleBar(form.Handle, p); };
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
