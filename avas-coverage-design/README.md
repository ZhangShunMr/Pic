# 车外行人警示声音传播与覆盖设计软件 V2.0

C# / WPF / MVVM 的 Windows 原生桌面程序。入口 **AvasCoverage.sln**，启动项目 **Avas.Desktop**。软件不启动 HTTP 服务，不使用浏览器、WebView、Blazor 或 Electron。生产代码只依赖 .NET 桌面框架。

普通启动为空白项目。独立模拟示例入口在“文件→载入模拟示例”。左侧选择工作区，中央持续显示原生绘制的声场和点结果，右侧编辑输入。八个工作区分别处理项目、扬声器、背景与网格、观察点、方案比较、扫描与剖面、音频试听及成果与复核。

## Windows 运行

支持能运行 .NET 10 WPF 的 Windows 10/11 x64。开发环境推荐安装 .NET SDK 10.0.403 及支持 .NET 10 的 Visual Studio，打开 `.sln` 将 `Avas.Desktop` 设为启动项目，按 F5。也可双击 `run.cmd`（需要 SDK）：

```powershell
dotnet run --project src/Avas.Desktop -c Release
```

自包含包生成后，解压整个目录，双击 `AvasCoverage.exe`；不要求目标机器安装 SDK 或运行时，运行不需要网络。发布命令：

```powershell
powershell -ExecutionPolicy Bypass -File tools/build-windows.ps1
```

输出 `artifacts/AvasCoverage-win-x64/` 和相应 ZIP。生产程序无需管理员权限，项目库存于 `%LOCALAPPDATA%\AvasCoverage\Projects`；制作试听时临时 WAV 位于当前用户临时目录，关闭时清理。

## 一次完整操作

1. 在“项目”填写名称、负责人、车辆和目的，点击“应用编辑”。
2. 在“扬声器”添加声源，输入三维位置、朝向、轴向参考距离、七带参考声压级、频响修正和来源，应用。最多8个声源。可导入七频带 CSV，必须先预检再整体应用。
3. 在“背景与网格”填写背景和来源、接收高度、区域、步长、车辆轮廓及工程目标，应用。最多20000个网格节点。
4. 在“观察点”添加或导入最多100点，支持等角环形点集；应用并计算。点表、原生地图、逐带频谱与源贡献可联动核对。
5. 捕获独立方案快照，在相同条件下比较。扫描朝向、增益或坐标不会自动更改项目，选行后才可应用；方向剖面可独立试算。
6. 可导入PCM16 WAV分析数字频谱，或制作观察点相对试听。导入音频不改变物理声级；以人工参考映射频带时来源改为“估算”。
7. “保存到项目库”保存已应用输入，重复保存产生 `.bak`，检测外部修改冲突。库中选择条目可打开；完整 `.avasproj` 可导出、搬迁后导入重开。结果重开后重新计算。最多10步撤销重做，累计审计最多1000条。
8. 导出自包含HTML工程报告、网格/观察点/比较/扫描/剖面CSV、SVG或JSON。原生保存对话框选择路径；成功后显示实际位置。HTML只是静态输出文件。

格式错误会显示红框；应用时再做业务范围校验。未应用草稿或格式错误会阻断计算、保存、切换对象和工作区。应用后相关结果过期，输出须重新计算。关闭、替换、删除和归档提供确认；归档项目只读，可解除。

## 可核对案例与边界

全向声源位于 `(0,0,1.5)m`，参考距离1m，各带60dB，增益与修正0；接收高度1.5m，观察点 `(2,0)m`，显示1000Hz。预期警示声级 **53.9794dB**；背景35dB时信号背景差 **18.9794dB**。两个同位置源按非相干能量叠加增大3.0103dB。

每带公式 `L=Lref+correction+gain−20log10(max(r,rmin)/r0)+20log10(D)`，`D=floor+(1−floor)*max(0,cosθ)^n`，n=0时全向。多源逐带按非相干能量叠加。七带中心125～8000Hz，A计权只是中心频率近似；背景均匀。覆盖率为有效网格节点满足自定义声级与信号背景差两项目标的比例。默认45dB/3dB为工程示例，不是法规阈值。

未模拟车身遮挡、反射、衍射、地面、气象、相干干涉。轮廓只示意和排除内部网格，截断距离不是近场解。**不提供法规合规、实车声压或真实可听性结论**。数字音频无声压标定链路；试听为24kHz单声道七带相对整形，固定数字参考、50ms淡入淡出，峰值超0.98时拒绝，无自动归一化。设备音量、实际听感和实车效果均未验证。

## 验证与材料状态

```powershell
dotnet build AvasCoverage.sln -c Release
dotnet run --project tests/Avas.Verification -c Release -- docs/core-verification.json
dotnet run --project tests/Avas.WorkspaceVerification -c Release -- docs/workspace-verification.json
# 仅 Windows：真实 WPF 窗口、绑定及客户端区域截图
dotnet run --project tests/Avas.DesktopVerification -c Release -- docs/windows-ui
```

当前在 Debian Linux、.NET SDK10.0.403 完成交叉编译；66项内核测试和18项命令层测试通过。Windows界面检查器已编译，尚未执行。无真实WPF截图、未验证原生文件选择器/声卡播放/缩放/键盘和辅助功能。未声称界面验收通过。详细状态见 `docs/验证记录.md`。

`docs/materials/` 提供操作说明书草稿、信息采集表、全部生产源码排印稿和源码清单。主要功能汉字计数有机器证据。当前缺Windows真实截图和Microsoft Word实际分页检查，手册不标终稿、不声称满足10页终稿门槛。无专用模板，先采用通用A4；申请人、权属、开发及发表日期均待本人核实。

两份原始规范在 `docs/specifications/`，完整保留备份原文。仅本项目路径在新分支新增；main和其他软件没有修改。旧功能对照、工程结构、统计口径与交接见 `docs/原生重做方案.md`。
