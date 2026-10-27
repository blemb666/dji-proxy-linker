// Шаг 3: скрипт для DaVinci Resolve, который привязывает локальные прокси к оригиналам на сервере.
// Пары «папка на сервере → папка на компьютере» вшиваются прямо в .lua: так скрипту не нужно
// читать файлы (Lua в Resolve не открывает пути с кириллицей).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace DjiProxyLinker
{
    public static class ResolveScript
    {
        public const string MenuName = "Link Local Proxies";

        public static string ScriptPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Blackmagic Design\DaVinci Resolve\Support\Fusion\Scripts\Utility", MenuName + ".lua");
            }
        }

        // Записывает скрипт со всеми парами папок; возвращает путь к нему
        public static string Install(IEnumerable<KeyValuePair<string, string>> mappings)
        {
            string template;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("LinkLocalProxies.lua"))
            using (var reader = new StreamReader(s, Encoding.UTF8))
                template = reader.ReadToEnd();

            var lines = new StringBuilder();
            foreach (var m in Expand(mappings))
                lines.AppendFormat("    {{ [==[{0}]==], [==[{1}]==] }},\n", m.Key, m.Value);

            Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath));
            File.WriteAllText(ScriptPath, template.Replace("--@@MAPPINGS@@\n", lines.ToString())
                                                  .Replace("--@@MAPPINGS@@\r\n", lines.ToString()),
                              new UTF8Encoding(false));
            return ScriptPath;
        }

        // Клипы в проекте могут быть импортированы и по UNC-пути, и через подключённый сетевой диск (Z:\…) —
        // добавляем обе записи, чтобы скрипт узнал любую
        static IEnumerable<KeyValuePair<string, string>> Expand(IEnumerable<KeyValuePair<string, string>> mappings)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var m in mappings)
                foreach (var source in NetworkPaths.Equivalents(m.Key))
                    if (!result.Any(r => string.Equals(r.Key, source, StringComparison.OrdinalIgnoreCase)))
                        result.Add(new KeyValuePair<string, string>(source, m.Value));
            return result;
        }
    }

    static class NetworkPaths
    {
        // Путь как есть + его вариант через подключённую букву диска или через UNC
        public static List<string> Equivalents(string path)
        {
            path = path.TrimEnd('\\');
            var result = new List<string> { path };
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Network))
            {
                string letter = drive.Name.TrimEnd('\\');          // "Z:"
                string unc = GetUnc(letter);                        // "\\server\public"
                if (unc == null) continue;
                unc = unc.TrimEnd('\\');

                if (StartsWithDir(path, unc)) result.Add(letter + path.Substring(unc.Length));
                else if (StartsWithDir(path, letter)) result.Add(unc + path.Substring(letter.Length));
            }
            return result;
        }

        static bool StartsWithDir(string path, string root)
        {
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   (path.Length == root.Length || path[root.Length] == '\\');
        }

        static string GetUnc(string letter)
        {
            var buffer = new StringBuilder(512);
            int length = buffer.Capacity;
            return WNetGetConnection(letter, buffer, ref length) == 0 ? buffer.ToString() : null;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
    }
}
