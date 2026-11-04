# 空调鼓风机风量挡位与共振避让设计软件 V2.0

C# / WPF / MVVM 的 Windows 原生桌面程序。启动入口为 `BlowerResonance.sln`，主程序 `BlowerResonance.exe`。运行不启动本地 Web 服务，不依赖浏览器或网络。

## 运行

Windows 10/11 x64：解压自包含包，双击其中 `BlowerResonance.exe`。整个目录须一起保留；无需安装 .NET。发布包以跨平台生成的真实 Windows PE 程序及 WindowsDesktop 运行时组成，尚未在 Windows 上运行验收。

源码运行：安装 .NET 8 SDK，用 Visual Studio 2022 打开解决方案，将 `Blower.Desktop` 设为启动项目，或在本目录执行 `dotnet run --project src/Blower.Desktop -c Release`。也可双击 `run.bat`。首次源码还原需要 NuGet；发布后的程序完全离线运行。

构建及生成自包含目录：在 Windows 执行 `./build-windows.ps1`。Linux 可使用同样的 dotnet build / publish 命令进行交叉编译，但不能运行 WPF。

## 使用

普通启动为空白。文件菜单可新建、打开、保存、另存、载入独立模拟示例。左侧工程导航进入项目来源、规则、阶次、模态、挡位方案、图形、交叉、接近明细、扫描、比较、规则敏感性、验证清单和质量复核。表格选中行后在属性区编辑并应用；错误不会改动当前项目。

新建方案与新增挡位使用独立按钮。点击“方案属性”编辑名称与锁定；锁定挡位不能修改。F5计算；扫描接纳为新方案；比较方向是目标减基准。输入变化后重新计算。Ctrl+S保存到任意路径，覆盖前保留.bak，保存后重开核对；本地项目库位于 `%LOCALAPPDATA%\BlowerResonance\Projects`。

导出CSV、SVG和自包含HTML报告，保留软件版本、来源和输入哈希。HTML是成果文档。频率接近仅代表潜在接近，不能证明噪声根因、响应大小或实车改善。

## 验证与材料

见 `docs/验证记录.md`、`docs/要求符合性矩阵.md` 和实际日志。操作说明书与软著采集表为待 Windows 验收的材料草稿，真实 WPF 截图和 Microsoft Word 实际分页尚未完成。没有使用旧 Web 截图或模拟截图。

`docs/Windows验收步骤.md` 给出必须补验的界面步骤；`tools/capture-windows.ps1` 仅在 Windows 上捕获真实运行窗口。生产源码统计及完整打印稿由 `tools/build-materials.py` 根据实际构建文件生成。
