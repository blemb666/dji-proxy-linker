// Ядро: поиск готовых прокси камер и перенос их туда, где их ищет DaVinci Resolve —
// в подпапку Proxy рядом с исходниками, под тем же именем. Ничего не знает про UI.
//
//   DJI:  DJI_0001.LRF          → Proxy\DJI_0001.MP4        (расширение как у исходника)
//   Sony: SUB\C0001S03.MP4      → CLIP\Proxy\C0001.MP4      (исходник лежит в соседней CLIP)
//
// Sony с «поднятием» (sonyFlatten) — раскладка для сервера, папка камеры становится плоской:
//   FX30\CLIP\C0001.MP4, C0001M01.XML  → FX30\C0001.MP4, FX30\C0001M01.XML
//   FX30\SUB\C0001S03.MP4               → FX30\Proxy\C0001.MP4
// Каждое перемещение записывается в скрытый журнал FX30\.proxylinker-journal.txt — по нему работает откат.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DjiProxyLinker
{
    public enum Mode { Move, Copy, Undo }

    public enum Outcome { Done, Skipped, Failed }

    public sealed class Job
    {
        public string Source;
        public string Target;
        public string Title;       // как показать исходный файл в журнале
        public string Display;     // как показать, куда он переносится
        public string CleanupDir;  // удалить эту папку, если после переноса она опустела
        public bool ForceMove;     // переносить даже в режиме «Копировать» (оригиналы Sony из CLIP)
        public string Journal;     // журнал «поднятия» Sony, куда записать / откуда стереть строку
        public string JournalLine;
    }

    public sealed class ScanResult
    {
        public readonly List<Job> Jobs = new List<Job>();
        public readonly List<string> Errors = new List<string>();

        public int FolderCount
        {
            get
            {
                return Jobs.Select(j => Path.GetDirectoryName(j.Source))
                           .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            }
        }
    }

    public static class Linker
    {
        public const string ProxyDirName = "Proxy";
        public const string JournalName = ".proxylinker-journal.txt";
        const string SonyClipDir = "CLIP", SonySubDir = "SUB", SonyProxySuffix = "S03";
        static readonly string[] VideoExts = { ".MP4", ".MOV" };
        static readonly string[] SonyClipExts = { ".MP4", ".MXF", ".MOV" };
        static readonly Regex SonyProxyName = new Regex(@"^(.+)" + SonyProxySuffix + @"\.MP4$", RegexOptions.IgnoreCase);
        const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

        public static ScanResult Scan(IEnumerable<string> paths, Mode mode, bool sonyFlatten)
        {
            var result = new ScanResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in paths)
            {
                if (Directory.Exists(p)) Walk(Path.GetFullPath(p), mode, sonyFlatten, result, seen);
                else if (File.Exists(p)) AddFile(Path.GetFullPath(p), mode, false, true, result, seen);
                else result.Errors.Add("Не найдено: " + p);
            }

            result.Jobs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Source, b.Source));
            return result;
        }

        // Обход без рекурсии и без падения на недоступных папках (корень SD-карты, System Volume Information)
        static void Walk(string root, Mode mode, bool sonyFlatten, ScanResult result, HashSet<string> seen)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                        AddFile(f, mode, sonyFlatten, false, result, seen);
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if ((File.GetAttributes(sub) & HiddenSystem) != HiddenSystem) stack.Push(sub);
                }
                catch (UnauthorizedAccessException) { result.Errors.Add("Нет доступа: " + dir); }
                catch (IOException ex) { result.Errors.Add(dir + ": " + ex.Message); }
            }
        }

        // explicitFile: файл перетащили руками — тогда для исходника ищем его прокси
        static void AddFile(string file, Mode mode, bool sonyFlatten, bool explicitFile, ScanResult result, HashSet<string> seen)
        {
            string name = Path.GetFileName(file);
            // "._имя" — служебные файлы macOS на картах exFAT, к видео отношения не имеют
            if (name.StartsWith("._", StringComparison.Ordinal)) return;

            if (mode == Mode.Undo && name.Equals(JournalName, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var undo in JournalUndoJobs(file))
                    if (seen.Add(undo.Source)) result.Jobs.Add(undo);
                return;
            }

            Job job = mode == Mode.Undo ? UndoJob(file)
                    : sonyFlatten && !explicitFile ? FlattenJob(file) ?? LinkJob(file, false)
                    : LinkJob(file, explicitFile);
            if (job != null && seen.Add(job.Source)) result.Jobs.Add(job);
        }

        static Job LinkJob(string file, bool explicitFile)
        {
            string dir = Path.GetDirectoryName(file);
            if (IsDir(dir, ProxyDirName)) return null;

            if (HasExt(file, ".LRF")) return DjiJob(file);
            if (IsDir(dir, SonySubDir)) return SonyJob(file);
            if (!explicitFile || !IsVideo(file, SonyClipExts)) return null;

            // Перетащили исходник: DJI — LRF рядом, Sony — SUB\<имя>S03.MP4 рядом с CLIP
            string lrf = Path.ChangeExtension(file, ".LRF");
            if (File.Exists(lrf)) return DjiJob(lrf);
            if (IsDir(dir, SonyClipDir))
            {
                string sub = Path.Combine(Path.GetDirectoryName(dir), SonySubDir,
                                          Path.GetFileNameWithoutExtension(file) + SonyProxySuffix + ".MP4");
                if (File.Exists(sub)) return SonyJob(sub);
            }
            return null;
        }

        static Job DjiJob(string lrf)
        {
            string dir = Path.GetDirectoryName(lrf);
            string name = Path.GetFileNameWithoutExtension(lrf) + SourceExt(dir, Path.GetFileNameWithoutExtension(lrf));
            return new Job
            {
                Source = lrf,
                Target = Path.Combine(dir, ProxyDirName, name),
                Title = Path.GetFileName(lrf),
                Display = Path.Combine(ProxyDirName, name)
            };
        }

        // Берём прокси Sony только если в соседней CLIP есть исходник с тем же именем
        static Job SonyJob(string proxy)
        {
            string clipName = SonyClipName(proxy);
            string clipDir = FindSibling(Path.GetDirectoryName(proxy), SonyClipDir);
            if (clipName == null || clipDir == null || !HasClip(clipDir, clipName)) return null;

            string name = clipName + Path.GetExtension(proxy);
            return new Job
            {
                Source = proxy,
                Target = Path.Combine(clipDir, ProxyDirName, name),
                Title = Path.Combine(SonySubDir, Path.GetFileName(proxy)),
                Display = Path.Combine(Path.GetFileName(clipDir), ProxyDirName, name)
            };
        }

        // «Поднятие» Sony: работает только в папке камеры, где рядом лежат CLIP и SUB
        static Job FlattenJob(string file)
        {
            string dir = Path.GetDirectoryName(file);
            bool inClip = IsDir(dir, SonyClipDir), inSub = IsDir(dir, SonySubDir);
            if (!inClip && !inSub) return null;

            string camera = Path.GetDirectoryName(dir);
            string clipDir = inClip ? dir : FindSibling(dir, SonyClipDir);
            string subDir = inSub ? dir : FindSibling(dir, SonySubDir);
            if (clipDir == null || subDir == null) return null;

            string from, to;
            if (inClip)
            {
                from = Path.Combine(Path.GetFileName(clipDir), Path.GetFileName(file));
                to = Path.GetFileName(file);
            }
            else
            {
                string clipName = SonyClipName(file);
                if (clipName == null || !HasClip(clipDir, clipName)) return null;
                from = Path.Combine(Path.GetFileName(subDir), Path.GetFileName(file));
                to = Path.Combine(ProxyDirName, clipName + Path.GetExtension(file));
            }

            return new Job
            {
                Source = file,
                Target = Path.Combine(camera, to),
                Title = from,
                Display = Path.Combine(Path.GetFileName(camera), to),
                CleanupDir = dir,
                ForceMove = inClip,
                Journal = Path.Combine(camera, JournalName),
                JournalLine = from + "\t" + to
            };
        }

        static Job UndoJob(string file)
        {
            string proxyDir = Path.GetDirectoryName(file);
            if (!IsDir(proxyDir, ProxyDirName) || !IsVideo(file, VideoExts)) return null;

            string owner = Path.GetDirectoryName(proxyDir);
            if (File.Exists(Path.Combine(owner, JournalName))) return null; // откатывается по журналу

            string name = Path.GetFileNameWithoutExtension(file);
            string subDir = IsDir(owner, SonyClipDir) ? FindSibling(owner, SonySubDir) : null;

            if (subDir != null)
            {
                string target = name + SonyProxySuffix + Path.GetExtension(file);
                return new Job
                {
                    Source = file,
                    Target = Path.Combine(subDir, target),
                    Title = Path.Combine(Path.GetFileName(owner), ProxyDirName, Path.GetFileName(file)),
                    Display = Path.Combine(Path.GetFileName(subDir), target),
                    CleanupDir = proxyDir
                };
            }

            return new Job
            {
                Source = file,
                Target = Path.Combine(owner, name + ".LRF"),
                Title = Path.Combine(ProxyDirName, Path.GetFileName(file)),
                Display = name + ".LRF",
                CleanupDir = proxyDir
            };
        }

        // Откат «поднятия»: каждая строка журнала «откуда<TAB>куда» превращается в обратный перенос
        static List<Job> JournalUndoJobs(string journal)
        {
            var jobs = new List<Job>();
            string camera = Path.GetDirectoryName(journal);
            string[] lines;
            try { lines = File.ReadAllLines(journal, Encoding.UTF8); }
            catch (IOException) { return jobs; }
            catch (UnauthorizedAccessException) { return jobs; }

            foreach (var line in lines.Reverse())
            {
                var parts = line.Split('\t');
                if (parts.Length != 2) continue;
                string movedTo = Path.Combine(camera, parts[1]);
                string movedDir = Path.GetDirectoryName(movedTo);
                jobs.Add(new Job
                {
                    Source = movedTo,
                    Target = Path.Combine(camera, parts[0]),
                    Title = Path.Combine(Path.GetFileName(camera), parts[1]),
                    Display = parts[0],
                    CleanupDir = string.Equals(movedDir, camera, StringComparison.OrdinalIgnoreCase) ? null : movedDir,
                    Journal = journal,
                    JournalLine = line
                });
            }
            return jobs;
        }

        public static Outcome Execute(Job job, Mode mode, out string detail)
        {
            if (File.Exists(job.Target))
            {
                detail = "уже есть: " + job.Display;
                return Outcome.Skipped;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.Target));
                bool copy = mode == Mode.Copy && !job.ForceMove;
                if (copy) File.Copy(job.Source, job.Target);
                else File.Move(job.Source, job.Target);

                if (job.Journal != null)
                {
                    if (mode == Mode.Undo) RemoveJournalLine(job.Journal, job.JournalLine);
                    else if (!copy) AppendJournalLine(job.Journal, job.JournalLine);
                }
                if (job.CleanupDir != null && !copy) DeleteIfEmpty(job.CleanupDir);

                detail = "→ " + job.Display;
                return Outcome.Done;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return Outcome.Failed;
            }
        }

        static void AppendJournalLine(string journal, string line)
        {
            bool created = !File.Exists(journal);
            if (!created) File.SetAttributes(journal, FileAttributes.Normal);
            File.AppendAllText(journal, line + Environment.NewLine, new UTF8Encoding(false));
            File.SetAttributes(journal, FileAttributes.Hidden);
        }

        static void RemoveJournalLine(string journal, string line)
        {
            if (!File.Exists(journal)) return;
            var rest = File.ReadAllLines(journal, Encoding.UTF8).ToList();
            rest.Remove(line);
            File.SetAttributes(journal, FileAttributes.Normal);
            if (rest.Count == 0) File.Delete(journal);
            else
            {
                File.WriteAllLines(journal, rest, new UTF8Encoding(false));
                File.SetAttributes(journal, FileAttributes.Hidden);
            }
        }

        static string SonyClipName(string proxy)
        {
            var match = SonyProxyName.Match(Path.GetFileName(proxy));
            return match.Success ? match.Groups[1].Value : null;
        }

        static bool HasClip(string clipDir, string clipName)
        {
            return SonyClipExts.Any(ext => File.Exists(Path.Combine(clipDir, clipName + ext)));
        }

        // Расширение берём как у исходника (MP4/MOV), чтобы Resolve точно сматчил имя
        static string SourceExt(string dir, string name)
        {
            foreach (var ext in VideoExts)
            {
                string[] found = Directory.GetFiles(dir, name + ext);
                if (found.Length > 0 && HasExt(found[0], ext)) return Path.GetExtension(found[0]);
            }
            return ".MP4";
        }

        // Соседняя папка с таким именем (без учёта регистра), например SUB → CLIP
        static string FindSibling(string dir, string name)
        {
            string parent = Path.GetDirectoryName(dir);
            if (parent == null) return null;
            try
            {
                return Directory.EnumerateDirectories(parent)
                                .FirstOrDefault(d => IsDir(d, name));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static void DeleteIfEmpty(string dir)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static bool IsDir(string dir, string name)
        {
            return string.Equals(Path.GetFileName(dir), name, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsVideo(string f, string[] exts)
        {
            return exts.Any(ext => HasExt(f, ext));
        }

        static bool HasExt(string f, string ext)
        {
            return string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase);
        }
    }
}
