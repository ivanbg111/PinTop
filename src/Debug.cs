using System;
using System.IO;

namespace PinTop
{
    /// <summary>
    /// Registro de diagnóstico. Se activa creando la variable de entorno PINTOP_DEBUG=1;
    /// escribe en %TEMP%\PinTop\ (log + última franja de barra de título analizada).
    /// </summary>
    internal static class Debug
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("PINTOP_DEBUG") == "1";

        static string Dir
        {
            get { return Path.Combine(Path.GetTempPath(), "PinTop"); }
        }

        public static string PathFor(string file)
        {
            Directory.CreateDirectory(Dir);
            return Path.Combine(Dir, file);
        }

        public static void Log(string message)
        {
            if (!Enabled) return;
            try
            {
                File.AppendAllText(PathFor("debug.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
