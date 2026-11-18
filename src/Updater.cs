using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

namespace PinTop
{
    /// <summary>
    /// Consulta en GitHub si hay una versión nueva de PinTop. Solo pide la información pública
    /// de la última Release; no envía ningún dato del usuario.
    /// </summary>
    internal static class Updater
    {
        const string Api = "https://api.github.com/repos/ivanbg111/PinTop/releases/latest";
        public const string ReleasesPage = "https://github.com/ivanbg111/PinTop/releases/latest";

        /// <summary>Comprueba en segundo plano; si hay versión nueva llama a <paramref name="onNewer"/> (en otro hilo).</summary>
        public static void CheckAsync(Action<string> onNewer)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    var request = (HttpWebRequest)WebRequest.Create(Api);
                    request.UserAgent = "PinTop-update-check";
                    request.Accept = "application/vnd.github+json";
                    request.Timeout = 10000;
                    string json;
                    using (WebResponse response = request.GetResponse())
                    using (var reader = new StreamReader(response.GetResponseStream()))
                        json = reader.ReadToEnd();

                    Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(\\.[0-9]+){0,3})\"");
                    Debug.Log("update check: respuesta " + json.Length + " bytes, tag " + (m.Success ? m.Groups[1].Value : "no encontrado")
                        + ", versión actual " + Program.Version);
                    if (!m.Success) return;
                    string latest = m.Groups[1].Value;
                    if (Normalize(latest) > Normalize(Program.Version)) onNewer(latest);
                }
                catch (Exception ex)
                {
                    Debug.Log("update check: " + ex.Message);   // sin conexión, límite de GitHub...: se ignora
                }
            });
        }

        static Version Normalize(string text)
        {
            string[] parts = text.Split('.');
            int[] n = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out n[i]);
            return new Version(n[0], n[1], n[2]);
        }
    }
}
