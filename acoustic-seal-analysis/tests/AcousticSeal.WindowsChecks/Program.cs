using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;
using AcousticSeal.Desktop;
using AcousticSeal.DesktopLogic;

internal static class Program
{
    private static string root = "";
    private static readonly List<object> checks = [];
    private static int failures;
    private static MainWindow window = null!;
    private static Workspace vm = null!;
    private static TestDialogs dialogs = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        root = Path.GetFullPath(args.FirstOrDefault() ?? "docs/screenshots");
        Directory.CreateDirectory(root);
        var app = new App(); app.InitializeComponent(); app.StartMainWindow = false;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        dialogs = new TestDialogs(root);
        window = new MainWindow(dialogs);
        app.MainWindow = window;
        vm = (Workspace)window.DataContext;
        var errors = new StringWriter();
        var listener = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Run();
                listener.Flush();
                Record("No WPF binding errors", errors.ToString().Length == 0, errors.ToString());
            }
            catch (Exception error) { Record("Windows execution completed", false, error.ToString()); }
            finally
            {
                File.WriteAllText(Path.Combine(root, "binding-errors.log"), errors.ToString());
                File.WriteAllText(Path.Combine(root, "windows-ui-evidence.json"), JsonSerializer.Serialize(new
                {
                    executedUtc = DateTimeOffset.UtcNow,
                    sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    platform = Environment.OSVersion.ToString(),
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    rendering = "Actual production WPF MainWindow shown on Windows. PNG captures of live native client visual using RenderTargetBitmap, not browser images or mock-ups.",
                    dialogBoundary = "Automated window integration uses deterministic IUserDialogs paths and decisions; actual native file dialog mouse/keyboard operation is not tested.",
                    failed = failures, checks
                }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        };
        window.Show(); app.Run();
        return failures == 0 ? 0 : 1;
    }
    private static void Record(string name, bool passed, string detail = "")
    {
        checks.Add(new { name, passed, detail });
        Console.WriteLine((passed ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " " + detail : ""));
        if (!passed) failures++;
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T value) yield return value;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            foreach (var descendant in Visuals<T>(VisualTreeHelper.GetChild(node, index))) yield return descendant;
    }
    private static async Task Settle()
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(120);
    }
    private static async Task Click(string label)
    {
        var button = Visuals<Button>(window).First(item => item.IsVisible && Equals(item.Content, label));
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await Settle();
    }
    private static async Task Navigate(string key)
    {
        var list = Visuals<ListBox>(window).First(item => ReferenceEquals(item.ItemsSource, vm.Modules));
        list.SelectedItem = vm.Modules.Single(item => item.Key == key);
        await Settle();
        if (vm.SelectedModule?.Key != key) throw new Exception("Native navigation binding failed: " + key);
    }
    private static void Field(string key, string value)
    {
        var field = vm.Editor!.Fields.Single(item => item.Key == key);
        if (field.IsChoice)
        {
            var combo = Visuals<ComboBox>(window).Single(item => ReferenceEquals(item.DataContext, field));
            combo.SelectedValue = value;
        }
        else
        {
            var box = Visuals<TextBox>(window).Single(item => ReferenceEquals(item.DataContext, field));
            box.Text = value;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
        if (field.Value != value) throw new Exception("Native editor binding failed: " + key);
    }
    private static async Task AddBand(string frequency, string level)
    {
        await Click("增加频带行");
        var grid = Visuals<DataGrid>(window).First(item => ReferenceEquals(item.ItemsSource, vm.Editor!.Bands));
        var row = vm.Editor!.Bands.Last();
        for (var column = 0; column < 2; column++)
        {
            grid.SelectedItem = row;
            grid.ScrollIntoView(row, grid.Columns[column]);
            grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[column]);
            if (!grid.BeginEdit()) throw new Exception("DataGrid BeginEdit failed");
            await Settle();
            var cell = grid.Columns[column].GetCellContent(row);
            var box = cell as TextBox ?? Visuals<TextBox>(cell!).First();
            box.Text = column == 0 ? frequency : level;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        Record("Native DataGrid edited " + frequency, row.Frequency == frequency && row.Level == level);
    }
    private static async Task Capture(string name)
    {
        await Settle();
        var visual = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var canvas = new DrawingVisual();
        using (var drawing = canvas.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            drawing.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
        }
        image.Render(canvas);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        Record(name + " native rendered pixels", pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 20);
        Record(name + " opaque background", pixels.Where((_, i) => i % 4 == 3).All(item => item == 255));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(root, name + ".png")); encoder.Save(stream);
    }
    private static async Task Run()
    {
        await Capture("01-empty");
        Record("Ordinary startup empty", vm.Results.Count == 0 && vm.SourceSummary == "尚无试验数据");
        Field("Name", "手工模拟封堵案例"); Field("Owner", "Windows验证人员");
        await Click("应用编辑"); await Click("保存项目");
        Record("Native blank project saved", File.Exists(dialogs.ProjectPath));
        await Navigate("location"); await Click("新增");
        Field("Name", "车门密封条"); Field("Material", "模拟胶带"); Field("RecoveryProcedure", "解除胶带后核对恢复");
        await Click("应用编辑"); await Capture("02-location");
        await Navigate("condition"); await Click("新增");
        foreach (var (key, value) in new[] { ("Name", "模拟工况"), ("Vehicle", "SIM-01"), ("Microphone", "驾驶员耳旁"), ("Excitation", "模拟声源"), ("Surface", "模拟台架"), ("Gear", "N"), ("Instrument", "SIM-MIC"), ("Calibration", "模拟链路"), ("Speed", "0"), ("Rpm", "0"), ("Temperature", "20"), ("Duration", "10") }) Field(key, value);
        await Click("应用编辑"); await Capture("03-condition");
        await Navigate("trial"); await Click("新增");
        Field("Name", "基线"); Field("SourceNote", "人工填写的独立Windows验证数值");
        await AddBand("125", "70"); await Click("应用编辑");
        var baseline = vm.Editor!.Identity;
        await Capture("04-baseline"); await Click("新增");
        Field("Name", "单项封堵"); Field("Kind", "Single"); Field("SourceNote", "人工填写的独立Windows验证数值");
        var choice = vm.Editor!.Locations[0];
        Visuals<CheckBox>(window).Single(item => ReferenceEquals(item.DataContext, choice)).IsChecked = true;
        await AddBand("125", "67"); await Click("应用编辑");
        var target = vm.Editor!.Identity;
        await Capture("05-single"); await Navigate("pair"); await Click("新增");
        Field("BaselineId", baseline); Field("TargetId", target); await Click("应用编辑");
        await Capture("06-pair"); await Navigate("result");
        Record("Native paired result is 3dB", vm.ResultBands.Single().Difference == 3);
        await Capture("07-manual-result");
        vm.SaveCommand.Execute(null); vm.ResultsCommand.Execute(null); vm.ReportCommand.Execute(null);
        Record("Native file workflow saved and exported", File.Exists(dialogs.ProjectPath) && File.ReadAllText(Path.Combine(root, "native-results.csv")).Contains("3.0.0") && File.ReadAllText(Path.Combine(root, "native-report.html")).Contains("人工填写的独立Windows验证数值"));
        vm.NewCommand.Execute(null); dialogs.Opens.Enqueue(dialogs.ProjectPath); vm.OpenCommand.Execute(null);
        await Navigate("result"); Record("Native save reopened same 3dB result", vm.ResultBands.Single().Difference == 3);
        vm.DemoCommand.Execute(null); await Settle();
        await Capture("08-example-project");
        await Navigate("trial");
        var list = Visuals<ListBox>(window).First(item => ReferenceEquals(item.ItemsSource, vm.Items));
        var combination = vm.Items.First(item => item.Subtitle.Contains("组合封堵")); list.SelectedItem = combination;
        await Capture("09-combination");
        await Navigate("import");
        var csv = Path.Combine(root, "native-import.csv"); File.WriteAllText(csv, $"trial_id,frequency_hz,level_db\\n{Demonstration.StableId(30)},125,70".Replace("\\n", "\n"), new UTF8Encoding(false));
        dialogs.Opens.Enqueue(csv); await Click("读取CSV文件"); await Click("预检当前内容");
        Record("Native CSV preview populated", vm.ImportStatus.Contains("可以应用"));
        await Capture("10-import-preview"); await Click("整体应用导入");
        await Navigate("result"); await Capture("11-example-difference");
        var original = Visuals<CheckBox>(window).Single(item => item.Name == "OriginalCheck"); original.IsChecked = true;
        await Capture("12-original-spectrum");
        vm.Minimum = "125"; vm.Maximum = "125"; vm.FilterCommand.Execute(null);
        Record("Native displayed range filtered", vm.ResultBands.Count == 1);
        vm.Minimum = ""; vm.Maximum = ""; vm.FilterCommand.Execute(null);
        await Navigate("quality"); await Capture("13-quality");
        await Navigate("settings"); await Capture("14-settings");
        await Navigate("audit"); vm.Reviewer = "Windows自动化验证"; vm.ReviewComment = "模拟输入与结果核对"; await Click("记录复核");
        await Capture("15-review"); Record("Native review recorded", vm.Reviews.Count > 0);
        window.Width = 1180; window.Height = 720; await Navigate("result"); await Capture("16-minimum-window");
        Record("Minimum native result plot has usable dimensions", Visuals<SpectrumPlot>(window).Single().ActualWidth > 350);
        window.Width = 1480; window.Height = 960;
        await Navigate("project"); Field("Name", ""); await Click("应用编辑");
        Record("Native invalid input rejected", dialogs.Errors.Count == 1 && vm.Editor!.Dirty);
        await Capture("17-invalid-draft"); Field("Name", "模拟案例（已核对）"); await Click("应用编辑");
        dialogs.Errors.Clear();
        await Navigate("condition"); Field("Vehicle", ""); await Click("应用编辑");
        await Navigate("result");
        Record("Native missing condition blocks comparison", vm.SelectedResult!.BlockingReasons.Count > 0 && vm.ResultBands.All(item => item.Difference is null));
        await Capture("18-blocked-comparison");
        Record("Native workflow no unexpected dialog errors", dialogs.Errors.Count == 0, string.Join(";", dialogs.Errors));
    }
    private sealed class TestDialogs(string directory) : IUserDialogs
    {
        public string ProjectPath => Path.Combine(directory, "native-roundtrip.sealproj");
        public Queue<string> Opens { get; } = [];
        public List<string> Errors { get; } = [];
        public string? OpenFile(string title, string filter) => Opens.TryDequeue(out var path) ? path : null;
        public string? SaveFile(string title, string filter, string filename) => title == "保存项目" ? ProjectPath : Path.Combine(directory, filter.Contains("HTML") ? "native-report.html" : "native-results.csv");
        public bool Confirm(string title, string text) => true;
        public bool? SaveChanges(string text) => false;
        public void ShowError(string text) => Errors.Add(text);
    }
}
