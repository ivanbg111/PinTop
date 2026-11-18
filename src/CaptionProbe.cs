using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Automation;

namespace PinTop
{
    /// <summary>
    /// Busca con UI Automation dónde están de verdad los botones minimizar/maximizar/cerrar de
    /// una ventana. Funciona también con apps que dibujan su propia barra de título (Chrome,
    /// Electron, WinUI...). Se ejecuta en un hilo propio porque UIA puede tardar.
    /// </summary>
    internal sealed class CaptionProbe : IDisposable
    {
        readonly object gate = new object();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly Thread thread;
        volatile bool stopping;

        bool hasPending;
        int pendingTicket;
        IntPtr pendingHwnd;
        Rectangle pendingFrame;
        float pendingScale;

        /// <summary>Se lanza en el hilo de trabajo: (ticket, hwnd, rectángulo de los botones o null).</summary>
        public event Action<int, IntPtr, Rectangle?> Completed;

        public CaptionProbe()
        {
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "CaptionProbe";
            thread.Start();
        }

        /// <summary>Pide analizar una ventana. Solo se atiende la petición más reciente.</summary>
        public void Request(int ticket, IntPtr hwnd, Rectangle frame, float scale)
        {
            lock (gate)
            {
                pendingTicket = ticket;
                pendingHwnd = hwnd;
                pendingFrame = frame;
                pendingScale = scale;
                hasPending = true;
            }
            signal.Set();
        }

        void Run()
        {
            while (!stopping)
            {
                signal.WaitOne();
                if (stopping) break;

                int ticket;
                IntPtr hwnd;
                Rectangle frame;
                float scale;
                lock (gate)
                {
                    if (!hasPending) continue;
                    ticket = pendingTicket;
                    hwnd = pendingHwnd;
                    frame = pendingFrame;
                    scale = pendingScale;
                    hasPending = false;
                }

                Rectangle? result;
                try
                {
                    result = FindCaptionButtons(hwnd, frame, scale);
                }
                catch (Exception)
                {
                    result = null;   // ventana cerrada, app colgada, sin soporte UIA...
                }

                Action<int, IntPtr, Rectangle?> handler = Completed;
                if (handler != null && !stopping) handler(ticket, hwnd, result);
            }
        }

        static Rectangle? FindCaptionButtons(IntPtr hwnd, Rectangle frame, float scale)
        {
            var band = new System.Windows.Rect(frame.Left, frame.Top, frame.Width, 90 * scale);
            var buttons = new List<Rectangle>();

            var cache = new CacheRequest();
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.ControlTypeProperty);

            using (cache.Activate())
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                var queue = new Queue<KeyValuePair<AutomationElement, int>>();
                queue.Enqueue(new KeyValuePair<AutomationElement, int>(root, 0));
                int visited = 0;
                Stopwatch clock = Stopwatch.StartNew();

                // Recorrido en anchura limitado a la franja superior de la ventana.
                while (queue.Count > 0 && visited < 700 && clock.ElapsedMilliseconds < 900)
                {
                    KeyValuePair<AutomationElement, int> item = queue.Dequeue();
                    AutomationElementCollection children;
                    try
                    {
                        children = item.Key.FindAll(TreeScope.Children, Condition.TrueCondition);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (AutomationElement child in children)
                    {
                        visited++;
                        System.Windows.Rect r;
                        ControlType type;
                        try
                        {
                            r = (System.Windows.Rect)child.GetCachedPropertyValue(AutomationElement.BoundingRectangleProperty);
                            type = (ControlType)child.GetCachedPropertyValue(AutomationElement.ControlTypeProperty);
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                        if (r.IsEmpty || r.Width <= 0 || r.Height <= 0 || !r.IntersectsWith(band)) continue;

                        if (type == ControlType.Button)
                            buttons.Add(new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y),
                                (int)Math.Round(r.Width), (int)Math.Round(r.Height)));
                        if (item.Value < 10)
                            queue.Enqueue(new KeyValuePair<AutomationElement, int>(child, item.Value + 1));
                    }
                }
            }

            return FindChain(buttons, frame, scale);
        }

        /// <summary>
        /// Los botones de la ventana son una fila de 1 a 4 botones pegados al borde derecho.
        /// No dependemos del idioma (Minimizar / Minimise / Réduire...), solo de la geometría.
        /// </summary>
        static Rectangle? FindChain(List<Rectangle> buttons, Rectangle frame, float scale)
        {
            int tol = Math.Max(3, (int)Math.Round(4 * scale));
            int minW = (int)(20 * scale), maxW = (int)(90 * scale), minH = (int)(14 * scale);

            Rectangle? close = null;
            foreach (Rectangle b in buttons)
            {
                if (b.Width < minW || b.Width > maxW || b.Height < minH) continue;
                if (Math.Abs(b.Right - frame.Right) <= tol && b.Top >= frame.Top - tol && b.Top - frame.Top <= tol * 3)
                {
                    close = b;
                    break;
                }
            }
            if (close == null) return null;

            Rectangle left = close.Value;
            for (int count = 1; count < 4; count++)
            {
                bool found = false;
                foreach (Rectangle b in buttons)
                {
                    if (b.Width < minW || b.Width > maxW) continue;
                    if (Math.Abs(b.Right - left.Left) <= tol && Math.Abs(b.Top - left.Top) <= tol
                        && Math.Abs(b.Height - left.Height) <= tol)
                    {
                        left = b;
                        found = true;
                        break;
                    }
                }
                if (!found) break;
            }
            return Rectangle.FromLTRB(left.Left, close.Value.Top, close.Value.Right, close.Value.Bottom);
        }

        public void Dispose()
        {
            stopping = true;
            signal.Set();
        }
    }
}
