# 汽车附件电机电磁激励频率设计软件 V2.0

Windows 原生 C# WPF 桌面软件，采用 MVVM，真实解决方案入口 [MotorFrequency.sln](MotorFrequency.sln)。原生窗口、Windows 文件对话框与 WPF 绘图；不包含 WebView、ASP.NET、本地HTTP服务或浏览器承载界面。HTML仅用于导出的静态报告。

## 直接运行（离线）

下载本分支 `release/MotorFrequency-V2.0-win-x64.zip`，将整个压缩包解压到可读写的本机文件夹，双击 `MotorFrequency.exe`。不要只复制单个 EXE；运行时及WPF依赖都在同一目录。目标 Windows 10/11 x64，无需预装.NET、Node或Python，运行不需联网。Windows可能对下载文件显示来源提示；先核对本分支发布清单和SHA-256。本分支离线ZIP已替换为运行41871662953实际产生并启动的Windows自包含构建，EXE及三个程序集字节哈希已核对一致，详见release/本地复核记录.json。Windows Server 2025 CI已实际启动自包含程序并正常关闭，记录见[Windows运行证据](docs/windows-evidence/published-exe.json)。发布清单中`windows_execution_verified`只有在CI核对该包EXE与三个程序集字节哈希后才为true；不能把源代码级验证混同为不同二进制包的验证。Windows10/11实机、网络隔离和高DPI仍待补验。

## 构建与调试

安装 .NET 10 SDK（global.json允许10.0.100及后续10.0功能版本），在此目录运行：

```powershell
dotnet build MotorFrequency.sln -c Release
dotnet run --project src/MotorFrequency.Desktop -c Release
```

第二条只在Windows运行。如需从源码调试，请下载本分支完整源码。也可用支持.NET 10的Visual Studio打开`.sln`，将MotorFrequency.Desktop设为启动项目。首次构建需要下载WindowsDesktop目标包；发布完成后运行不需要网络。没有第三方生产NuGet依赖。

离线发布：`powershell -ExecutionPolicy Bypass -File tools/publish-windows.ps1`（仅执行本仓库已审阅脚本），或：

```powershell
dotnet publish src/MotorFrequency.Desktop -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Linux可执行同一build/publish命令完成Windows目标交叉编译；不能在Linux运行WPF。

## 完整工作流

1. 普通启动为空白。文件→新建空白项目，进入左侧“项目档案”填写名称、目的与可选负责人。示例仅由“独立模拟示例”入口载入，并持续显示模拟标记。
2. “电机与转速”选择永磁无刷同步或永磁有刷。无刷使用p与Q，有刷使用K；不适用拓扑的参数保留合法档案值。设置扫描区间、步长、观察转速及图表上限，点击右侧“应用条件”。输入错误显示字段原因，上一组有效结果保留。
3. “候选依据”查看公式和依据，切换候选参与状态，添加有依据的自定义机械阶次。启用理想齿槽候选必须确认结构假设并填依据。图中实线为当前候选、虚线为选定快照、琥珀色为频带、等大小圆点为频谱内部峰；点击横向位置移动观察转速。频率图不是声压预测。
4. “敏感频带”添加频带和来源，检查进入/离开转速区间。可选择交叠行，在区间中点观察。没有幅值与阻尼模型，交叠不证明共振。
5. “频谱复核”读取或粘贴UTF-8 CSV，补齐转速、单位、参考量、采集工况和来源；先预检，再确认导入。导入后原始点内嵌保存。改动内容或元信息会使预检失效；错误整批拒绝且指出行号。每条频谱单独显示幅值，不混合不同单位。
6. “参数研究”枚举p/Q或K，反算目标频率与容差对应转速。结果有明确条件，不作可制造性或优劣排名；它是会话工具，需单独导出，重开项目后重新运行。改动条件后旧结果不能导出。
7. “方案快照”记录当前已应用参数，修改并应用当前条件后，选快照统一在当前观察转速比较，差值为当前−快照；缺项为空，拓扑或扫描范围不同显示限制。快照保存自己的候选副本。
8. 文件→保存/另存为，选择本机`.motorproj`路径；再次保存保留上一版`.bak`。从打开对话框选择`.motorproj.bak`会作为新标识副本恢复。文件被外部修改、移动或删除时拒绝覆盖。项目可直接搬迁重开。
9. “成果输出”选择CSV、SVG、静态HTML报告或JSON清单，并选择保存位置；成功后显示真实写入路径，可在资源管理器定位。原始点CSV需与项目或报告配套保留元信息。未完成新增表单时先完成添加/导入或明确重置；保存不会默默丢弃。
10. “操作记录”支持30步会话撤销重做、归档/取消归档。归档暂停内容编辑，仍可保存、导出与复制。退出或替换未保存项目有保存、放弃与取消选择；强制结束进程仍会丢失未保存工作。

## 手算核对与输入约定

1800 r/min、p=2、Q=12：转频30 Hz、电频60 Hz、槽通过360 Hz。理想假设成立时LCM(12,4)=12阶，对应360 Hz。同频的槽通过与齿槽候选分别保留。p=5时LCM(12,10)=60阶，对应1800 Hz。有刷K=10时，单刷片通过300 Hz，不乘电刷数。

55～65 Hz频带对应两极对电频交叠转速1650～1950 r/min；目标60 Hz、容差3 Hz反算得到1800及1710～1890 r/min。电气周期、实际电磁力时间/空间阶次与根因不同，邻近不能证明因果。模型不支持异步滑差、自动PWM边带、力或声压幅值预测。

CSV表头严格`frequency_hz,amplitude`，英文逗号、小数点，UTF-8可BOM，2～10000点、≤1MiB、频率严格递增。项目≤8MiB、≤12条谱/30000总点、≤40自定义候选、30频带、12快照、2000历史。更多边界见[算法与数据格式](docs/算法与数据格式.md)。旧格式1的已知字段可读取，V2.0继续使用formatVersion=1；未知、缺失、重复字段、无效引用及未来格式拒绝，不静默补默认或迁移。保存修改后softwareVersion更新为V2.0。

## 验证与材料状态

```powershell
dotnet run --project tests/MotorFrequency.Verification -c Release
dotnet run --project tests/MotorFrequency.Application.Verification -c Release
powershell -File tools/verify-windows.ps1
python tools/source_inventory.py
python tools/build_materials.py
```

最后两个Python脚本用于交付材料，不是生产运行依赖；文档生成需python-docx。Windows验证脚本必须在真实Windows交互桌面执行，会加载WPF界面、验证部分绑定并保存实时窗口渲染截图，另有原生保存、打开和导出对话框成功补测；字体、高DPI和其余人工操作路径仍待验收。

[交付索引](docs/交付索引.md)汇总证据和未验收项。已在Linux和Windows Server 2025完成构建及80项核心/MVVM检查，并在Windows实际运行WPF窗口、检查绑定、生成12张原生客户端截图、启动并正常关闭自包含EXE。截图已逐张检查并嵌入手册，原始字节与来源提交可追溯。最新运行41871662953已通过真实原生保存、打开和报告导出对话框补测，重开恢复751个频点；独立记录及三张对话框截图见docs/windows-dialog-evidence。手册共附15张实际截图（12张WPF客户端、3张系统文件对话框）。**Windows10/11目标设备、高DPI、网络隔离与最大负载性能仍待补验**。操作说明书及采集表仍为草稿：runner没有注册Microsoft Word COM；LibreOffice预览仅供排版检查，不能代替Word实际页数和逐页终稿验收。申请人权属、身份、开发及发表日期保留待核实。未宣称真实车辆试验效果。
