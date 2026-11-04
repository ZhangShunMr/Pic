using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Avas.Core.Analysis;
using Avas.Core.IO;
using Avas.Desktop;
using Avas.Workspace;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.FirstOrDefault() ?? "docs/windows-ui");
        Directory.CreateDirectory(output);
        var app = new Application
        {
            Resources = (ResourceDictionary)Application.LoadComponent(new Uri("/AvasCoverage;component/Theme.xaml", UriKind.Relative))
        };
        var files = new VerificationFiles(Path.Combine(output, "files"));
        var vm = new WorkspaceViewModel(new ProjectStore(Path.Combine(output, "project-library")), files);
        var window = new MainWindow(vm);
        var errors = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var checks = new List<string>();
        var images = new List<object>();
        var failed = false;
        var expectedValidationTrace = new List<string>();
        void Assert(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks.Add(message);
        }
        async Task Layout()
        {
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(120);
        }
        async Task Capture(string name)
        {
            await Layout();
            // This renders the live, shown WPF visual tree. It is not a generated mockup.
            var view = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var client = new DrawingVisual();
            using (var drawing = client.RenderOpen())
            {
                drawing.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                drawing.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            }
            bitmap.Render(client);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Assert(pixels.Where((_, i) => i % 4 == 3).All(alpha => alpha == 255), name + "：原生背景不透明");
            Assert(pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 20, name + "：包含实际渲染内容");
            var path = Path.Combine(output, name + ".png");
            using (var stream = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }
            images.Add(new { file = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), width = bitmap.PixelWidth, height = bitmap.PixelHeight, provenance = "live Windows WPF client visual, shown MainWindow" });
            var inspector = (FrameworkElement)window.FindName("Inspector");
            var detail = new RenderTargetBitmap((int)Math.Ceiling(inspector.ActualWidth), (int)Math.Ceiling(inspector.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            detail.Render(inspector);
            var detailPath = Path.Combine(output, name + "-editor.png");
            using (var stream = File.Create(detailPath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(detail));
                encoder.Save(stream);
            }
            images.Add(new { file = Path.GetFileName(detailPath), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(detailPath))).ToLowerInvariant(), width = detail.PixelWidth, height = detail.PixelHeight, provenance = "live, visible WPF Inspector panel from the same shown MainWindow; original pixels" });
        }
        async Task Page(string page)
        {
            vm.SelectedSection = page;
            await Layout();
            Assert(vm.Tab == page, "进入工作区：" + page);
        }
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Layout();
                Assert(window.IsVisible && vm.Project.Speakers.Count == 0 && vm.CurrentResult is null, "真实窗口空白启动");
                await Capture("01-空白项目");
                var project = Descendants<TextBox>(window).First(x => x.Name == "ProjectName");
                project.Text = "WPF界面验证项目";
                project.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert(vm.HasDraft, "WPF文本绑定写入草稿");
                vm.Commands["Apply"].Execute(null);
                Assert(vm.Project.Name == "WPF界面验证项目" && vm.Errors.Count == 0, "原生命令应用字段");
                vm.Commands["Demo"].Execute(null);
                await vm.CalculateAsync();
                Assert(vm.CurrentResult?.Listeners.Count == 4, "独立模拟示例完成声场计算");
                await Capture("02-模拟声场");
                await Page("扬声器");
                await Capture("03-扬声器频带");
                var coordinate = Descendants<TextBox>(window).First(x => x.Name == "SpeakerX");
                Assert(errors.Errors.Count == 0, "正常输入阶段没有WPF绑定错误");
                coordinate.Text = "abc";
                coordinate.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert(Validation.GetHasError(coordinate) && vm.BindingErrorCount > 0, "原生数值绑定拒绝文字输入");
                vm.Commands["Apply"].Execute(null);
                Assert(vm.Errors.Count > 0 && vm.BindingErrorCount > 0 && coordinate.Text == "abc",
                    "格式错误阻止应用旧数值并保留待修正文字");
                await Capture("04-数值校验");
                vm.Commands["Discard"].Execute(null);
                await Layout();
                Assert(vm.BindingErrorCount == 0 && !vm.HasDraft, "放弃编辑恢复有效数值");
                expectedValidationTrace.AddRange(errors.Errors);
                errors.Errors.Clear();
                await Page("背景与网格"); await Capture("05-背景网格");
                await Page("观察点"); await Capture("06-观察点频谱");
                await Page("方案比较");
                vm.SnapshotName = "基线"; vm.CaptureSnapshot();
                vm.SpeakerDraft!.Gain += 3; vm.ApplyDraft(); await vm.CalculateAsync();
                vm.SnapshotName = "增益方案"; vm.CaptureSnapshot(); vm.Compare();
                Assert(vm.Comparison is { Comparable: true }, "真实窗口方案比较");
                await Capture("07-方案比较");
                await Page("扫描与剖面");
                vm.ScanRequest = new ScanRequest { SpeakerId = vm.SelectedSpeakerId!.Value, Parameter = ScanParameter.Gain, Start = 0, End = 2, Step = 1 };
                await vm.RunScanAsync(); await vm.RunTransectAsync();
                Assert(vm.Scan?.Rows.Count == 3 && vm.Transect?.Points.Count > 0, "真实窗口扫描与剖面");
                await Capture("08-扫描剖面");
                await Page("音频试听"); vm.AuditionSeconds = 1; await vm.RenderAuditionAsync();
                Assert(vm.Audition is { Peak: < .98 }, "试听波形制作完成；不验证听感");
                await Capture("09-相对试听");
                await Page("成果与复核"); vm.Save(); vm.Export("report");
                Assert(vm.Errors.Count == 0 && File.Exists(vm.LastDownload), "真实命令保存并输出报告；文件选择器由测试宿主替代");
                await Capture("10-成果复核");
                window.Width = 1120; window.Height = 700; await Capture("11-最小窗口");
                Assert(errors.Errors.Count == 0, "没有WPF数据绑定路径错误");
            }
            catch (Exception error)
            {
                failed = true;
                checks.Add("FAILED: " + error);
                await Capture("failure");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output, "desktop-evidence.json"), ProjectCodec.Json(new
                {
                    at = DateTimeOffset.UtcNow,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    status = failed ? "failed" : "passed",
                    limitations = new[] { "File picker and confirmation host replaced for deterministic automation", "Audio playback and perceived audibility not verified", "Client area captures exclude operating-system window chrome", "Manual keyboard, scaling and screen-reader checks still required" },
                    checks,
                    bindingErrors = errors.Errors,
                    expectedInvalidNumericValidationTrace = expectedValidationTrace,
                    images
                }));
                vm.DisposeServices();
                app.Shutdown(failed ? 1 : 0);
            }
        };
        return app.Run(window);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T target) yield return target;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
internal sealed class BindingTrace : TraceListener
{
    public List<string> Errors { get; } = [];
    public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
    public override void WriteLine(string? message) => Write(message);
}
internal sealed class VerificationFiles(string root) : IDesktopServices
{
    public string LastReadName => "";
    public bool Confirm(string title, string message) => true;
    public byte[]? ReadFile(string kind) => null;
    public string WriteExport(byte[] bytes, string suggestedName)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, suggestedName);
        File.WriteAllBytes(path, bytes);
        return path;
    }
    public string WriteTemporaryAudio(byte[] bytes) => WriteExport(bytes, "相对试听.wav");
    public void PlayAudio(string path) { }
    public void StopAudio() { }
}
