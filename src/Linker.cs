// Ядро: поиск LRF и перенос их в Proxy\*.MP4 (и обратно). Ничего не знает про UI.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DjiProxyLinker
{
    public enum Mode { Move, Copy, Undo }

    public enum Outcome { Done, Skipped, Failed }

    public sealed class Job
    {
        public string Source;
        public string Target;
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
        static readonly string[] VideoExts = { ".MP4", ".MOV" };
        const FileAttributes HiddenSystem = FileAttributes.Hidden | FileAttributes.System;

        public static ScanResult Scan(IEnumerable<string> paths, Mode mode)
        {
            var result = new ScanResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in paths)
            {
                if (Directory.Exists(p)) Walk(Path.GetFullPath(p), mode, result, seen);
                else if (File.Exists(p)) AddFile(Path.GetFullPath(p), mode, true, result, seen);
                else result.Errors.Add("Не найдено: " + p);
            }

            result.Jobs.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Source, b.Source));
            return result;
        }

        // Обход без рекурсии и без падения на недоступных папках (корень SD-карты, System Volume Information)
        static void Walk(string root, Mode mode, ScanResult result, HashSet<string> seen)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                        AddFile(f, mode, false, result, seen);
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                        if ((File.GetAttributes(sub) & HiddenSystem) != HiddenSystem) stack.Push(sub);
                }
                catch (UnauthorizedAccessException) { result.Errors.Add("Нет доступа: " + dir); }
                catch (IOException ex) { result.Errors.Add(dir + ": " + ex.Message); }
            }
        }

        // explicitFile: файл перетащили руками — тогда для исходника .MP4 ищем рядом его .LRF
        static void AddFile(string file, Mode mode, bool explicitFile, ScanResult result, HashSet<string> seen)
        {
            string dir = Path.GetDirectoryName(file);
            bool inProxy = IsProxyDir(dir);
            Job job = null;

            if (mode == Mode.Undo)
            {
                if (inProxy && IsVideo(file))
                    job = new Job
                    {
                        Source = file,
                        Target = Path.Combine(Path.GetDirectoryName(dir), Path.GetFileNameWithoutExtension(file) + ".LRF")
                    };
            }
            else if (!inProxy)
            {
                string lrf = null;
                if (HasExt(file, ".LRF")) lrf = file;
                else if (explicitFile && IsVideo(file))
                {
                    string candidate = Path.ChangeExtension(file, ".LRF");
                    if (File.Exists(candidate)) lrf = candidate;
                }

                if (lrf != null)
                {
                    string name = Path.GetFileNameWithoutExtension(lrf);
                    job = new Job { Source = lrf, Target = Path.Combine(dir, ProxyDirName, name + SourceExt(dir, name)) };
                }
            }

            if (job != null && seen.Add(job.Source)) result.Jobs.Add(job);
        }

        public static Outcome Execute(Job job, Mode mode, out string detail)
        {
            if (File.Exists(job.Target))
            {
                detail = "уже есть: " + Describe(job, mode);
                return Outcome.Skipped;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.Target));
                if (mode == Mode.Copy) File.Copy(job.Source, job.Target);
                else File.Move(job.Source, job.Target);

                if (mode == Mode.Undo) DeleteIfEmpty(Path.GetDirectoryName(job.Source));

                detail = "→ " + Describe(job, mode);
                return Outcome.Done;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return Outcome.Failed;
            }
        }

        // Как показать файл в журнале: "DJI_..._D.LRF" или "Proxy\DJI_..._D.MP4"
        public static string Title(Job job, Mode mode)
        {
            return mode == Mode.Undo
                ? Path.Combine(ProxyDirName, Path.GetFileName(job.Source))
                : Path.GetFileName(job.Source);
        }

        static string Describe(Job job, Mode mode)
        {
            return mode == Mode.Undo
                ? Path.GetFileName(job.Target)
                : Path.Combine(ProxyDirName, Path.GetFileName(job.Target));
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

        static void DeleteIfEmpty(string dir)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static bool IsProxyDir(string dir)
        {
            return string.Equals(Path.GetFileName(dir), ProxyDirName, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsVideo(string f)
        {
            return VideoExts.Any(ext => HasExt(f, ext));
        }

        static bool HasExt(string f, string ext)
        {
            return string.Equals(Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase);
        }
    }
}
