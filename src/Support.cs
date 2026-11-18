using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PinTop
{
    /// <summary>Ventana invisible que recibe los atajos de teclado globales.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly List<int> ids = new List<int>();

        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            var cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE: ventana solo de mensajes
            CreateHandle(cp);
        }

        public bool Register(int id, Hotkey hotkey)
        {
            if (hotkey.IsEmpty) return true;
            if (!Native.RegisterHotKey(Handle, id, hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.Key)) return false;
            ids.Add(id);
            return true;
        }

        public void UnregisterAll()
        {
            foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
            ids.Clear();
        }

        /// <summary>¿Está libre esta combinación? (otra app no la tiene ya registrada)</summary>
        public static bool IsAvailable(Hotkey hotkey)
        {
            if (hotkey.IsEmpty) return true;
            const int probeId = 0xBFF0;
            if (!Native.RegisterHotKey(IntPtr.Zero, probeId, hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.Key)) return false;
            Native.UnregisterHotKey(IntPtr.Zero, probeId);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterAll();
            DestroyHandle();
        }
    }

    /// <summary>Arranque automático con Windows (clave Run del usuario actual).</summary>
    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "PinTop";

        static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                return key != null && key.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, Command);
                else key.DeleteValue(ValueName, false);
            }
        }

        /// <summary>Si estaba activado desde otra ruta (p. ej. se movió el .exe), apunta a la actual.</summary>
        public static void RefreshPath()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                object current = key.GetValue(ValueName);
                if (current != null && !string.Equals(current as string, Command, StringComparison.OrdinalIgnoreCase))
                    key.SetValue(ValueName, Command);
            }
        }
    }

    /// <summary>Colores: acento de Windows, mezclas y color de tinta legible sobre un fondo.</summary>
    internal static class Theme
    {
        public static readonly Color Brand = Color.FromArgb(0, 120, 215);

        public static Color Accent()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    object v = key == null ? null : key.GetValue("AccentColor");
                    if (v is int)
                    {
                        int c = (int)v;   // formato ABGR
                        return Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                    }
                }
            }
            catch (Exception) { }
            return Brand;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static bool IsDark(Color c)
        {
            return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 150;
        }

        public static Color InkFor(Color background)
        {
            return IsDark(background) ? Color.FromArgb(240, 240, 240) : Color.FromArgb(28, 28, 28);
        }

        // ─── Tema claro / oscuro ───

        /// <summary>0 = como Windows, 1 = claro, 2 = oscuro (lo fija TrayApp desde los ajustes).</summary>
        public static int Mode;

        public static bool IsDarkMode()
        {
            return Mode == 2 || (Mode == 0 && AppsUseDark());
        }

        public static bool AppsUseDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Barra de título del color de la ventana (Windows 11) y en modo oscuro si toca.</summary>
        public static void StyleTitleBar(IntPtr hwnd, Palette p)
        {
            int dark = p.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int caption = ColorRef(p.Window);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            int text = ColorRef(p.Text);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_TEXT_COLOR, ref text, sizeof(int));
        }

        static int ColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        /// <summary>Estilo moderno (claro u oscuro según el tema) para un menú.</summary>
        public static void StyleMenu(ToolStripDropDown menu)
        {
            Palette p = Palette.Create(IsDarkMode());
            menu.Renderer = new ModernMenuRenderer(p);
            menu.ForeColor = p.Text;
            menu.BackColor = p.Card;
            menu.Font = Ui.Body;
            menu.Padding = new Padding(Ui.S(4), Ui.S(4), Ui.S(4), Ui.S(4));
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripSeparator) continue;
                item.Padding = new Padding(Ui.S(4), Ui.S(5), Ui.S(4), Ui.S(5));
                item.Font = item.Font.Bold ? Ui.BodyStrong : Ui.Body;
            }
        }
    }

    /// <summary>Menús con aspecto de Windows 11: esquinas redondeadas al pasar, colores del tema.</summary>
    internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        readonly Palette p;

        public ModernMenuRenderer(Palette p) : base(new MenuColors(p))
        {
            this.p = p;
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(p.Card);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(p.CardBorder))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(Ui.S(2), 1, e.Item.Width - Ui.S(4), e.Item.Height - 2);
            using (GraphicsPath path = Ui.Round(r, Ui.S(4)))
            using (var brush = new SolidBrush(p.Hover))
                g.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(p.Divider))
                e.Graphics.DrawLine(pen, Ui.S(8), y, e.Item.Width - Ui.S(8), y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? p.Text : p.SubText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = p.SubText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Marca de verificación con el color de acento.
            Rectangle r = e.ImageRectangle;
            using (var pen = new Pen(p.Accent, Math.Max(1.6f, Ui.Scale * 1.6f)))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.2f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.74f),
                    new PointF(r.Left + r.Width * 0.8f, r.Top + r.Height * 0.28f)
                });
            }
        }
    }

    internal sealed class MenuColors : ProfessionalColorTable
    {
        readonly Palette p;

        public MenuColors(Palette p)
        {
            this.p = p;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground { get { return p.Card; } }
        public override Color ImageMarginGradientBegin { get { return p.Card; } }
        public override Color ImageMarginGradientMiddle { get { return p.Card; } }
        public override Color ImageMarginGradientEnd { get { return p.Card; } }
        public override Color MenuBorder { get { return p.CardBorder; } }
        public override Color MenuItemBorder { get { return p.Hover; } }
        public override Color MenuItemSelected { get { return p.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return p.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return p.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return p.Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return p.Hover; } }
        public override Color SeparatorDark { get { return p.Divider; } }
        public override Color SeparatorLight { get { return p.Divider; } }
        public override Color CheckBackground { get { return p.Card; } }
        public override Color CheckSelectedBackground { get { return p.Hover; } }
        public override Color CheckPressedBackground { get { return p.Hover; } }
    }

    /// <summary>Dibujo de la chincheta.</summary>
    internal static class Glyphs
    {
        // Misma fuente que usa Windows 11 para minimizar/maximizar/cerrar: se ve nítida
        // porque el texto GDI se ajusta a la rejilla de píxeles.
        static readonly string IconFont = PickIconFont();
        static Font cachedFont;
        static int cachedFontSize;

        static string PickIconFont()
        {
            using (var fonts = new System.Drawing.Text.InstalledFontCollection())
            {
                string fallback = null;
                foreach (FontFamily f in fonts.Families)
                {
                    if (f.Name == "Segoe Fluent Icons") return f.Name;
                    if (f.Name == "Segoe MDL2 Assets") fallback = f.Name;
                }
                return fallback;
            }
        }

        /// <summary>Chincheta para el botón: glifo de fuente nítido; si no hay fuente, dibujo vectorial.</summary>
        public static void DrawPinIcon(Graphics g, Rectangle box, Color ink, Color back, bool pinned)
        {
            if (IconFont == null)
            {
                DrawPin(g, box, ink, pinned);
                return;
            }
            int px = Math.Max(10, (int)Math.Round(box.Height * 0.6));
            if (cachedFont == null || cachedFontSize != px)
            {
                if (cachedFont != null) cachedFont.Dispose();
                cachedFont = new Font(IconFont, px, GraphicsUnit.Pixel);
                cachedFontSize = px;
            }
            TextRenderer.DrawText(g, pinned ? "" : "", cachedFont, box, ink, back,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        // Chincheta vertical en una caja de 16x16; luego se inclina 45°.
        static readonly PointF[] PinOutline =
        {
            new PointF(5f, 1f), new PointF(11f, 1f), new PointF(11f, 2.6f), new PointF(10f, 2.6f),
            new PointF(10f, 7f), new PointF(12.6f, 9.6f), new PointF(12.6f, 10.8f), new PointF(3.4f, 10.8f),
            new PointF(3.4f, 9.6f), new PointF(6f, 7f), new PointF(6f, 2.6f), new PointF(5f, 2.6f)
        };

        public static void DrawPin(Graphics g, RectangleF box, Color ink, bool filled, float fill = 0.68f)
        {
            float side = Math.Min(box.Width, box.Height) * fill;
            float s = side / 16f;
            var m = new Matrix();
            m.Translate(box.X + (box.Width - side) / 2f, box.Y + (box.Height - side) / 2f);
            m.Scale(s, s);
            m.RotateAt(45f, new PointF(8f, 8f));

            SmoothingMode old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            using (var pen = new Pen(ink, Math.Max(1f, 1.35f * s)))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                path.AddPolygon(PinOutline);
                path.Transform(m);
                if (filled)
                {
                    using (var brush = new SolidBrush(ink)) g.FillPath(brush, path);
                }
                g.DrawPath(pen, path);

                var needle = new[] { new PointF(8f, 10.8f), new PointF(8f, 15.2f) };
                m.TransformPoints(needle);
                g.DrawLine(pen, needle[0], needle[1]);
            }
            m.Dispose();
            g.SmoothingMode = old;
        }
    }

    /// <summary>Icono de la app: chincheta blanca sobre círculo azul.</summary>
    internal static class Icons
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var bg = new SolidBrush(Theme.Brand))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(bg, 0.5f, 0.5f, size - 1.5f, size - 1.5f);
                Glyphs.DrawPin(g, new RectangleF(0, 0, size, size), Color.White, true, 0.62f);
            }
            return bmp;
        }

        static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

        /// <summary>
        /// Icono de ventana con todos los tamaños (16 a 256 px): Windows elige el exacto para la
        /// barra de título, la barra de tareas o Alt+Tab, así nunca se ve borroso al escalarlo.
        /// </summary>
        public static Icon CreateWindowIcon()
        {
            var ms = new MemoryStream(BuildIco(IcoSizes));
            return new Icon(ms);
        }

        public static Icon CreateTrayIcon()
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap bmp = Render(size))
            {
                IntPtr hIcon = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
        }

        /// <summary>Genera un .ico con varias resoluciones (PNG embebidos). Uso: PinTop.exe --export-icon ruta.ico</summary>
        public static void WriteIco(string path)
        {
            File.WriteAllBytes(path, BuildIco(IcoSizes));
        }

        /// <summary>Formato .ico con una imagen PNG por tamaño.</summary>
        static byte[] BuildIco(int[] sizes)
        {
            var images = new List<byte[]>();
            foreach (int s in sizes)
            {
                using (Bitmap bmp = Render(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            }

            using (var output = new MemoryStream())
            using (var w = new BinaryWriter(output))
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
                w.Flush();
                return output.ToArray();
            }
        }
    }
}
