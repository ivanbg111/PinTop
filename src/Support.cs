using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PinTop
{
    /// <summary>Ventana invisible que recibe el atajo de teclado global.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();

        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            var cp = new CreateParams();
            cp.Parent = new IntPtr(-3); // HWND_MESSAGE: ventana solo de mensajes
            CreateHandle(cp);
        }

        public bool Register(int id, uint modifiers, uint key)
        {
            if (!Native.RegisterHotKey(Handle, id, modifiers, key)) return false;
            ids.Add(id);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (int id in ids) Native.UnregisterHotKey(Handle, id);
            ids.Clear();
            DestroyHandle();
        }
    }

    /// <summary>Arranque automático con Windows (clave Run del usuario actual).</summary>
    internal static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "PinTop";

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
                if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue(ValueName, false);
            }
        }
    }

    /// <summary>Icono de la bandeja dibujado en tiempo de ejecución.</summary>
    internal static class Icons
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon CreateTrayIcon()
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (var font = new Font("Segoe MDL2 Assets", size * 0.62f, GraphicsUnit.Pixel))
                using (var sf = new StringFormat())
                using (var bg = new SolidBrush(Color.FromArgb(0, 120, 215)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.FillEllipse(bg, 0, 0, size - 1, size - 1);
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("", font, Brushes.White, new RectangleF(0, 1, size, size), sf);
                }
                IntPtr hIcon = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
        }
    }
}
