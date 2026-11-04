using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cabin.Core.IO;
using Cabin.Desktop;
using Cabin.Desktop.Services;
using Cabin.Desktop.ViewModels;
using Cabin.Desktop.Views;

namespace Cabin.WindowsChecks;

internal static class Program
{
    private static readonly List<object> Results = new();
    private static int failures;
    private static string directory = "";

    [STAThread]
    public static int Main(string[] args)
    {
        directory = Path.GetFullPath(args.Length > 0 ? args[0] : "windows-evidence");
        Directory.CreateDirectory(directory);
        var app = new App();
        app.InitializeComponent();
        app.StartMainWindow = false;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var services = new Services(directory);
        var window = new MainWindow(services);
        app.MainWindow = window;
        var bindingErrors = new StringWriter();
        var listener = new TextWriterTraceListener(bindingErrors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await CheckWindow(window, services);
                listener.Flush();
                Record("WPF binding errors absent", bindingErrors.ToString().Length == 0, bindingErrors.ToString());
            }
            catch (Exception error)
            {
                Record("Windows integration execution", false, error.ToString());
            }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "binding-errors.log"), bindingErrors.ToString());
                File.WriteAllText(Path.Combine(directory, "windows-ui-evidence.json"), JsonSerializer.Serialize(new
                {
                    executedUtc = DateTimeOffset.UtcNow,
                    platform = Environment.OSVersion.ToString(),
                    framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    rendering = "Actual WPF MainWindow shown on Windows. PNG files captured from the rendered native Window using RenderTargetBitmap; not browser or mock-up images.",
                    failed = failures,
                    checks = Results
                }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(failures == 0 ? 0 : 1);
            }
        };
        window.Show();
        app.Run();
        return failures == 0 ? 0 : 1;
    }

    private static async Task CheckWindow(MainWindow window, Services services)
    {
        var vm = (WorkspaceViewModel)window.DataContext;
        await Render(window, "01-empty");
        Record("ordinary native startup is empty", !vm.HasProject && vm.Result is null);
        vm.NewCommand.Execute(null);
        Record("blank project has no points", vm.Editor!.Sources.Count == 0 && vm.Editor.Seats.Count == 0);
        vm.SaveCommand.Execute(null);
        Record("blank save is a real reopenable file", NativeFiles.Open(services.ProjectPath).Sources.Count == 0);
        vm.ExampleCommand.Execute(null);
        await Render(window, "02-project");
        vm.Section = "points";
        await Render(window, "03-points");
        vm.CalculateCommand.Execute(null);
        await Wait(vm);
        Record("WPF calculate populates field and spectrum", vm.Field is not null && vm.Spectrum is not null);
        await Render(window, "04-response-field");
        vm.ShowShape = true;
        vm.UpdateFieldCommand.Execute(null);
        await Wait(vm);
        await Render(window, "05-mode-shape");
        vm.Section = "spectrum";
        await Render(window, "06-seat-spectrum");
        vm.Section = "modes";
        await Render(window, "07-modes");
        vm.Section = "contributions";
        await Render(window, "08-coupling");
        vm.Section = "comparison";
        vm.CaptureBaselineCommand.Execute(null);
        vm.CompareCommand.Execute(null);
        await Wait(vm);
        Record("native baseline comparison populated", vm.Comparison?.Seats.Count == 4);
        await Render(window, "09-comparison");
        vm.Section = "scan";
        vm.ScanCount.Text = "5";
        vm.ScanCommand.Execute(null);
        await Wait(vm);
        Record("native scan completed", vm.Scan?.Rows.Count == 5);
        await Render(window, "10-scan");
        vm.Section = "diagnostics";
        vm.TruncationCommand.Execute(null);
        await Wait(vm);
        Record("native truncation study populated", vm.Truncation?.Rows.Count == 4);
        await Render(window, "11-diagnostics");
        vm.Section = "reviews";
        await Render(window, "12-reviews");
        vm.ExportReportCommand.Execute(null);
        vm.SaveCommand.Execute(null);
        Record("native report contains model limitation", File.ReadAllText(services.ReportPath).Contains("非Pa，非dB SPL"));
        vm.NewCommand.Execute(null);
        vm.OpenCommand.Execute(null);
        Record("saved project reopened in native VM", vm.Editor!.Seats.Count == 4 && vm.Baselines.Count == 2);
        vm.CalculateCommand.Execute(null);
        await Wait(vm);
        window.Width = 1160;
        window.Height = 740;
        await Render(window, "13-minimum-window");
        Record("minimum native window rendered", window.FieldVisual.ActualWidth > 0 && window.FieldVisual.ActualHeight > 0);
        window.Width = 1440;
        window.Height = 920;
        vm.Editor!.Length.Text = "invalid";
        vm.Section = "project";
        await Render(window, "14-invalid-input");
        Record("edit invalidates native results", vm.Result is null && vm.Editor.Length.HasErrors);
        Record("native interactions have no service errors", services.Errors.Count == 0, string.Join("\n", services.Errors));
        vm.Section = "help";
        await Render(window, "15-help");
        var preview = PointCsv.Preview(PointCsv.Header + "\nseat,错误,NaN,0,0,,,", new());
        var import = new ImportWindow(preview) { Owner = window };
        import.Show();
        await Render(import, "16-invalid-csv-dialog");
        Record("native import dialog is rendered", import.IsVisible && !preview.CanApply);
        import.Close();
        var examples = new ExampleWindow { Owner = window };
        examples.Show();
        await Render(examples, "17-example-dialog");
        Record("native example dialog rendered", examples.IsVisible);
        examples.Close();
    }

    private static async Task Render(Window window, string name)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(150);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Record(name + " contains rendered pixels", pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 20);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(stream);
    }

    private static async Task Wait(WorkspaceViewModel vm)
    {
        var started = DateTime.UtcNow;
        while (vm.Busy)
        {
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(60))
                throw new TimeoutException("Windows calculation did not complete");
            await Task.Delay(20);
        }
    }

    private static void Record(string name, bool passed, string detail = "")
    {
        Results.Add(new { name, status = passed ? "passed" : "failed", detail });
        Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
        if (!passed)
            failures++;
    }

    private sealed class Services(string root) : IDesktopServices
    {
        public string ProjectPath => Path.Combine(root, "native-roundtrip.cabin.json");
        public string ReportPath => Path.Combine(root, "native-report.txt");
        public List<string> Errors { get; } = new();
        public string? OpenProjectPath() => ProjectPath;
        public string? SaveProjectPath(string currentPath, string suggested) => ProjectPath;
        public string? OpenCsvPath() => null;
        public string? ExportPath(string label, string extension) => ReportPath;
        public bool Confirm(string title, string message) => true;
        public int SaveChanges(string name) => 2;
        public int SelectExample() => 1;
        public bool ConfirmImport(CsvPreview preview) => preview.CanApply;
        public void Error(string message) => Errors.Add(message);
        public void ExportFieldImage() { }
    }
}
