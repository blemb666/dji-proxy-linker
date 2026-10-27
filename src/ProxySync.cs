// Шаг 2: забрать папки Proxy с сервера на компьютер, повторяя структуру папок.
//   \\server\FastMotion\ZERO_U6\1 смена\FX30\Proxy\C0001.MP4
//     → D:\Proxy\ZERO_U6\1 смена\FX30\Proxy\C0001.MP4
// Повторный запуск докачивает только новое; файл сначала пишется в .part, поэтому обрыв сети
// не оставляет битых прокси.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DjiProxyLinker
{
    public sealed class SyncItem
    {
        public string Source;
        public string Target;
        public string Title;   // путь относительно корня, для журнала
        public long Length;
        public DateTime Modified;
    }

    public sealed class SyncPlan
    {
        public readonly List<SyncItem> ToCopy = new List<SyncItem>();
        public readonly List<string> Errors = new List<string>();
        public int Total;
        public long TotalBytes;

        public long CopyBytes { get { return ToCopy.Sum(i => i.Length); } }
        public int UpToDate { get { return Total - ToCopy.Count; } }
    }

    public static class ProxySync
    {
        const string PartExt = ".part";
        const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;
        static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2); // FAT/exFAT и SMB округляют время

        public static SyncPlan Plan(string sourceRoot, string destRoot)
        {
            var plan = new SyncPlan();
            sourceRoot = Path.GetFullPath(sourceRoot).TrimEnd('\\');
            destRoot = Path.GetFullPath(destRoot).TrimEnd('\\');

            var stack = new Stack<string>();
            stack.Push(sourceRoot);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try
                {
                    if (string.Equals(Path.GetFileName(dir), Linker.ProxyDirName, StringComparison.OrdinalIgnoreCase))
                        AddProxyDir(dir, sourceRoot, destRoot, plan);
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if ((File.GetAttributes(sub) & HiddenSystem) != HiddenSystem) stack.Push(sub);
                }
                catch (UnauthorizedAccessException) { plan.Errors.Add("Нет доступа: " + dir); }
                catch (IOException ex) { plan.Errors.Add(dir + ": " + ex.Message); }
            }

            plan.ToCopy.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Source, b.Source));
            return plan;
        }

        static void AddProxyDir(string dir, string sourceRoot, string destRoot, SyncPlan plan)
        {
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles())
            {
                if (file.Name.StartsWith("._", StringComparison.Ordinal) || file.Name.EndsWith(PartExt, StringComparison.OrdinalIgnoreCase))
                    continue;

                string rel = file.FullName.Substring(sourceRoot.Length).TrimStart('\\');
                var item = new SyncItem
                {
                    Source = file.FullName,
                    Target = Path.Combine(destRoot, rel),
                    Title = rel,
                    Length = file.Length,
                    Modified = file.LastWriteTimeUtc
                };
                plan.Total++;
                plan.TotalBytes += item.Length;

                var existing = new FileInfo(item.Target);
                bool same = existing.Exists && existing.Length == item.Length &&
                            (existing.LastWriteTimeUtc - item.Modified).Duration() <= TimeTolerance;
                if (!same) plan.ToCopy.Add(item);
            }
        }

        // onBytes вызывается после каждого блока; cancelled — проверка кнопки «Остановить»
        public static void Copy(SyncItem item, Action<long> onBytes, Func<bool> cancelled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Target));
            string part = item.Target + PartExt;
            try
            {
                using (var src = new FileStream(item.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
                using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    var buffer = new byte[4 << 20];
                    int read;
                    while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (cancelled()) throw new OperationCanceledException();
                        dst.Write(buffer, 0, read);
                        onBytes(read);
                    }
                }
                File.SetLastWriteTimeUtc(part, item.Modified);
                if (File.Exists(item.Target)) File.Delete(item.Target);
                File.Move(part, item.Target);
            }
            catch
            {
                try { File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }

        public static string FormatSize(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return unit == 0 ? bytes + " Б" : value.ToString(value < 10 ? "0.0" : "0") + " " + units[unit];
        }
    }
}
