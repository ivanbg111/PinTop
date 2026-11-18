using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PinTop
{
    internal enum HotkeyAction
    {
        TogglePin,
        OpacityDown,
        OpacityUp,
        ToggleButton
    }

    /// <summary>Combinación de teclas (modificadores MOD_* de Win32 + tecla virtual).</summary>
    internal struct Hotkey
    {
        public readonly uint Modifiers;
        public readonly uint Key;

        public static readonly Hotkey None = new Hotkey(0, 0);

        public Hotkey(uint modifiers, uint key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public bool IsEmpty { get { return Key == 0; } }

        public bool SameAs(Hotkey other)
        {
            return Modifiers == other.Modifiers && Key == other.Key;
        }

        public override string ToString()
        {
            return IsEmpty ? "Ninguno" : Describe(Modifiers, Key);
        }

        public string Serialize()
        {
            return Modifiers + "," + Key;
        }

        public static Hotkey Parse(string text)
        {
            string[] parts = text.Split(',');
            uint m, k;
            if (parts.Length == 2 && uint.TryParse(parts[0], out m) && uint.TryParse(parts[1], out k))
                return new Hotkey(m, k);
            return None;
        }

        public static string Describe(uint modifiers, uint key)
        {
            var parts = new List<string>();
            if ((modifiers & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & Native.MOD_SHIFT) != 0) parts.Add("Mayús");
            if ((modifiers & Native.MOD_WIN) != 0) parts.Add("Win");
            if (key != 0) parts.Add(KeyName((Keys)key));
            return string.Join(" + ", parts.ToArray());
        }

        static string KeyName(Keys k)
        {
            switch (k)
            {
                case Keys.PageUp: return "RePág";
                case Keys.PageDown: return "AvPág";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Space: return "Espacio";
                case Keys.Home: return "Inicio";
                case Keys.End: return "Fin";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Supr";
                case Keys.Back: return "Retroceso";
                case Keys.Enter: return "Intro";
                case Keys.Pause: return "Pausa";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Multiply: return "Num *";
                case Keys.Divide: return "Num /";
            }
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num " + (k - Keys.NumPad0);
            if (k >= Keys.F1 && k <= Keys.F24) return k.ToString();
            uint ch = Native.MapVirtualKey((uint)k, 2) & 0x7FFF;   // MAPVK_VK_TO_CHAR (respeta la distribución del teclado)
            if (ch > 32) return char.ToUpperInvariant((char)ch).ToString();
            return k.ToString();
        }
    }

    /// <summary>Configuración guardada en %APPDATA%\PinTop\settings.ini.</summary>
    internal sealed class Settings
    {
        public bool ShowButton = true;
        /// <summary>true: botón en todas las ventanas visibles; false: solo en la activa y las fijadas.</summary>
        public bool AllWindows = true;
        public int ButtonSize = 24;   // px lógicos: 20, 24 o 28
        public readonly Dictionary<HotkeyAction, Hotkey> Hotkeys = new Dictionary<HotkeyAction, Hotkey>();
        /// <summary>Posición elegida a mano por app: distancia (px lógicos) del borde derecho de la ventana.</summary>
        public readonly Dictionary<string, int> Offsets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public bool IsNew;

        public static string FilePath
        {
            get
            {
                return Path.Combine(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinTop"), "settings.ini");
            }
        }

        public static Settings CreateDefault()
        {
            var s = new Settings();
            uint ctrlAlt = Native.MOD_CONTROL | Native.MOD_ALT;
            s.Hotkeys[HotkeyAction.TogglePin] = new Hotkey(ctrlAlt, (uint)Keys.P);
            s.Hotkeys[HotkeyAction.OpacityDown] = new Hotkey(ctrlAlt, (uint)Keys.PageDown);
            s.Hotkeys[HotkeyAction.OpacityUp] = new Hotkey(ctrlAlt, (uint)Keys.PageUp);
            s.Hotkeys[HotkeyAction.ToggleButton] = Hotkey.None;
            return s;
        }

        public static Settings Load()
        {
            Settings s = CreateDefault();
            if (!File.Exists(FilePath))
            {
                s.IsNew = true;
                return s;
            }
            try
            {
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = raw.Substring(0, eq).Trim();
                    string value = raw.Substring(eq + 1).Trim();
                    int number;

                    if (key == "ShowButton") s.ShowButton = value != "0";
                    else if (key == "AllWindows") s.AllWindows = value != "0";
                    else if (key == "ButtonSize" && int.TryParse(value, out number)) s.ButtonSize = Math.Max(16, Math.Min(32, number));
                    else if (key.StartsWith("Hotkey."))
                    {
                        HotkeyAction action;
                        if (Enum.TryParse(key.Substring(7), out action)) s.Hotkeys[action] = Hotkey.Parse(value);
                    }
                    else if (key.StartsWith("Offset.") && int.TryParse(value, out number)) s.Offsets[key.Substring(7)] = number;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public void Save()
        {
            var lines = new List<string>();
            lines.Add("ShowButton=" + (ShowButton ? "1" : "0"));
            lines.Add("AllWindows=" + (AllWindows ? "1" : "0"));
            lines.Add("ButtonSize=" + ButtonSize);
            foreach (KeyValuePair<HotkeyAction, Hotkey> pair in Hotkeys)
                lines.Add("Hotkey." + pair.Key + "=" + pair.Value.Serialize());
            foreach (KeyValuePair<string, int> pair in Offsets)
                lines.Add("Offset." + pair.Key + "=" + pair.Value);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
                IsNew = false;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public Settings Clone()
        {
            var c = new Settings();
            c.ShowButton = ShowButton;
            c.AllWindows = AllWindows;
            c.ButtonSize = ButtonSize;
            foreach (KeyValuePair<HotkeyAction, Hotkey> pair in Hotkeys) c.Hotkeys[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, int> pair in Offsets) c.Offsets[pair.Key] = pair.Value;
            return c;
        }
    }
}
