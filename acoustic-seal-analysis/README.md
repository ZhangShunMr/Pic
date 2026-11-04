# 车身声学泄漏路径与封堵试验分析软件 V3.0

C# / WPF / MVVM Windows 原生桌面版，入口 `AcousticSeal.sln`，启动项目 `AcousticSeal.Desktop`。不启动 HTTP 服务，不使用 WebView、浏览器或 Razor 界面。HTML 仅为可携带的成果报告格式。

从 main 的 `2bf4fc16fbca9c7ef36c4cce0a7d3034a16a2cd4` 建立独立分支 `windows-native/acoustic-seal-analysis`。仅新增本项目目录；两份规范的原文保存于 docs。旧版数据、算法和验证基于备份标签 `backup/pre-windows-native-20261104/cloud-dev-20261102` 复核；只复用独立的 C# 核心业务层，全部界面重新实现。

## Windows 运行

目标为 Windows 10/11 x64。推荐 Windows 11；已在 GitHub Actions Windows Server 2025 验证原生窗口及限定控件流程；未验证所有 Windows 10/11 硬件、DPI 和系统文件对话框交互。

1. 仓库 `releases/AcousticSeal-V3.0-win-x64.zip` 提供本次构建的自包含发布包。取得自包含发布目录时，完整解压到可写目录，双击 `AcousticSeal.Desktop.exe`。必须保留同目录全部文件。运行时无需网络、SDK、浏览器或服务器。
2. 从源码运行时，安装 .NET SDK **10.0.403**，在本目录执行 `dotnet build AcousticSeal.sln -c Release`，然后 `dotnet run --project src/AcousticSeal.Desktop -c Release`。首次还原 Windows targeting pack 需要联网；运行不联网。
3. Visual Studio 打开 `AcousticSeal.sln`，使用支持 .NET 10 的 Visual Studio，并安装 .NET 桌面开发组件；设置 `AcousticSeal.Desktop` 为启动项目。也可以双击 `run.cmd`。
4. 自包含发布：`powershell -ExecutionPolicy Bypass -File tools/publish-windows.ps1`。输出 `artifacts/win-x64`，完整复制到离线 Windows 机器。

启动为空白项目；菜单“文件→载入独立模拟案例”会先处理未保存工作，再载入标注为模拟的案例。左侧十个任务工作页；标准文件菜单用于打开、保存和成果输出，模块不依赖顶部按钮导航。

## 完整业务流程

项目档案 → 封堵位置（材料/覆盖/工艺）→ 测量工况（车辆/计权/标定/数值口径）→ 基线、单项、组合、恢复试验 → 手工频带或 CSV 预检导入 → 显式配对 → 差值、原始频谱、重复性、漂移与恢复检查 → 保存 `.sealproj` → 重开 → 导出报告/结果/原始频带/分析 JSON。

字段编辑为独立草稿；“应用编辑”整体验证后更新项目并重算。切换页面、打开项目、新建、载入示例或退出时处理未应用和未保存修改。删除引用对象会阻止；试验复制仅复制结构，不复制测量。保存可选择任意文件名，覆盖保留 `.bak`，检测外部修改与删除冲突。打开备份或旧格式后要求另存，不覆盖原件。项目内嵌全部频带，不依赖原始 CSV 的后续位置。撤销/重做限当前会话20步，累计审计和复核进入文件。

CSV 为 UTF-8 三列 `trial_id,frequency_hz,level_db`；试验 GUID 可从频带导出获取。支持逗号/分号/制表符、合并/替换、缺失值及整批失败。预检内容改变后必须重检。频带范围 1—100000 Hz，声级 -100—200 dB re 20 µPa，缺失不补零。

结果是测得声级差 ΔL=基线−目标，不是路径独立贡献；不同口径/来源会阻断；单项分贝不能相加预测组合。工程阈值非标准限值。独立模拟案例与导出均保留模拟标识，不能证明实车效果。

## 验证与材料

`dotnet run --project tests/AcousticSeal.Verification -c Release` 执行核心与 MVVM 逻辑验证。`python tools/source_inventory.py` 重建生产源码清单。实际日志、逐项符合性、算法说明、操作说明书草稿及信息采集表草稿位于 docs。

**Windows 证据：** 已在 Windows Server 2025 runner 实际运行 WPF 窗口，51 项窗口检查通过、绑定错误为空，取得18张真实WPF客户端截图；自包含 `.exe` 直接启动并以0退出。源提交、时间、截图方法和测试边界见 `docs/screenshots/windows-ui-evidence.json`、`published-exe-evidence.json`；发布包由通过启动检查的同一Windows输出打包，并核对归档内exe哈希。

**剩余限制：** 窗口自动化使用可控 `IUserDialogs` 路径和确认，系统文件对话框的鼠标/键盘操作、所有快捷键、多DPI、Windows10/11硬件兼容性尚未覆盖。手册已嵌入18张真实截图、图号及图注；Microsoft Word COM不可用（REGDB_E_CLASSNOTREG），Word实际不少于10页及逐页排版尚未验收，不能认定软著终稿已完成。未获专用模板；权属、申请人、开发及发表日期待核实。详见 `docs/验收与限制.md`。
