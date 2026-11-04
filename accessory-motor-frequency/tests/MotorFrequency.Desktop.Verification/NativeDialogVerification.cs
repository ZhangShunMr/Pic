using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MotorFrequency.Application;
using MotorFrequency.Desktop.Services;

namespace MotorFrequency.Desktop.Verification;

internal static class NativeDialogVerification
{
    internal static void Run(Window window, string output)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Motor frequency 实机文件 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var interaction = new AutomatedNativeInteraction(window, directory, output);
        var vm = new WorkspaceViewModel(interaction);
        window.DataContext = vm;
        try
        {
            vm.DemoCommand.Execute(null);
            vm.SaveCommand.Execute(null);
            Require(!vm.IsError && File.Exists(interaction.ProjectPath), "Native save: " + vm.Message);
            var id = vm.Current!.Id;
            vm.OpenCommand.Execute(null);
            Require(!vm.IsError && vm.Current!.Id == id && vm.Current.Spectra[0].Points.Count == 751, "Native reopen: " + vm.Message);
            vm.SelectedExport = WorkspaceViewModel.ExportOptions.Single(x => x.Key == "report");
            vm.ExportCommand.Execute(null);
            Require(!vm.IsError && File.ReadAllText(interaction.ReportPath).Contains("模拟演示"), "Native export: " + vm.Message);
            File.WriteAllText(Path.Combine(output, "native-dialogs.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                sourceCommit = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                actualNativeDialogs = true,
                scope = "Only dialogs belonging to this verifier process; UI Automation sets filename and invokes native Save/Open buttons",
                projectSave = File.Exists(interaction.ProjectPath),
                projectReopenPoints = vm.Current!.Spectra[0].Points.Count,
                reportExport = File.Exists(interaction.ReportPath),
                pathsIncludeSpacesAndChinese = true,
                captures = new[] { "dialog-01-save.png", "dialog-02-open.png", "dialog-03-export.png" }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class AutomatedNativeInteraction(Window owner, string directory, string output) : IDesktopInteraction
    {
        private readonly NativeInteraction native = new(owner);
        internal string ProjectPath => Path.Combine(directory, "模拟项目.motorproj");
        internal string ReportPath => Path.Combine(directory, "模拟报告.html");
        public string? OpenProjectFile() => Dialog(native.OpenProjectFile, "打开项目或将备份恢复为副本", ProjectPath,
            Path.Combine(output, "dialog-02-open.png"));
        public string? OpenSpectrumFile() => throw new NotSupportedException("Not part of this native dialog verification");
        public string? SaveFile(string name, string extension, string description) => Dialog(
            () => native.SaveFile(name, extension, description), "选择本机保存位置",
            extension == ".motorproj" ? ProjectPath : ReportPath,
            Path.Combine(output, extension == ".motorproj" ? "dialog-01-save.png" : "dialog-03-export.png"));
        public LeaveChoice ConfirmLeave(string reason) => LeaveChoice.Discard;
        public bool Confirm(string message) => true;
        public void RevealFile(string path) => throw new NotSupportedException("Not verified by native dialog automation");
    }

    private static string? Dialog(Func<string?> operation, string title, string path, string screenshot)
    {
        Exception? failure = null;
        nint handle = 0;
        var worker = new Thread(() =>
        {
            try
            {
                AutomationElement? dialog = null;
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    dialog = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
                        new PropertyCondition(AutomationElement.ProcessIdProperty, Environment.ProcessId),
                        new PropertyCondition(AutomationElement.NameProperty, title)));
                    if (dialog is not null) break;
                    Thread.Sleep(100);
                }
                Require(dialog is not null, "Native dialog not found: " + title);
                handle = new nint(dialog!.Current.NativeWindowHandle);
                var edits = dialog.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                AutomationElement? fileName = null;
                foreach (AutomationElement edit in edits)
                {
                    if (edit.Current.AutomationId == "1001" || edit.Current.Name.Contains("File name", StringComparison.OrdinalIgnoreCase)
                        || edit.Current.Name.Contains("文件名"))
                    {
                        fileName = edit;
                        break;
                    }
                }
                Require(fileName is not null && fileName.TryGetCurrentPattern(ValuePattern.Pattern, out _), "Native filename edit not found");
                ((ValuePattern)fileName!.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
                CaptureDialog(handle, screenshot);
                var accept = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1")));
                Require(accept is not null, "Native Save/Open button not found");
                ((InvokePattern)accept!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            catch (Exception error)
            {
                failure = error;
                if (handle != 0) PostMessage(handle, 0x0010, 0, 0);
            }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        var result = operation();
        if (!worker.Join(25000)) throw new TimeoutException("Native dialog automation did not finish");
        if (failure is not null) throw new InvalidOperationException("Native dialog automation failed", failure);
        Require(result is not null && Path.GetFullPath(result) == Path.GetFullPath(path), "Native selected path differs");
        return result;
    }

    private static void CaptureDialog(nint window, string path)
    {
        Require(GetWindowRect(window, out var rectangle), "Cannot read native window rectangle");
        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        var screen = GetDC(window);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            Require(PrintWindow(window, memory, 2), "PrintWindow did not capture native dialog");
            var info = new BitmapInfo
            {
                Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height,
                Planes = 1, BitCount = 32, SizeImage = (uint)(width * height * 4)
            };
            var pixels = new byte[width * height * 4];
            Require(GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) != 0, "Cannot read dialog pixels");
            Require(pixels.Distinct().Count() > 16, "Native capture is unexpectedly blank");
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var file = File.Create(path);
            encoder.Save(file);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(window, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowRect rectangle);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] pixels, ref BitmapInfo info, uint usage);
}
