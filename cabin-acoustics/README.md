# 车内低频声学模态与座位声压分布设计软件 V1.0

**C# / WPF / MVVM，Windows 原生桌面程序。** 真实解决方案入口：[CabinAcoustics.sln](CabinAcoustics.sln)。没有 HTTP 服务、浏览器、WebView、Razor 或本地端口。

理想矩形刚性腔的低频研究工具。输入矩形尺寸、声速、等效阻尼、相干点声源与座位耳点，查看模态、空间剖面、相对频响、复数贡献、基准比较、参数扫描和截断敏感性。**相对响应不是Pa或dB SPL，不代表实车绝对声压或真实降噪效果。**

已在远端Windows Server 2025完成实际WPF窗口检查（51/51检查，18张实际原生截图），并直接启动自包含exe正常退出。核心计算与共享MVVM测试70/70通过。下载[Windows自包含运行包](https://github.com/ZhangShunMr/Pic/actions/runs/41858289334/artifacts/13337359397)，证据见[成功工作流](https://github.com/ZhangShunMr/Pic/actions/runs/41858289334)。下载解压后，在artifacts/win-x64目录双击exe。GitHub artifact有保留期限，过期可从源码重建。

## Windows 运行

运行平台：Windows 10/11 x64。离线自包含发布文件夹中双击 `CabinAcoustics.exe`；复制整个文件夹即可运行，无需安装 .NET、联网、启动服务器或打开浏览器。普通启动为空白。通过文件菜单“新建空白项目”或“载入独立模拟示例…”开始。

源码开发：安装 .NET 8 SDK，Visual Studio 2022 的“.NET桌面开发”工作负载；打开 `.sln`，将 `Cabin.Desktop` 设为启动项目。也可在本目录双击 `run.bat`，或执行：

```powershell
dotnet build CabinAcoustics.sln -c Release
dotnet run --project src/Cabin.Desktop -c Release
```

Windows 发布与验证：

```powershell
./tools/verify-windows.ps1
./tools/publish-windows.ps1
```

`artifacts/win-x64/` 是自包含输出目录，包含 `.exe` 和全部运行依赖。首次构建及发布需要联网恢复微软开发包，发布完成后的程序运行离线。脚本不替代真实运行检查；请阅读实际验证记录。[Windows工作流](../.github/workflows/cabin-native.yml)在本软件分支推送后尝试实际WPF检查，结果以工作流日志和截图证据为准。

## 从输入到成果

1. 文件菜单新建项目，填写档案及右侧模型属性。默认几何是起始输入，不是实测数据。
2. 左侧工程树进入“声源与耳点”，添加至少一个启用非零声源和一个耳点。坐标单位m，包含壁面边界。
3. 点击“校验输入”，再“计算相对声场”（F5）。输入改变立即清空旧结果。
4. 在“空间剖面”设置方向、切面、研究频率及显示量，点击“更新剖面与贡献”。在其他工作区查看座位频响、模态、耦合及数值提示。
5. 捕获基准后修改、重算、比较。扫描保持绝对坐标，无效候选逐行显示错误；应用候选后必须重算。
6. Ctrl+S保存 `.cabin.json`，可搬迁、重开并复算。文件内嵌模型、声源、耳点、基准、复核与保存历史。
7. 导出菜单输出UTF-8 CSV、离线文本研究报告及当前WPF声场画布PNG。结果导出不代替项目保存。

## 数据与边界

项目格式版本1，最大2MiB；结构错误、未知字段、不支持版本与越界参数均拒绝。最多8个声源、12个耳点、12个基准、100项复核、500条历史。点位CSV字段为 `kind,name,x_m,y_m,z_m,amplitude,phase_deg,note`；小数点，UTF-8，逗号分隔，名称/备注不支持换行。CSV不保存声源启停及耳点标识，导入全部启用并生成新标识，需人工核对，完整往返请使用项目文件。

坐标原点前左下角；模型使用余弦基底，000模态排除。复数响应采用100Hz/1m³固定参考标度和统一模态阻尼；空间响应局部归一化，座位曲线共用全频带最大幅值参考。采样RMS不是时间域声压有效值。近零比值有−120dB显示下限。真实内饰、柔性板、开口、复杂几何和校准不在模型范围。

## 验证与材料

Linux可执行核心和共享MVVM测试，可交叉编译WPF，不能在Linux运行WPF：

```sh
dotnet build CabinAcoustics.sln -c Release
dotnet run --project tests/Cabin.Tests -c Release -- docs/test-evidence.json examples
```

共享MVVM测试使用仅位于测试工程的 `CommandManager` 替身，不代表WPF控件、绑定、对话框或图像已验证。Windows检查程序显示真实WPF窗口，捕获原生渲染图，并检查绑定错误及业务流程。截图必须逐图人工检查后才可用于正式手册。

[验证记录](docs/验证记录.md)、[算法与文件格式](docs/算法与数据格式.md)、[操作说明书草稿](docs/操作说明书.md)、[软著信息采集表](docs/软著信息采集表.md)、[要求符合性矩阵](docs/要求符合性矩阵.md)、[交付索引](docs/交付索引.md)。材料中所有未验证事项明确标记，未提供专用申请模板，申请人/权属/日期待核实。Microsoft Word实际分页验收不能由手动分页符或非Word预览替代。
