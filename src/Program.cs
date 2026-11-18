using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace PinTop
{
    internal static class Program
    {
        const string MutexName = "PinTop.SingleInstance";

        public static string Version
        {
            get
            {
                // Solo para probar el aviso de actualización (modo diagnóstico).
                string fake = Debug.Enabled ? Environment.GetEnvironmentVariable("PINTOP_FAKE_VERSION") : null;
                if (!string.IsNullOrEmpty(fake)) return fake;
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }

        [STAThread]
        static void Main(string[] args)
        {
            // Herramientas de desarrollo / línea de comandos
            if (args.Length >= 2 && args[0] == "--export-icon")
            {
                Icons.WriteIco(args[1]);
                return;
            }
            if (args.Length >= 1 && args[0] == "--exit")
            {
                Signal(TrayApp.ExitEvent);
                return;
            }

            bool isFirstInstance;
            using (var mutex = new Mutex(true, MutexName, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // Ya está abierto: en vez de un error, abrimos sus ajustes.
                    Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                    if (!Signal(TrayApp.ShowSettingsEvent))
                        MessageBox.Show("PinTop ya se está ejecutando (mira el icono junto al reloj).", "PinTop",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // Red de seguridad: un error inesperado nunca debe dejar ventanas fijadas,
                // transparentes o en modo fantasma.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    // Error en la interfaz: se registra y la app sigue funcionando.
                    Debug.Log("error: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    // Error en otro hilo: la app se va a cerrar; antes, dejar las ventanas como estaban.
                    Debug.Log("error fatal: " + e.ExceptionObject);
                    try { WindowManager.RestoreAll(); } catch (Exception) { }
                };

                Ui.Init();   // escala por DPI y fuentes de la interfaz
                Application.Run(new TrayApp());
            }
        }

        static bool Signal(string name)
        {
            EventWaitHandle handle;
            if (!EventWaitHandle.TryOpenExisting(name, out handle)) return false;
            using (handle) handle.Set();
            return true;
        }
    }
}
