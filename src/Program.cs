using System;
using System.Threading;
using System.Windows.Forms;

namespace PinTop
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            bool isFirstInstance;
            using (var mutex = new Mutex(true, "PinTop.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    MessageBox.Show("PinTop ya se está ejecutando (mira el icono junto al reloj).", "PinTop",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
