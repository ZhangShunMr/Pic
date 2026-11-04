using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MotorFrequency.Application;
using MotorFrequency.Desktop;

namespace MotorFrequency.Desktop.Verification;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Requires Windows and a real WPF desktop. No screenshot or UI success is claimed.");
            return 2;
        }
        var nativeDialogsRequested = args.Contains("--native-dialogs");
        var output = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "docs/windows-evidence");
        Directory.CreateDirectory(output);
        foreach (var name in new[] { "result.json", "failure.txt", "native-dialogs.json",
            "dialog-01-save.png.failure.txt", "dialog-02-open.png.failure.txt", "dialog-03-export.png.failure.txt" })
        {
            var previous = Path.Combine(output, name);
            if (File.Exists(previous)) File.Delete(previous);
        }
        var errors = new BindingErrors();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        var interaction = new TestInteraction();
        var vm = new WorkspaceViewModel(interaction);
        window.DataContext = vm;
        window.Show();
        try
        {
            Pump();
            Require(!vm.HasProject, "Startup should be empty");
            Capture(window, Path.Combine(output, "00-empty.png"));
            vm.NewCommand.Execute(null);
            vm.Section = "motor";
            Pump();
            var poleBox = Descendants(window).OfType<TextBox>().First(x =>
                BindingOperations.GetBinding(x, TextBox.TextProperty)?.Path.Path == "Motor.PolePairs");
            poleBox.Text = "0";
            poleBox.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            vm.ApplyCommand.Execute(null);
            Require(vm.IsError && vm.Current!.Motor.PolePairs == 2, "Invalid editor must preserve results");
            Capture(window, Path.Combine(output, "01-invalid-input.png"));
            vm.ResetCommand.Execute(null);
            vm.DemoCommand.Execute(null);
            Require(vm.Current!.Demonstration, "Demo must be explicitly marked");
            foreach (var page in WorkspaceViewModel.Navigation)
            {
                vm.Section = page.Key;
                Pump();
                Require(Descendants(window).OfType<UserControl>().Any(x => x.GetType().Namespace == "MotorFrequency.Desktop.Views"),
                    "Expected native UserControl on " + page.Key);
                if (page.Key == "sweep") vm.SweepCommand.Execute(null);
                Pump();
                Capture(window, Path.Combine(output, page.Key + ".png"));
            }
            vm.Section = "candidates";
            Pump();
            Require(Descendants(window).OfType<DataGrid>().Any(x => x.Items.Count >= 3), "Candidate table should be bound");
            window.Width = 1100;
            window.Height = 720;
            Pump();
            Capture(window, Path.Combine(output, "minimum-window.png"));
            if (nativeDialogsRequested) NativeDialogVerification.Run(window, output);
            Require(errors.Errors.Count == 0, "WPF binding errors: " + string.Join(" | ", errors.Errors));
            File.WriteAllText(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
                os = Environment.OSVersion.ToString(),
                result = "passed",
                source = "Live shown WPF window, RenderTargetBitmap capture, no browser",
                nativeFileDialogs = nativeDialogsRequested
                    ? "Real Windows SaveFileDialog/OpenFileDialog automated in own process; save, reopen, HTML export verified"
                    : "Not executed in basic WPF check; separate --native-dialogs verification required",
                pages = WorkspaceViewModel.Navigation.Select(x => x.Key),
                bindingErrors = errors.Errors
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS live WPF page loading, editor binding, rejection, chart/table binding, minimum size capture");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            window.Close();
            app.Shutdown();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pump()
    {
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

internal sealed class TestInteraction : IDesktopInteraction
{
    public string? OpenProjectFile() => null;
    public string? OpenSpectrumFile() => null;
    public string? SaveFile(string suggestedName, string extension, string description) => null;
    public LeaveChoice ConfirmLeave(string reason) => LeaveChoice.Discard;
    public bool Confirm(string message) => true;
    public void RevealFile(string path) { }
}

internal sealed class BindingErrors : TraceListener
{
    public List<string> Errors { get; } = [];
    public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
    public override void WriteLine(string? message) => Write(message);
}
