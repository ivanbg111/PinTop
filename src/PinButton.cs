using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PinTop
{
    /// <summary>
    /// El botón flotante que se dibuja sobre la barra de título de la ventana activa.
    /// Nunca roba el foco: la ventana de debajo sigue siendo la activa.
    /// </summary>
    internal sealed class PinButton : Form
    {
        static readonly Color Accent = Color.FromArgb(0, 120, 215);
        static readonly Color AccentHover = Color.FromArgb(40, 145, 235);
        static readonly Color Idle = Color.FromArgb(52, 52, 52);
        static readonly Color IdleHover = Color.FromArgb(85, 85, 85);

        const string GlyphPin = "";       // Segoe MDL2 Assets: Pin
        const string GlyphPinned = "";    // Segoe MDL2 Assets: PinnedFill

        readonly ToolTip tip = new ToolTip();
        Font glyphFont;
        bool pinned, hover, shown;

        public event EventHandler PinClicked;

        public PinButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Idle;
            Text = "PinTop";
            Bounds = new Rectangle(-32000, -32000, 24, 24);
            tip.ShowAlways = true;
            UpdateTooltip();

            Show();                 // crea la ventana sin activarla (ShowWithoutActivation)
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            // Hacer clic en el botón no debe activar nuestra ventana.
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Coloca el botón en <paramref name="bounds"/> (pantalla) por encima de todo.</summary>
        public void ShowAt(Rectangle bounds, bool isPinned)
        {
            if (isPinned != pinned)
            {
                pinned = isPinned;
                UpdateTooltip();
                Invalidate();
            }
            // Se reafirma siempre HWND_TOPMOST: al fijar una ventana, esta pasa por delante del botón.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE);
            if (!shown)
            {
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                shown = true;
            }
        }

        public void HideButton()
        {
            if (!shown) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            shown = false;
            hover = false;
        }

        void UpdateTooltip()
        {
            tip.SetToolTip(this, pinned
                ? "Soltar ventana (Ctrl+Alt+P)\nClic derecho: transparencia"
                : "Fijar encima de todo (Ctrl+Alt+P)\nClic derecho: transparencia");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            int radius = Math.Max(4, ClientSize.Height / 4);
            using (var path = RoundedRect(new Rectangle(Point.Empty, ClientSize), radius))
            {
                Region = new Region(path);
            }

            if (glyphFont != null) glyphFont.Dispose();
            glyphFont = new Font("Segoe MDL2 Assets", Math.Max(8f, ClientSize.Height * 0.5f), GraphicsUnit.Pixel);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(pinned ? (hover ? AccentHover : Accent) : (hover ? IdleHover : Idle));

            if (glyphFont == null) return;
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(pinned ? GlyphPinned : GlyphPin, glyphFont, Brushes.White,
                    new RectangleF(0, 1, ClientSize.Width, ClientSize.Height), sf);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location) && PinClicked != null)
                PinClicked(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tip.Dispose();
                if (glyphFont != null) glyphFont.Dispose();
            }
            base.Dispose(disposing);
        }

        static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
