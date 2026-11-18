using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PinTop
{
    /// <summary>
    /// El botón flotante que se dibuja sobre la barra de título de la ventana activa.
    /// Toma el color de la barra para integrarse, nunca roba el foco y se puede arrastrar.
    /// </summary>
    internal sealed class PinButton : Form
    {
        readonly ToolTip tip = new ToolTip();
        Color baseColor = Color.FromArgb(32, 32, 32);
        Color accent = Theme.Accent();
        string hotkeyText = "";
        bool pinned, hover, shown;

        // Arrastre
        bool pressing, dragging;
        int pressScreenX, pressLeft;

        /// <summary>Rango permitido para la X izquierda del botón al arrastrarlo.</summary>
        public int DragMin, DragMax;

        /// <summary>Ventana a la que pertenece este botón.</summary>
        public IntPtr Target;

        public event Action<PinButton> PinClicked;
        public event Action<PinButton, int> DragEnded;
        /// <summary>Rueda del ratón sobre el botón (delta positivo = hacia arriba).</summary>
        public event Action<PinButton, int> Wheel;

        bool ghost;

        public PinButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = baseColor;
            Text = "PinTop";
            Bounds = new Rectangle(-32000, -32000, 24, 24);
            tip.ShowAlways = true;
            UpdateTooltip();

            Show();                 // crea la ventana sin activarla (ShowWithoutActivation)
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }

        public bool IsShown { get { return shown; } }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
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

        public void SetHotkeyText(string text)
        {
            hotkeyText = text;
            UpdateTooltip();
        }

        /// <summary>Color de la barra de título sobre la que está el botón.</summary>
        public void SetBaseColor(Color color)
        {
            accent = Theme.Accent();
            if (color.ToArgb() == baseColor.ToArgb()) return;
            baseColor = color;
            Invalidate();
        }

        /// <summary>
        /// Coloca el botón en <paramref name="bounds"/> (pantalla), en la pila de ventanas justo
        /// detrás de <paramref name="insertAfter"/>: es decir, justo encima de su ventana. Así no
        /// flota por encima de otras ventanas que la tapen.
        /// </summary>
        public void ShowAt(Rectangle bounds, bool isPinned, bool isGhost, IntPtr insertAfter)
        {
            if (dragging) return;
            if (isPinned != pinned || isGhost != ghost)
            {
                pinned = isPinned;
                ghost = isGhost;
                UpdateTooltip();
                Invalidate();
            }
            // El botón está siempre en la capa "siempre visible"; dentro de ella, justo donde le toca.
            if ((Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (!shown)
            {
                // Primero se coloca (aún oculto) y se muestra; después se fija su lugar en la pila.
                Native.SetWindowPos(Handle, insertAfter, bounds.X, bounds.Y, bounds.Width, bounds.Height, Native.SWP_NOACTIVATE);
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                shown = true;
            }
            Native.SetWindowPos(Handle, insertAfter, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                Native.SWP_NOACTIVATE);

            // Red de seguridad: si Windows lo sacó de la capa "siempre visible", devolverlo.
            if ((Native.GetWindowLong(Handle, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) == 0)
            {
                Debug.Log("zorder fallback target=" + Target + " after=" + insertAfter);
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        public void HideButton()
        {
            if (!shown) return;
            Native.ShowWindow(Handle, Native.SW_HIDE);
            shown = false;
            hover = false;
            pressing = dragging = false;
        }

        void UpdateTooltip()
        {
            string action = pinned ? "Soltar ventana" : "Fijar encima de todo";
            if (hotkeyText.Length > 0) action += " (" + hotkeyText + ")";
            if (ghost) action = "Modo fantasma activo: los clics atraviesan esta ventana\n" + action;
            tip.SetToolTip(this, action + "\nRueda: transparencia · Arrástralo para moverlo · Clic derecho: más opciones");
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            int radius = Math.Max(3, ClientSize.Height / 5);
            using (GraphicsPath path = RoundedRect(new Rectangle(Point.Empty, ClientSize), radius))
            {
                Region = new Region(path);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color back, ink;
            if (pinned)
            {
                // En modo fantasma el acento se ve "lavado", para distinguirlo de un fijado normal.
                Color fill = ghost ? Theme.Blend(accent, baseColor, 0.5f) : accent;
                back = hover ? Theme.Blend(fill, Color.White, 0.18f) : fill;
                ink = Theme.InkFor(fill);
            }
            else
            {
                ink = Theme.InkFor(baseColor);
                back = hover ? Theme.Blend(baseColor, ink, 0.14f) : baseColor;
            }
            e.Graphics.Clear(back);
            Glyphs.DrawPinIcon(e.Graphics, new Rectangle(Point.Empty, ClientSize), ink, back, pinned);
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Wheel == null || e.Delta == 0) return;
            // Una "muesca" de rueda son 120; algunos ratones mandan varias de golpe.
            int notches = Math.Max(1, Math.Abs(e.Delta) / 120);
            for (int i = 0; i < notches; i++) Wheel(this, e.Delta);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            pressing = true;
            dragging = false;
            pressScreenX = Cursor.Position.X;
            pressLeft = Bounds.Left;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressing) return;
            int dx = Cursor.Position.X - pressScreenX;
            if (!dragging && Math.Abs(dx) > 4)
            {
                dragging = true;
                Cursor = Cursors.SizeWE;
                tip.Hide(this);
            }
            if (dragging)
            {
                int left = Math.Max(DragMin, Math.Min(DragMax, pressLeft + dx));
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, left, Bounds.Top, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !pressing) return;
            pressing = false;
            Capture = false;
            if (dragging)
            {
                dragging = false;
                Cursor = Cursors.Default;
                if (DragEnded != null) DragEnded(this, Bounds.Left);
            }
            else if (ClientRectangle.Contains(e.Location) && PinClicked != null)
            {
                PinClicked(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
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
