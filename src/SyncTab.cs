// Вкладка «С сервера»: копирует папки Proxy с сервера на компьютер (ProxySync)
// и обновляет скрипт для DaVinci (ResolveScript). Разметка — PageSync в MainWindow.xaml.

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DjiProxyLinker
{
    sealed class SyncTab
    {
        readonly Window window;
        readonly Settings settings;
        readonly ObservableCollection<LogEntry> log = new ObservableCollection<LogEntry>();
        readonly TextBox source, dest;
        readonly TextBlock summary, status, countDone, countSkipped, countFailed, countFailedLabel, resolveHint;
        readonly Button run, sourceBrowse, destBrowse, resolveButton;
        readonly ProgressBar progress;
        readonly ItemsControl logList;
        readonly FrameworkElement logEmpty;
        volatile bool cancel;

        public bool Busy { get; private set; }

        public SyncTab(Window window, Settings settings)
        {
            this.window = window;
            this.settings = settings;
            source = Find<TextBox>("SyncSource");
            dest = Find<TextBox>("SyncDest");
            summary = Find<TextBlock>("SyncSummary");
            status = Find<TextBlock>("SyncStatus");
            countDone = Find<TextBlock>("SyncCountDone");
            countSkipped = Find<TextBlock>("SyncCountSkipped");
            countFailed = Find<TextBlock>("SyncCountFailed");
            countFailedLabel = Find<TextBlock>("SyncCountFailedLabel");
            resolveHint = Find<TextBlock>("ResolveHint");
            run = Find<Button>("SyncRun");
            sourceBrowse = Find<Button>("SyncSourceBrowse");
            destBrowse = Find<Button>("SyncDestBrowse");
            resolveButton = Find<Button>("ResolveScriptButton");
            progress = Find<ProgressBar>("SyncProgress");
            logList = Find<ItemsControl>("SyncLog");
            logEmpty = window.FindName("SyncLogEmpty") as FrameworkElement;

            logList.ItemsSource = log;
            source.Text = settings.SyncSource;
            dest.Text = settings.SyncDest;

            sourceBrowse.Click += (s, e) =>
            {
                string picked = Pick("Папка на сервере, из которой забрать прокси");
                if (picked != null) SetSource(picked);
            };
            destBrowse.Click += (s, e) =>
            {
                string picked = Pick("Папка на компьютере, куда сложить прокси");
                if (picked != null) dest.Text = picked;
            };
            run.Click += (s, e) =>
            {
                if (Busy) cancel = true;
                else Start();
            };
            resolveButton.Click += (s, e) => InstallScript();

            Reset();
        }

        T Find<T>(string name) where T : class
        {
            return MainWindowController.FindIn<T>(window, name);
        }

        string Pick(string title)
        {
            var picked = FolderPicker.Show(window, title);
            return picked.Count > 0 ? picked[0] : null;
        }

        // Папку на компьютере предлагаем сами: D:\Proxy\<имя папки на сервере> (или Видео\Proxy\…)
        public void SetSource(string path)
        {
            source.Text = path.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(dest.Text)) dest.Text = DefaultDest(source.Text);
        }

        static string DefaultDest(string sourcePath)
        {
            var d = new DriveInfo("D");
            string root = d.IsReady && d.DriveType == DriveType.Fixed
                ? @"D:\Proxy"
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Proxy");
            string leaf = Path.GetFileName(sourcePath.TrimEnd('\\'));
            return string.IsNullOrEmpty(leaf) ? root : Path.Combine(root, leaf);
        }

        void Reset()
        {
            log.Clear();
            SetCounters(0, 0, 0);
            progress.Value = 0;
            status.Text = "";
            summary.Text = "Выберите папку на сервере — программа найдёт в ней все папки Proxy";
            UpdateResolveHint();
            UpdateLogEmpty();
        }

        async void Start()
        {
            string src = source.Text.Trim().Trim('"').TrimEnd('\\');
            string dst = dest.Text.Trim().Trim('"').TrimEnd('\\');
            if (src.Length == 0 || dst.Length == 0)
            {
                summary.Text = "Укажите обе папки: на сервере и на компьютере";
                return;
            }
            if (IsInside(dst, src))
            {
                summary.Text = "Папка на компьютере не должна быть внутри папки на сервере";
                return;
            }

            settings.SyncSource = src;
            settings.SyncDest = dst;
            settings.Save();

            SetBusy(true);
            log.Clear();
            SetCounters(0, 0, 0);
            progress.Value = 0;
            status.Text = "";
            summary.Text = "Поиск папок Proxy…";
            UpdateLogEmpty();

            bool exists = await Task.Run(() => Directory.Exists(src));
            if (!exists)
            {
                summary.Text = "Папка на сервере недоступна: " + src;
                SetBusy(false);
                return;
            }

            var plan = await Task.Run(() => ProxySync.Plan(src, dst));
            foreach (var err in plan.Errors) AddLog(MainWindowController.GlyphError, err, "", "Error");
            summary.Text = string.Format("Найдено {0} прокси · {1} · новых {2} ({3})",
                plan.Total, ProxySync.FormatSize(plan.TotalBytes), plan.ToCopy.Count, ProxySync.FormatSize(plan.CopyBytes));

            int copied = 0, failed = 0;
            long total = plan.CopyBytes, doneBytes = 0;
            progress.Maximum = Math.Max(total, 1);
            SetCounters(0, plan.UpToDate, 0);
            var timer = Stopwatch.StartNew();
            cancel = false;

            await Task.Run(() =>
            {
                var lastUi = Stopwatch.StartNew();
                foreach (var item in plan.ToCopy)
                {
                    if (cancel) break;
                    string error = null;
                    try
                    {
                        ProxySync.Copy(item, n =>
                        {
                            doneBytes += n;
                            if (lastUi.ElapsedMilliseconds < 200) return;
                            lastUi.Restart();
                            long snapshot = doneBytes;
                            Ui(() => ShowProgress(snapshot, total, copied, plan.ToCopy.Count, timer));
                        }, () => cancel);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { error = ex.Message; }

                    if (error == null) copied++; else failed++;
                    int c = copied, f = failed;
                    long snap = doneBytes;
                    Ui(() =>
                    {
                        if (error == null) AddLog(MainWindowController.GlyphOk, item.Title, ProxySync.FormatSize(item.Length), "Ok");
                        else AddLog(MainWindowController.GlyphError, item.Title, error, "Error");
                        SetCounters(c, plan.UpToDate, f);
                        ShowProgress(snap, total, c, plan.ToCopy.Count, timer);
                    });
                }
            });
            timer.Stop();

            if (cancel)
                status.Text = "Остановлено — при следующем запуске докачается остальное";
            else
            {
                progress.Value = progress.Maximum;
                status.Text = plan.ToCopy.Count == 0
                    ? "Всё уже на компьютере"
                    : string.Format("Готово за {0}", FormatTime(timer.Elapsed));
            }

            // Пару папок запоминаем даже после остановки — скрипт DaVinci свяжет то, что уже скачано
            settings.AddMapping(src, dst);
            settings.Save();
            InstallScript();
            SetBusy(false);
        }

        void InstallScript()
        {
            try
            {
                ResolveScript.Install(settings.Mappings);
                AddLog(MainWindowController.GlyphInfo, "Скрипт для DaVinci обновлён",
                       "Workspace → Scripts → " + ResolveScript.MenuName + " — привяжет эти прокси к клипам с сервера", "Info");
            }
            catch (Exception ex)
            {
                AddLog(MainWindowController.GlyphError, "Не удалось установить скрипт для DaVinci", ex.Message, "Error");
            }
            UpdateResolveHint();
        }

        void UpdateResolveHint()
        {
            resolveHint.Text = File.Exists(ResolveScript.ScriptPath)
                ? "DaVinci: Workspace → Scripts → " + ResolveScript.MenuName
                : "Скрипт появится в DaVinci после первой синхронизации (перезапустите DaVinci)";
        }

        void ShowProgress(long doneBytes, long total, int copied, int count, Stopwatch timer)
        {
            progress.Value = doneBytes;
            double seconds = timer.Elapsed.TotalSeconds;
            double speed = seconds > 0.5 ? doneBytes / seconds : 0;
            string eta = speed > 0 ? " · осталось " + FormatTime(TimeSpan.FromSeconds((total - doneBytes) / speed)) : "";
            status.Text = string.Format("{0} из {1} · {2}/с{3}", copied, count, ProxySync.FormatSize((long)speed), eta);
        }

        void SetBusy(bool busy)
        {
            Busy = busy;
            run.Content = busy ? "Остановить" : "Синхронизировать";
            source.IsEnabled = dest.IsEnabled = sourceBrowse.IsEnabled = destBrowse.IsEnabled = resolveButton.IsEnabled = !busy;
            var tabLink = window.FindName("TabLink") as UIElement;
            if (tabLink != null) tabLink.IsEnabled = !busy;
        }

        void SetCounters(int copied, int upToDate, int failed)
        {
            countDone.Text = copied.ToString();
            countSkipped.Text = upToDate.ToString();
            countFailed.Text = failed.ToString();
            countFailedLabel.Text = MainWindowController.Plural(failed, "ошибка", "ошибки", "ошибок");
        }

        void AddLog(string glyph, string title, string detail, string kind)
        {
            log.Add(new LogEntry { Glyph = glyph, Title = title, Detail = detail, Kind = kind });
            UpdateLogEmpty();
            var viewer = MainWindowController.FindScrollViewer(logList);
            if (viewer != null) viewer.ScrollToEnd();
        }

        void UpdateLogEmpty()
        {
            if (logEmpty != null) logEmpty.Visibility = log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Ui(Action action)
        {
            window.Dispatcher.BeginInvoke(action);
        }

        static bool IsInside(string path, string root)
        {
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   (path.Length == root.Length || path[root.Length] == '\\');
        }

        static string FormatTime(TimeSpan t)
        {
            return t.TotalHours >= 1 ? string.Format("{0}:{1:mm\\:ss}", (int)t.TotalHours, t)
                                     : string.Format("{0}:{1:ss}", (int)t.TotalMinutes, t);
        }

        // ---------- Демо-состояние для скриншота ----------

        public void FillDemo()
        {
            source.Text = @"\\192.168.168.30\public\FastMotion\ZERO_U6";
            dest.Text = @"D:\Proxy\ZERO_U6";
            summary.Text = "Найдено 86 прокси · 3,4 ГБ · новых 34 (1,3 ГБ)";
            foreach (var cam in new[] { "FX30", "Pocket 3" })
                for (int i = 1; i <= 2; i++)
                    AddLog(MainWindowController.GlyphOk,
                           cam == "FX30" ? string.Format(@"1 смена\FX30\Proxy\C000{0}.MP4", i)
                                         : string.Format(@"1 смена\Pocket 3\Proxy\DJI_2026102712{0}502_000{0}_D.MP4", i),
                           (38 + i * 7) + " МБ", "Ok");
            AddLog(MainWindowController.GlyphInfo, "Скрипт для DaVinci обновлён",
                   "Workspace → Scripts → " + ResolveScript.MenuName + " — привяжет эти прокси к клипам с сервера", "Info");
            SetCounters(34, 52, 0);
            progress.Maximum = 100;
            progress.Value = 100;
            status.Text = "Готово за 0:41";
            resolveHint.Text = "DaVinci: Workspace → Scripts → " + ResolveScript.MenuName;
        }
    }
}
