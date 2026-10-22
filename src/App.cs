// Точка входа и логика окна. Разметка — в MainWindow.xaml (встроен в exe как ресурс).
//
// Аргументы командной строки (так же работает перетаскивание файлов на exe / «Отправить»):
//   DjiProxyLinker.exe [--copy | --undo] [--run] <папки/файлы...>
//   --run                      сразу запустить обработку без нажатия кнопки
//   --screenshot <png> [empty] отрендерить окно в PNG (для README)

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: AssemblyTitle("DJI Proxy Linker")]
[assembly: AssemblyProduct("DJI Proxy Linker")]
[assembly: AssemblyDescription("Links DJI .LRF proxy files for DaVinci Resolve")]
[assembly: AssemblyCopyright("Apache License 2.0")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace DjiProxyLinker
{
    public sealed class LogEntry
    {
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Kind { get; set; }
    }

    static class Program
    {
        public const string Version = "1.0.0";
        public const string SponsorUrl = "https://t.me/adam_editing_page";
        public const string RepoUrl = "https://github.com/blemb666/dji-proxy-linker";

        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application();
            var paths = new List<string>();
            var mode = Mode.Move;
            bool autoRun = false;
            string screenshot = null;
            bool screenshotEmpty = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--copy": mode = Mode.Copy; break;
                    case "--undo": mode = Mode.Undo; break;
                    case "--run": autoRun = true; break;
                    case "--screenshot":
                        screenshot = i + 1 < args.Length ? args[++i] : "screenshot.png";
                        if (i + 1 < args.Length && args[i + 1] == "empty") { screenshotEmpty = true; i++; }
                        break;
                    default: paths.Add(args[i]); break;
                }
            }

            Window window;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
                window = (Window)XamlReader.Load(s);

            var controller = new MainWindowController(window, mode);
            if (screenshot != null)
            {
                controller.Screenshot(screenshot, screenshotEmpty);
                return;
            }

            if (paths.Count > 0)
                window.Loaded += async (s, e) =>
                {
                    await controller.AddPaths(paths);
                    if (autoRun) controller.Run();
                };

            app.Run(window);
        }
    }

    sealed class MainWindowController
    {
        const string GlyphOk = "\uE73E", GlyphSkip = "\uE738", GlyphError = "\uE7BA", GlyphInfo = "\uE946";

        public readonly Window Window;
        readonly ObservableCollection<LogEntry> log = new ObservableCollection<LogEntry>();
        readonly List<string> paths = new List<string>();
        ScanResult scan;
        int scanToken;
        bool busy, finished;
        int done, skipped, failed;

        readonly Border dropZone;
        readonly TextBlock dropTitle, dropHint, modeHint, status, countDone, countSkipped, countFailed, countFailedLabel;
        readonly RadioButton modeMove, modeCopy, modeUndo;
        readonly Button run, clear;
        readonly ProgressBar progress;
        readonly ItemsControl logList;
        readonly FrameworkElement logEmpty;

        public MainWindowController(Window window, Mode mode)
        {
            Window = window;
            dropZone = Find<Border>("DropZone");
            dropTitle = Find<TextBlock>("DropTitle");
            dropHint = Find<TextBlock>("DropHint");
            modeHint = Find<TextBlock>("ModeHint");
            status = Find<TextBlock>("StatusText");
            countDone = Find<TextBlock>("CountDone");
            countSkipped = Find<TextBlock>("CountSkipped");
            countFailed = Find<TextBlock>("CountFailed");
            countFailedLabel = Find<TextBlock>("CountFailedLabel");
            modeMove = Find<RadioButton>("ModeMove");
            modeCopy = Find<RadioButton>("ModeCopy");
            modeUndo = Find<RadioButton>("ModeUndo");
            run = Find<Button>("RunButton");
            clear = Find<Button>("ClearButton");
            progress = Find<ProgressBar>("Progress");
            logList = Find<ItemsControl>("LogList");
            logEmpty = Window.FindName("LogEmpty") as FrameworkElement;

            logList.ItemsSource = log;
            Find<TextBlock>("VersionText").Text = "v" + Program.Version;

            Find<Button>("MinimizeButton").Click += (s, e) => Window.WindowState = WindowState.Minimized;
            Find<Button>("CloseButton").Click += (s, e) => Window.Close();
            Find<Button>("SponsorLink").Click += (s, e) => OpenUrl(Program.SponsorUrl);
            Find<Button>("GithubLink").Click += (s, e) => OpenUrl(Program.RepoUrl);
            run.Click += (s, e) => Run();
            clear.Click += (s, e) => Reset();

            Window.AllowDrop = true;
            Window.DragEnter += OnDragOver;
            Window.DragOver += OnDragOver;
            Window.DragLeave += OnDragLeave;
            Window.Drop += OnDrop;
            dropZone.MouseLeftButtonUp += (s, e) => PickFolders();
            Window.KeyDown += OnKeyDown;

            (mode == Mode.Undo ? modeUndo : mode == Mode.Copy ? modeCopy : modeMove).IsChecked = true;
            foreach (var rb in new[] { modeMove, modeCopy, modeUndo })
                rb.Checked += (s, e) => OnModeChanged();

            UpdateModeText();
            Reset();
        }

        T Find<T>(string name) where T : class
        {
            var element = Window.FindName(name) as T;
            if (element == null)
                throw new InvalidOperationException("MainWindow.xaml: нет элемента " + name + " (" + typeof(T).Name + ")");
            return element;
        }

        Mode CurrentMode
        {
            get { return modeUndo.IsChecked == true ? Mode.Undo : modeCopy.IsChecked == true ? Mode.Copy : Mode.Move; }
        }

        // ---------- Ввод: перетаскивание, диалог, клавиши ----------

        void OnDragOver(object sender, DragEventArgs e)
        {
            bool ok = !busy && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = ok ? DragDropEffects.Link : DragDropEffects.None;
            if (ok) dropZone.Tag = "Over";
            e.Handled = true;
        }

        void OnDragLeave(object sender, DragEventArgs e)
        {
            // DragLeave приходит и при переходе между дочерними элементами — сбрасываем только при выходе из окна
            var p = e.GetPosition(Window);
            if (p.X <= 0 || p.Y <= 0 || p.X >= Window.ActualWidth || p.Y >= Window.ActualHeight)
                RestoreDropTag();
        }

        async void OnDrop(object sender, DragEventArgs e)
        {
            RestoreDropTag();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (busy || files == null) return;
            await AddPaths(files);
        }

        async void PickFolders()
        {
            if (busy) return;
            var picked = FolderPicker.Show(Window, "Выберите папки с видео DJI");
            if (picked.Count > 0) await AddPaths(picked);
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) PickFolders();
            else if (e.Key == Key.Enter && run.IsEnabled) Run();
            else if (e.Key == Key.Escape && !busy) Reset();
        }

        void RestoreDropTag()
        {
            dropZone.Tag = paths.Count > 0 ? "Filled" : null;
        }

        // ---------- Состояния ----------

        public Task AddPaths(IEnumerable<string> newPaths)
        {
            if (finished) Reset();
            foreach (var p in newPaths)
            {
                string full;
                try { full = Path.GetFullPath(p.Trim('"')); }
                catch (Exception) { continue; }
                if (!paths.Contains(full, StringComparer.OrdinalIgnoreCase)) paths.Add(full);
            }
            return Rescan();
        }

        void Reset()
        {
            if (busy) return;
            scanToken++;
            paths.Clear();
            scan = null;
            finished = false;
            log.Clear();
            SetCounters(0, 0, 0);
            progress.Value = 0;
            status.Text = "";
            dropZone.Tag = null;
            dropTitle.Text = "Перетащите папки с видео сюда";
            dropHint.Text = "или нажмите, чтобы выбрать  ·  Ctrl+O";
            dropHint.ToolTip = null;
            run.IsEnabled = false;
            clear.IsEnabled = false;
            UpdateLogEmpty();
        }

        async void OnModeChanged()
        {
            UpdateModeText();
            finished = false;
            if (paths.Count > 0) await Rescan();
        }

        void UpdateModeText()
        {
            switch (CurrentMode)
            {
                case Mode.Copy:
                    modeHint.Text = "LRF остаются на месте, в папке Proxy создаются копии .MP4";
                    run.Content = "Создать прокси";
                    break;
                case Mode.Undo:
                    modeHint.Text = "Файлы из папок Proxy возвращаются обратно в .LRF";
                    run.Content = "Откатить";
                    break;
                default:
                    modeHint.Text = "LRF переносятся в папку Proxy как .MP4 — мгновенно и без лишнего места";
                    run.Content = "Создать прокси";
                    break;
            }
        }

        async Task Rescan()
        {
            int token = ++scanToken;
            var mode = CurrentMode;
            var snapshot = paths.ToArray();

            dropZone.Tag = "Filled";
            dropTitle.Text = "Поиск файлов…";
            dropHint.Text = DescribePaths(snapshot);
            dropHint.ToolTip = string.Join(Environment.NewLine, snapshot);
            run.IsEnabled = false;
            clear.IsEnabled = true;

            var result = await Task.Run(() => Linker.Scan(snapshot, mode));
            if (token != scanToken) return;

            scan = result;
            log.Clear();
            SetCounters(0, 0, 0);
            progress.Value = 0;
            status.Text = "";
            foreach (var err in result.Errors) AddLog(GlyphError, err, "", "Error");

            int n = result.Jobs.Count, f = result.FolderCount;
            if (n == 0)
                dropTitle.Text = mode == Mode.Undo ? "Папки Proxy не найдены" : "Файлы LRF не найдены";
            else
                dropTitle.Text = string.Format("Найдено {0} {1} в {2} {3}", n,
                    mode == Mode.Undo ? "прокси" : Plural(n, "файл", "файла", "файлов") + " LRF",
                    f, Plural(f, "папке", "папках", "папках"));

            run.IsEnabled = n > 0;
            UpdateLogEmpty();
        }

        public async void Run()
        {
            if (busy || scan == null || scan.Jobs.Count == 0) return;

            var jobs = scan.Jobs;
            var mode = CurrentMode;
            busy = true;
            SetInputsEnabled(false);
            log.Clear();
            SetCounters(0, 0, 0);
            progress.Maximum = jobs.Count;
            progress.Value = 0;

            var timer = Stopwatch.StartNew();
            IProgress<Tuple<Job, Outcome, string>> reporter = new Progress<Tuple<Job, Outcome, string>>(r =>
            {
                string title = Linker.Title(r.Item1, mode);
                switch (r.Item2)
                {
                    case Outcome.Done: done++; AddLog(GlyphOk, title, r.Item3, "Ok"); break;
                    case Outcome.Skipped: skipped++; AddLog(GlyphSkip, title, r.Item3, "Skip"); break;
                    default: failed++; AddLog(GlyphError, title, r.Item3, "Error"); break;
                }
                SetCounters(done, skipped, failed);
                progress.Value++;
                status.Text = string.Format("{0} из {1}", progress.Value, jobs.Count);
            });

            await Task.Run(() =>
            {
                foreach (var job in jobs)
                {
                    string detail;
                    var outcome = Linker.Execute(job, mode, out detail);
                    reporter.Report(Tuple.Create(job, outcome, detail));
                }
            });
            timer.Stop();

            busy = false;
            finished = true;
            scan = null;
            SetInputsEnabled(true);
            run.IsEnabled = false;

            status.Text = string.Format("Готово за {0:0.0} с", timer.Elapsed.TotalSeconds);
            if (failed > 0)
                dropTitle.Text = string.Format("Готово, но с ошибками: {0}", failed);
            else if (mode == Mode.Undo)
                dropTitle.Text = string.Format("Откатено: {0}", done);
            else
                dropTitle.Text = string.Format("Готово: {0} прокси", done);

            if (mode != Mode.Undo && done + skipped > 0)
            {
                dropHint.Text = "Импортируйте папку в DaVinci Resolve — прокси подтянутся автоматически";
                AddLog(GlyphInfo, "Что дальше", "Если клипы уже в проекте: Media Pool → ПКМ → Link Proxy Media", "Info");
            }
        }

        // ---------- Вспомогательное ----------

        void SetInputsEnabled(bool enabled)
        {
            run.IsEnabled = enabled;
            clear.IsEnabled = enabled;
            modeMove.IsEnabled = modeCopy.IsEnabled = modeUndo.IsEnabled = enabled;
            dropZone.IsEnabled = enabled;
        }

        void SetCounters(int d, int s, int f)
        {
            done = d; skipped = s; failed = f;
            countDone.Text = d.ToString();
            countSkipped.Text = s.ToString();
            countFailed.Text = f.ToString();
            countFailedLabel.Text = Plural(f, "ошибка", "ошибки", "ошибок");
        }

        void AddLog(string glyph, string title, string detail, string kind)
        {
            var entry = new LogEntry { Glyph = glyph, Title = title, Detail = detail, Kind = kind };
            log.Add(entry);
            UpdateLogEmpty();

            var listBox = logList as ListBox;
            if (listBox != null) listBox.ScrollIntoView(entry);
            else
            {
                var viewer = FindScrollViewer(logList);
                if (viewer != null) viewer.ScrollToEnd();
            }
        }

        void UpdateLogEmpty()
        {
            if (logEmpty != null) logEmpty.Visibility = log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        static ScrollViewer FindScrollViewer(DependencyObject element)
        {
            for (var p = VisualTreeHelper.GetParent(element); p != null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer) return (ScrollViewer)p;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(element);
            while (queue.Count > 0)
            {
                var d = queue.Dequeue();
                if (d is ScrollViewer) return (ScrollViewer)d;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) queue.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
            return null;
        }

        static string DescribePaths(string[] list)
        {
            if (list.Length == 1) return list[0];
            var names = list.Select(p =>
            {
                string name = Path.GetFileName(p.TrimEnd('\\'));
                return name.Length > 0 ? name : p;
            });
            return string.Join("  ·  ", names.Take(4)) + (list.Length > 4 ? "  ·  …" : "");
        }

        static string Plural(int n, string one, string few, string many)
        {
            n = Math.Abs(n) % 100;
            int last = n % 10;
            if (n > 10 && n < 20) return many;
            if (last == 1) return one;
            if (last > 1 && last < 5) return few;
            return many;
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        // ---------- Скриншот для README ----------

        public void Screenshot(string file, bool empty)
        {
            Window.WindowStartupLocation = WindowStartupLocation.Manual;
            Window.Left = -20000;
            Window.ShowActivated = false;
            Window.ShowInTaskbar = false;
            Window.Show();

            if (!empty)
            {
                paths.Add(@"D:\Footage\2026-10-22 Pocket 3");
                dropZone.Tag = "Filled";
                dropTitle.Text = "Готово: 24 прокси";
                dropHint.Text = "Импортируйте папку в DaVinci Resolve — прокси подтянутся автоматически";
                clear.IsEnabled = true;
                string[] names =
                {
                    "DJI_20261022121502_0041_D", "DJI_20261022121844_0042_D", "DJI_20261022122310_0043_D",
                    "DJI_20261022123007_0044_D", "DJI_20261022123552_0045_D"
                };
                foreach (var n in names)
                    AddLog(GlyphOk, n + ".LRF", "→ Proxy\\" + n + ".MP4", "Ok");
                AddLog(GlyphSkip, "DJI_20261022124130_0046_D.LRF", "уже есть: Proxy\\DJI_20261022124130_0046_D.MP4", "Skip");
                AddLog(GlyphInfo, "Что дальше", "Если клипы уже в проекте: Media Pool → ПКМ → Link Proxy Media", "Info");
                SetCounters(23, 1, 0);
                progress.Maximum = 24;
                progress.Value = 24;
                status.Text = "Готово за 0.3 с";
            }

            Window.UpdateLayout();
            Window.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ApplicationIdle);

            const double scale = 2;
            var rtb = new RenderTargetBitmap((int)(Window.ActualWidth * scale), (int)(Window.ActualHeight * scale),
                                             96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(Window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(file)) encoder.Save(fs);
            Window.Close();
        }
    }
}
