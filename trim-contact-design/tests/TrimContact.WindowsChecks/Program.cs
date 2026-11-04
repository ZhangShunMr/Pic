using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;
using TrimContact.Desktop;
using TrimContact.Desktop.ViewModels;

namespace TrimContact.WindowsChecks;

internal static class Program
{
    private static readonly List<object> Checks = [];
    private static int failures;
    private static string directory = "";
    private static string screenshots = "";

    [STAThread]
    public static int Main(string[] args)
    {
        directory = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/windows");
        screenshots = Path.Combine(directory, "screenshots");
        Directory.CreateDirectory(screenshots);
        var app = new App();
        app.InitializeComponent();
        app.StartMainWindow = false;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var interaction = new Interaction(directory);
        var window = new MainWindow(interaction);
        app.MainWindow = window;
        using var bindingLog = new StringWriter();
        using var listener = new TextWriterTraceListener(bindingLog);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Run(window, interaction);
                listener.Flush();
                Record("WPF binding errors absent", bindingLog.ToString().Length == 0, bindingLog.ToString());
            }
            catch (Exception error)
            {
                Record("native WPF execution", false, error.ToString());
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "binding-errors.log"), bindingLog.ToString());
                File.WriteAllText(Path.Combine(directory, "windows-ui-evidence.json"), JsonSerializer.Serialize(new
                {
                    executedUtc = DateTimeOffset.UtcNow,
                    sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    runUrl = "https://github.com/" + Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") + "/actions/runs/" + Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
                    platform = Environment.OSVersion.ToString(),
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    rendering = "Actual production WPF MainWindow shown on Windows; client visual captured with RenderTargetBitmap after layout/render. Not browser or mock-up images.",
                    interaction = "Real WPF window, visual tree, bindings, native drawing and selected command automation. File/confirmation interface uses deterministic replacements; real shell dialogs are not certified.",
                    failed = failures,
                    screenshotCount = Directory.GetFiles(screenshots, "*.png").Length,
                    checks = Checks
                }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        };
        window.Show();
        app.Run();
        return failures == 0 ? 0 : 1;
    }

    private static async Task Run(MainWindow window, Interaction ui)
    {
        var vm = (WorkspaceViewModel)window.DataContext;
        await Capture(window, "01-empty");
        Record("ordinary WPF startup empty", vm.Draft.Variants.Count == 0 && vm.Draft.Loads.Count == 0);
        vm.DemoCommand.Execute(null);
        await Capture(window, "02-project");
        await Capture(window, "02a-project-fields", Descendants<GroupBox>(window).Single(g => g.Header as string == "项目身份与参数来源"));
        Record("independent example is marked simulation", vm.Draft.Origin == DataOrigin.Simulation);
        var mass = Descendants<TextBox>(window).Single(t => t.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "Draft.Panel.MassKg");
        mass.Text = "bad";
        await Render(window);
        Record("actual TextBox invalid number blocks calculation", vm.HasConversionErrors && !vm.CalculateCommand.CanExecute(null));
        await Capture(window, "03-invalid-number");
        mass.Text = "2";
        await Render(window);
        Record("actual TextBox binding writes draft number", vm.Draft.Panel.MassKg == 2 && !vm.HasConversionErrors);
        mass.Text = "1";
        await Render(window);
        vm.ApplyCommand.Execute(null);
        vm.SectionIndex = 1;
        await Capture(window, "04-layout");
        Record("native grids list four clips and one contact", Descendants<DataGrid>(window).Any(g => g.Items.Count == 4) && Descendants<DataGrid>(window).Any(g => g.Items.Count == 1));
        await Invoke(window, "复制方案");
        Record("real WPF command copies independent scheme", vm.Draft.Variants.Count == 2);
        vm.SelectedContact!.PreloadMm = 0.4;
        vm.NotifyDraftEdited();
        vm.ApplyCommand.Execute(null);
        vm.SectionIndex = 2;
        await Capture(window, "05-loads");
        await Invoke(window, "求解全部方案  F5");
        await Wait(vm);
        Record("native background calculation populates two schemes", vm.ResultsCurrent && vm.VariantResults.Count == 2 && vm.Envelopes.Count == 5);
        await Capture(window, "06-envelope");
        var resultTabs = Descendants<TabControl>(window).Single();
        resultTabs.SelectedIndex = 1;
        await Capture(window, "07-nominal-traces");
        resultTabs.SelectedIndex = 2;
        await Capture(window, "08-force-balance");
        Record("native balance rows close force and moments", vm.BalanceRows.All(r => Math.Abs(r.NormalErrorN) < 1e-6 && Math.Abs(r.MomentXErrorNmm) < 1e-4));
        resultTabs.SelectedIndex = 3;
        await Capture(window, "09-quality-review");
        vm.SectionIndex = 4;
        await Capture(window, "10-comparison");
        vm.SweepSteps = 4;
        vm.SweepCommand.Execute(null);
        await Wait(vm);
        Record("native scan completes without altering model", vm.SweepCurrent && vm.SweepRows.Count == 4);
        await Capture(window, "11-scan");
        vm.SectionIndex = 5;
        vm.BatchRequest.PreloadDeltaMm = 0.05;
        vm.PreviewBatchCommand.Execute(null);
        Record("native batch preview lists proposed edits", vm.BatchChanges.Count > 0);
        await Capture(window, "12-batch");
        vm.GenerateTasksCommand.Execute(null);
        Record("native task generation populated", vm.Tasks.Count > 0);
        await Capture(window, "13-verification-tasks");
        vm.SaveCommand.Execute(null);
        Record("native project save contains source and both schemes", DesktopFiles.Open(ui.ProjectPath).Project.Variants.Count == 2);
        ui.ExportPath = Path.Combine(directory, "native-report.html");
        vm.ExportReportCommand.Execute(null);
        Record("native report export contains source hash", File.ReadAllText(ui.ExportPath).Contains(ProjectCodec.InputHash(vm.Applied)));
        vm.NewCommand.Execute(null);
        vm.OpenCommand.Execute(null);
        Record("native saved project reopens", vm.Draft.Variants.Count == 2 && vm.Draft.Tasks.Count > 0 && !vm.ResultsCurrent);
        vm.SectionIndex = 7;
        await Capture(window, "14-help");
        window.Width = 1060;
        window.Height = 720;
        vm.SectionIndex = 1;
        await Capture(window, "15-minimum-window");
        Record("minimum window has visible layout controls", Descendants<DataGrid>(window).Count() == 2);
        Record("no desktop interaction errors", ui.Errors.Count == 0, string.Join("\n", ui.Errors));
    }

    private static async Task Invoke(MainWindow window, string label)
    {
        await Render(window);
        var button = Descendants<Button>(window).Single(b => b.Content as string == label);
        if (!button.IsEnabled) throw new InvalidOperationException("Button disabled: " + label);
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
        await Render(window);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static async Task Render(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(180);
        window.UpdateLayout();
    }

    private static async Task Capture(Window window, string name, FrameworkElement? target = null)
    {
        await Render(window);
        var visual = target ?? (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
            context.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, visual.ActualWidth, visual.ActualHeight));
        }
        bitmap.Render(drawing);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Record(name + " rendered with opaque background", pixels.Where((_, i) => i % 4 == 3).All(a => a == 255));
        Record(name + " contains native rendered content", pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 20);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(screenshots, name + ".png"));
        encoder.Save(file);
    }

    private static async Task Wait(WorkspaceViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (vm.IsBusy)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Native calculation timeout");
            await Task.Delay(20);
        }
    }

    private static void Record(string name, bool passed, string detail = "")
    {
        Checks.Add(new { name, passed, detail });
        if (!passed) failures++;
        Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
    }

    private sealed class Interaction(string root) : IDesktopInteraction
    {
        public string ProjectPath => Path.Combine(root, "native-roundtrip.trimproj");
        public string ExportPath { get; set; } = Path.Combine(root, "native-export.csv");
        public List<string> Errors { get; } = [];
        public string? OpenPath(string filter) => ProjectPath;
        public string? SavePath(string filter, string suggested) => filter.StartsWith("接触设计项目") ? ProjectPath : ExportPath;
        public bool Confirm(string message) => true;
        public UnsavedChoice Unsaved(string message) => UnsavedChoice.Discard;
        public void Error(string message) => Errors.Add(message);
        public void Information(string message) { }
    }
}
