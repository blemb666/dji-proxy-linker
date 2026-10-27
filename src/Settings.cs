// Настройки между запусками: %APPDATA%\DJI Proxy Linker\settings.ini (ключ=значение, UTF-8).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DjiProxyLinker
{
    sealed class Settings
    {
        public bool SonyFlatten;
        public string SyncSource = "";
        public string SyncDest = "";
        // Все пары «сервер → компьютер», которые когда-либо синхронизировались — для скрипта DaVinci
        public readonly List<KeyValuePair<string, string>> Mappings = new List<KeyValuePair<string, string>>();

        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "DJI Proxy Linker", "settings.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "SonyFlatten": s.SonyFlatten = value == "1"; break;
                        case "SyncSource": s.SyncSource = value; break;
                        case "SyncDest": s.SyncDest = value; break;
                        case "Mapping":
                            var parts = value.Split('|');
                            if (parts.Length == 2) s.AddMapping(parts[0], parts[1]);
                            break;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return s;
        }

        public void Save()
        {
            var lines = new List<string>
            {
                "SonyFlatten=" + (SonyFlatten ? "1" : "0"),
                "SyncSource=" + SyncSource,
                "SyncDest=" + SyncDest
            };
            lines.AddRange(Mappings.Select(m => "Mapping=" + m.Key + "|" + m.Value));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Новая пара заменяет старую с тем же серверным путём
        public void AddMapping(string source, string dest)
        {
            source = source.TrimEnd('\\');
            dest = dest.TrimEnd('\\');
            Mappings.RemoveAll(m => string.Equals(m.Key, source, StringComparison.OrdinalIgnoreCase));
            Mappings.Add(new KeyValuePair<string, string>(source, dest));
        }
    }
}
