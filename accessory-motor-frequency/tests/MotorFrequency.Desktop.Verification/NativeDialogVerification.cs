using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
                scope = "Only dialogs belonging to this verifier process; UI Automation locates filename/accept controls and real keyboard input submits native Save/Open",
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
        using var finished = new ManualResetEventSlim(false);
        var watchdog = new Thread(() =>
        {
            if (!finished.Wait(TimeSpan.FromSeconds(30)))
            {
                var modal = FindWindow(null, title);
                if (modal != 0)
                {
                    GetWindowThreadProcessId(modal, out var process);
                    if (process == Environment.ProcessId) PostMessage(modal, 0x0010, 0, 0);
                }
            }
        }) { IsBackground = true };
        watchdog.Start();
        var worker = new Thread(() =>
        {
            try
            {
                Console.WriteLine("Native dialog: waiting for " + title);
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    var candidate = FindWindow(null, title);
                    if (candidate != 0)
                    {
                        GetWindowThreadProcessId(candidate, out var process);
                        if (process == Environment.ProcessId) { handle = candidate; break; }
                    }
                    Thread.Sleep(100);
                }
                Require(handle != 0, "Native dialog not found: " + title);
                var dialog = AutomationElement.FromHandle(handle);
                Console.WriteLine("Native dialog: found owned window");
                if (dialog.TryGetCurrentPattern(WindowPattern.Pattern, out var ready))
                    ((WindowPattern)ready).WaitForInputIdle(3000);
                Thread.Sleep(250);
                var edits = dialog.FindAll(TreeScope.Descendants, new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox)));
                var controls = new List<object>();
                foreach (AutomationElement element in edits)
                {
                    controls.Add(new { element.Current.Name, element.Current.AutomationId,
                        type = element.Current.ControlType.ProgrammaticName,
                        valuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out _) });
                }
                File.WriteAllText(screenshot + ".controls.json", System.Text.Json.JsonSerializer.Serialize(controls,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                AutomationElement? fileName = edits.Cast<AutomationElement>().FirstOrDefault(edit =>
                    (edit.Current.AutomationId is "1001" or "1148") && edit.Current.ControlType == ControlType.Edit
                    && edit.TryGetCurrentPattern(ValuePattern.Pattern, out _));
                fileName ??= edits.Cast<AutomationElement>().FirstOrDefault(edit =>
                    edit.Current.ControlType == ControlType.Edit
                    && (edit.Current.Name.Contains("File name", StringComparison.OrdinalIgnoreCase)
                    || edit.Current.Name.Contains("文件名"))
                    && edit.TryGetCurrentPattern(ValuePattern.Pattern, out _));
                Require(fileName is not null && fileName.TryGetCurrentPattern(ValuePattern.Pattern, out _), "Native filename edit not found");
                SetForegroundWindow(handle);
                Require(GetForegroundWindow() == handle, "Cannot focus own native dialog for keyboard input");
                fileName!.SetFocus();
                for (var attempt = 0; attempt < 40 && !fileName.Current.HasKeyboardFocus; attempt++)
                    Thread.Sleep(50);
                Require(fileName.Current.HasKeyboardFocus, "Native filename edit did not receive keyboard focus");
                var filenameValue = (ValuePattern)fileName.GetCurrentPattern(ValuePattern.Pattern);
                PressKey(0x11, false);
                PressKey(0x41);
                PressKey(0x11, true);
                foreach (var character in path)
                {
                    SendKeys(new KeyboardInput { Scan = character, Flags = 4 },
                        new KeyboardInput { Scan = character, Flags = 6 });
                }
                var entered = "";
                var complete = false;
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    entered = filenameValue.Current.Value;
                    if (string.Equals(entered.Trim('"'), path, StringComparison.OrdinalIgnoreCase))
                    {
                        complete = true;
                        break;
                    }
                    Thread.Sleep(100);
                }
                File.WriteAllText(screenshot + ".input.json", System.Text.Json.JsonSerializer.Serialize(new
                {
                    expected = path, actual = entered, complete, keyboardFocus = fileName.Current.HasKeyboardFocus
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                CaptureDialog(handle, screenshot);
                Require(complete, "Native filename edit did not retain complete entered path; see input.json");
                Console.WriteLine("Native dialog: filename entered by real keyboard input");
                var accept = dialog.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1")));
                Require(accept is not null, "Native Save/Open button not found");
                accept!.SetFocus();
                Require(GetForegroundWindow() == handle, "Own dialog lost foreground before submit");
                PressKey(0x20);
            }
            catch (Exception error)
            {
                failure = error;
                File.WriteAllText(screenshot + ".failure.txt", error.ToString());
                Console.Error.WriteLine(error);
                if (handle != 0) PostMessage(handle, 0x0010, 0, 0);
            }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        string? result;
        try { result = operation(); }
        finally { finished.Set(); watchdog.Join(1000); }
        if (!worker.Join(25000)) throw new TimeoutException("Native dialog automation did not finish");
        if (failure is not null) throw new InvalidOperationException("Native dialog automation failed", failure);
        File.WriteAllText(screenshot + ".selection.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            expected = path, actual = result,
            expectedCanonical = CanonicalPath(path), actualCanonical = result is null ? null : CanonicalPath(result)
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Require(result is not null && string.Equals(CanonicalPath(result), CanonicalPath(path),
            StringComparison.OrdinalIgnoreCase), "Native selected path differs; see selection.json");
        return result;
    }

    private static void PressKey(ushort key, bool? release = null)
    {
        if (release is null)
            SendKeys(new KeyboardInput { VirtualKey = key }, new KeyboardInput { VirtualKey = key, Flags = 2 });
        else
            SendKeys(new KeyboardInput { VirtualKey = key, Flags = release.Value ? 2u : 0u });
    }

    private static void SendKeys(params KeyboardInput[] keys)
    {
        var input = keys.Select(key => new Input { Type = 1, Data = new InputData { Keyboard = key } }).ToArray();
        Require(SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) == input.Length,
            "Native keyboard input was not delivered");
    }

    private static string CanonicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        var buffer = new StringBuilder(32768);
        var length = GetLongPathName(directory, buffer, (uint)buffer.Capacity);
        return Path.Combine(length > 0 && length < buffer.Capacity ? buffer.ToString() : directory, Path.GetFileName(full));
    }

    private static void CaptureDialog(nint window, string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { CaptureDialogOnce(window, path); return; }
            catch (InvalidOperationException error) when (attempt < 15
                && (error.Message.Contains("capture is unexpectedly blank") || error.Message.Contains("PrintWindow")))
            {
                Thread.Sleep(100);
            }
        }
    }

    private static void CaptureDialogOnce(nint window, string path)
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
            SelectObject(memory, previous);
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
    private struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
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
