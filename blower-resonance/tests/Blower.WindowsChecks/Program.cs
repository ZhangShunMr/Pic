using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Desktop;
using Blower.Desktop.Services;
using Blower.Desktop.ViewModels;

internal static class Program
{
    private static readonly List<object> checks = [];
    private static int failed;
    private static string folder = "";
    [STAThread]
    public static int Main(string[] args)
    {
        folder = Path.GetFullPath(args.FirstOrDefault() ?? "docs/screenshots");
        Directory.CreateDirectory(folder);
        var app = new App();
        app.InitializeComponent();
        app.StartMainWindow = false;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var services = new TestDialogs(folder);
        var window = new MainWindow(services);
        app.MainWindow = window;
        var errors = new StringWriter();
        var listener = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Run(window, services);
                listener.Flush();
                Check("No actual WPF binding errors", errors.ToString().Length == 0, errors.ToString());
            }
            catch (Exception e)
            {
                Check("Windows integration completed", false, e.ToString());
            }
            finally
            {
                File.WriteAllText(Path.Combine(folder,"binding-errors.log"),errors.ToString());
                File.WriteAllText(Path.Combine(folder,"windows-ui-evidence.json"),JsonSerializer.Serialize(new
                {
                    executedUtc=DateTimeOffset.UtcNow,
                    sourceCommit=Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    platform=Environment.OSVersion.ToString(),
                    framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    screenshotMethod="Actual production WPF MainWindow shown on Windows; native Window client content captured with RenderTargetBitmap after layout/render. No browser or mock-up. Test dialog adapter is used for confirmations and file paths.",
                    failed,
                    checks
                },new JsonSerializerOptions{WriteIndented=true}));
                app.Shutdown(failed==0?0:1);
            }
        };
        window.Show();
        app.Run();
        return failed==0?0:1;
    }
    private static async Task Run(MainWindow w,TestDialogs d)
    {
        var vm=(WorkspaceViewModel)w.DataContext;
        await Capture(w,"01-empty");
        Check("Ordinary WPF startup is empty",vm.Project.Modes.Count==0 && vm.Project.Plans.Count==0);
        Check("Native menu Save is bound",Descendants<Menu>(w).Single().Items.OfType<MenuItem>().SelectMany(x=>x.Items.OfType<MenuItem>()).Any(x=>(string?)x.Header=="保存" && x.Command==vm.SaveCommand));
        vm.SaveCommand.Execute(null);
        Check("Blank native save reopens",DesktopFiles.Open(d.Path).Project.Plans.Count==0);
        vm.DemoCommand.Execute(null);
        await Capture(w,"02-project");
        Check("Explicit demo retains simulation marker",vm.Project.Origin==Origin.Simulation);
        Navigate(w,vm,"settings"); await Capture(w,"03-settings");
        var blade=vm.Form.Fields.Single(x=>x.Label=="叶片数");
        var text=Descendants<TextBox>(w).Single(x=>ReferenceEquals(x.DataContext,blade));
        text.Text="0";
        text.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Click(w,"应用属性");
        Check("Real TextBox binding and bound Apply reject zero blades",vm.Error && vm.Project.Settings.Blades==40);
        await Capture(w,"16-invalid-input");
        Click(w,"还原草稿");
        Navigate(w,vm,"orders"); await Capture(w,"04-orders");
        Navigate(w,vm,"modes");
        var grid=Descendants<DataGrid>(w).Single(x=>x.IsVisible);
        grid.SelectedItem=vm.Project.Modes[0];
        await Capture(w,"05-modes");
        Check("Native DataGrid selection populates modal editor",vm.Form.Fields.Any(x=>x.Label=="模态频率 Hz"));
        Navigate(w,vm,"plans"); await Capture(w,"06-plans");
        Click(w,"计算交叉与接近  F5"); await Wait(vm);
        Check("Bound native Calculate populates contact results",vm.Result?.Contacts.Count==144);
        Navigate(w,vm,"plot"); await Capture(w,"07-frequency-plot");
        Check("Native drawing has chart data",vm.Chart?.Lines.Count==4 && vm.Chart.Bands.Count==3);
        Navigate(w,vm,"crossings"); await Capture(w,"08-crossings");
        Navigate(w,vm,"contacts"); await Capture(w,"09-contacts");
        Navigate(w,vm,"scan"); Click(w,"运行约束扫描"); await Wait(vm);
        Check("Bound scan completed",vm.Scan?.Feasible==true && vm.Scan.Choices.Count==6);
        await Capture(w,"10-scan");
        Click(w,"接纳为新方案");
        Check("Scan acceptance creates separate plan",vm.Project.Plans.Count==3);
        vm.ComputeCommand.Execute(null); await Wait(vm);
        Navigate(w,vm,"compare"); vm.ComparePlan=vm.Project.Plans[^1]; Click(w,"比较");
        Check("Native comparison populated",vm.Comparison?.Rows.Count==6);
        await Capture(w,"11-comparison");
        Navigate(w,vm,"study"); Click(w,"试算规则组合"); await Wait(vm);
        Check("Rule sensitivity has nine cases",vm.Study?.Cases.Count==9);
        await Capture(w,"12-rule-study");
        Navigate(w,vm,"checks"); Click(w,"为当前方案生成验证任务");
        Check("Native verification task generation",vm.Project.Checks.Count>0);
        await Capture(w,"13-checks");
        Navigate(w,vm,"review"); await Capture(w,"14-review");
        Navigate(w,vm,"exchange"); vm.CsvText="name,rpm,fixed\nlow,bad,false"; Click(w,"预检");
        Check("CSV error has actual row",vm.Preview?.Problems.Any(x=>x.Line==2)==true);
        await Capture(w,"15-csv-error");
        vm.SaveCommand.Execute(null);
        vm.NewCommand.Execute(null); vm.OpenCommand.Execute(null);
        Check("Actual native VM save/reopen retains plans and checks",vm.Project.Plans.Count==3 && vm.Project.Checks.Count>0);
        vm.ComputeCommand.Execute(null); await Wait(vm);
        vm.ExportContactsCommand.Execute(null);
        Check("Native export writes real CSV with source",File.ReadAllText(d.ExportPathValue).Contains("模拟设计数据"));
        Navigate(w,vm,"plot"); w.Width=1040; w.Height=700;
        await Capture(w,"17-minimum-window");
        Check("Minimum native window lays out nonzero chart",w.ActualWidth>=1040 && Descendants<Blower.Desktop.Controls.FrequencyCanvas>(w).Single().ActualWidth>200);
        w.Width=1450; w.Height=940;
        Navigate(w,vm,"help"); await Capture(w,"18-help");
        vm.SaveCommand.Execute(null);
    }
    private static void Navigate(MainWindow w,WorkspaceViewModel vm,string key)
    {
        var nav=Descendants<ListBox>(w).Single();
        nav.SelectedItem=vm.Nodes.Single(x=>x.Key==key);
        w.UpdateLayout();
        Check("Native navigation binding: "+key,vm.SelectedNode?.Key==key);
    }
    private static void Click(MainWindow w,string caption)
    {
        var button=Descendants<Button>(w).Single(x=>x.IsVisible && Equals(x.Content,caption));
        Check("Native command bound and available: "+caption,button.Command?.CanExecute(button.CommandParameter)==true);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Raising a RoutedEvent does not call Button.OnClick. Execute its actual bound command.
        button.Command!.Execute(button.CommandParameter);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T typed) yield return typed;
            foreach(var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static async Task Wait(WorkspaceViewModel vm)
    {
        var end=DateTime.UtcNow.AddSeconds(60);
        while(vm.Busy)
        {
            if(DateTime.UtcNow>end) throw new TimeoutException("Native calculation timed out");
            await Task.Delay(20);
        }
    }
    private static async Task Capture(Window w,string name)
    {
        w.UpdateLayout();
        await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
        await Task.Delay(180);
        var visual=(FrameworkElement)w.Content;
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);
        var surface=new DrawingVisual();
        using(var draw=surface.RenderOpen())
        {
            draw.DrawRectangle(w.Background,null,new Rect(0,0,bitmap.PixelWidth,bitmap.PixelHeight));
            draw.DrawRectangle(new VisualBrush(visual),null,new Rect(0,0,visual.ActualWidth,visual.ActualHeight));
        }
        bitmap.Render(surface);
        var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
        bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
        Check(name+" is rendered native content",pixels.Where((_,i)=>i%4!=3).Distinct().Count()>20);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(Path.Combine(folder,name+".png")); encoder.Save(stream);
    }
    private static void Check(string name,bool pass,string detail="")
    {
        checks.Add(new{name,status=pass?"passed":"failed",detail});
        Console.WriteLine((pass?"PASS ":"FAIL ")+name);
        if(!pass) failed++;
    }
    private sealed class TestDialogs(string folder):IDesktopDialogs
    {
        public string Path {get;}=System.IO.Path.Combine(folder,"native-roundtrip.blowerproj");
        public string ExportPathValue {get;}=System.IO.Path.Combine(folder,"native-export.csv");
        public string? OpenProject()=>Path;
        public string? SaveProject(string name)=>Path;
        public string? OpenCsv()=>null;
        public string? ExportPath(string name,string extension)=>ExportPathValue;
        public bool Confirm(string message)=>true;
        public SaveDecision Unsaved(string message)=>SaveDecision.Discard;
    }
}
