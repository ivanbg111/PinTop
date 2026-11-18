using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PinTop
{
    /// <summary>
    /// Mira los píxeles de la barra de título para encontrar un hueco libre (sin iconos ni texto)
    /// donde poner el botón, y de paso averigua el color de fondo de la barra para integrarse.
    /// El botón debe estar oculto antes de llamar a estos métodos.
    /// </summary>
    internal static class ScreenScan
    {
        const int Threshold = 42;   // diferencia de color (suma RGB) para considerar un píxel "ocupado"

        /// <summary>Franja capturada: columnas ocupadas y color de fondo dominante.</summary>
        sealed class Strip
        {
            public bool[] Busy;
            public Color Background;
        }

        /// <summary>Obtiene la imagen de una zona de la pantalla (coordenadas de pantalla). Devuelve null si falla.</summary>
        public delegate Bitmap Grab(Rectangle screenArea);

        /// <summary>Captura lo que se ve en pantalla (la ventana debe estar destapada).</summary>
        public static Bitmap FromScreen(Rectangle area)
        {
            var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Left, area.Top, 0, 0, bmp.Size);
                return bmp;
            }
            catch (Exception)
            {
                bmp.Dispose();   // p. ej. escritorio seguro / pantalla bloqueada
                return null;
            }
        }

        /// <summary>Devuelve la X (izquierda) del botón: el hueco libre más cercano a la izquierda de <paramref name="anchorLeft"/>.</summary>
        public static int FindFreeSlot(int anchorLeft, int y, int size, int gap, int minLeft, float scale, Grab grab, out Color background)
        {
            int defaultX = anchorLeft - gap - size;
            int need = size + gap * 2;
            int width = Math.Min(anchorLeft - minLeft - 1, (int)Math.Round(520 * scale));
            background = Color.FromArgb(32, 32, 32);
            if (width < need || size <= 0) return defaultX;

            Strip strip = Capture(new Rectangle(anchorLeft - width, y, width, size), grab, "strip.png");
            if (strip == null) return defaultX;
            background = strip.Background;

            // El hueco libre más a la derecha (más cerca de minimizar).
            int run = 0;
            for (int col = width - 1; col >= 0; col--)
            {
                run = strip.Busy[col] ? 0 : run + 1;
                if (run >= need) return anchorLeft - width + col + gap;
            }
            return defaultX;
        }

        /// <summary>
        /// Cuando no sabemos dónde están los botones (barra personalizada sin accesibilidad),
        /// busca el icono de minimizar en la esquina derecha y devuelve dónde empieza su zona.
        /// </summary>
        public static int RefineEstimatedAnchor(Rectangle caption, int frameRight, float scale, Grab grab)
        {
            int left = caption.Left - (int)Math.Round(40 * scale);
            int top = caption.Top + caption.Height / 4;
            int height = Math.Max(4, caption.Height / 2);
            int width = frameRight - 2 - left;
            if (width <= 0) return caption.Left;

            Strip strip = Capture(new Rectangle(left, top, width, height), grab, "corner.png");
            if (strip == null) return caption.Left;

            for (int col = 0; col < width; col++)
            {
                if (!strip.Busy[col]) continue;
                // Primer icono (minimizar): dejamos un margen como el de los botones nativos.
                int glyphLeft = left + col;
                return Math.Min(caption.Right, glyphLeft - (int)Math.Round(12 * scale));
            }
            return caption.Left;
        }

        static Strip Capture(Rectangle area, Grab grab, string debugName)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            Bitmap grabbed = grab(area);
            if (grabbed == null) return null;
            using (Bitmap bmp = grabbed)
            {
                if (Debug.Enabled) bmp.Save(Debug.PathFor(debugName));

                int width = area.Width, height = area.Height;
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new byte[data.Stride * height];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                int stride = data.Stride;
                bmp.UnlockBits(data);

                // Color de fondo = el color más frecuente.
                var hist = new int[4096];
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                    {
                        int i = row * stride + col * 4;
                        hist[Bin(px, i)]++;
                    }
                int bestBin = 0;
                for (int b = 1; b < hist.Length; b++) if (hist[b] > hist[bestBin]) bestBin = b;

                long sr = 0, sg = 0, sb = 0, n = 0;
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                    {
                        int i = row * stride + col * 4;
                        if (Bin(px, i) != bestBin) continue;
                        sr += px[i + 2]; sg += px[i + 1]; sb += px[i]; n++;
                    }
                if (n == 0) return null;
                int br = (int)(sr / n), bgc = (int)(sg / n), bb = (int)(sb / n);

                var strip = new Strip();
                strip.Background = Color.FromArgb(br, bgc, bb);
                strip.Busy = new bool[width];
                for (int col = 0; col < width; col++)
                    for (int row = 0; row < height; row++)
                    {
                        int i = row * stride + col * 4;
                        int diff = Math.Abs(px[i + 2] - br) + Math.Abs(px[i + 1] - bgc) + Math.Abs(px[i] - bb);
                        if (diff > Threshold) { strip.Busy[col] = true; break; }
                    }

                if (Debug.Enabled)
                {
                    var map = new StringBuilder();
                    foreach (bool busy in strip.Busy) map.Append(busy ? '#' : '.');
                    Debug.Log(debugName + " area=" + area + " bg=" + strip.Background + "\n" + map);
                }
                return strip;
            }
        }

        static int Bin(byte[] px, int i)
        {
            return ((px[i + 2] >> 4) << 8) | ((px[i + 1] >> 4) << 4) | (px[i] >> 4);
        }
    }

    /// <summary>
    /// Imagen de una ventana pedida a Windows (PrintWindow): sirve aunque la ventana esté
    /// tapada por otras o por nuestros botones. Si la app no sabe pintarse así, usa la pantalla.
    /// </summary>
    internal sealed class WindowGrab : IDisposable
    {
        readonly Bitmap image;
        readonly Point origin;

        public WindowGrab(IntPtr hwnd)
        {
            Native.RECT r;
            if (Native.IsHungAppWindow(hwnd) || !Native.GetWindowRect(hwnd, out r) || r.Width <= 0 || r.Height <= 0) return;
            origin = new Point(r.Left, r.Top);
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                ok = Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
            }
            if (ok) image = bmp;
            else bmp.Dispose();
        }

        public Bitmap Grab(Rectangle screenArea)
        {
            if (image != null)
            {
                var local = new Rectangle(screenArea.X - origin.X, screenArea.Y - origin.Y, screenArea.Width, screenArea.Height);
                if (new Rectangle(Point.Empty, image.Size).Contains(local))
                {
                    Bitmap crop = image.Clone(local, PixelFormat.Format32bppArgb);
                    if (!IsBlank(crop)) return crop;
                    crop.Dispose();   // la app no se dejó pintar: imagen negra
                }
            }
            return ScreenScan.FromScreen(screenArea);
        }

        static bool IsBlank(Bitmap bmp)
        {
            for (int y = 0; y < bmp.Height; y += 2)
                for (int x = 0; x < bmp.Width; x += 2)
                    if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0) return false;
            return true;
        }

        public void Dispose()
        {
            if (image != null) image.Dispose();
        }
    }
}
