# 汽车内饰卡扣与防异响接触设计软件 V2.0

C# WPF Windows 原生桌面软件，采用 MVVM、真实 `TrimContact.sln`。左侧任务目录、布置示意图和原生表格共同完成设计；不启动 HTTP 服务、不使用浏览器界面。HTML 只作为成果报告格式。

本软件布置刚性饰板、双向卡扣和单边缓冲接触，输入公差、预紧、刚度、摩擦与准静态惯性载荷，比较接触分离、滑移余量和用户限值；不预测真实噪声。所有默认参数都是待标定的工程初值。

## 离线运行

从本分支 `dist/TrimContact-V2.0-win-x64.zip` 解压完整目录，Windows 10/11 x64 双击 `TrimContact.Desktop.exe`。包自带 .NET，不需安装SDK或运行时，运行时无网络请求；单文件首次启动将原生库解压到用户临时目录，需有写入权限。发布包未签名。Windows ARM64、32位系统不在本包支持范围。

**已在 Windows Server 2025 runner实际运行原生WPF窗口和自包含EXE。** 真实截图、窗口检查、正常退出及构建证据在 `docs/windows/`；检查覆盖输入绑定、真实按钮命令、绘图、求解、扫描、保存重开和最小窗口。自动流程的文件/确认接口使用替身，Windows10/11、高DPI与真实系统文件对话框仍待人工复核，见 `docs/Windows实机验收.md`。操作手册已使用真实截图，Microsoft Word未安装，Word实际分页仍待核验，尚不能作为申报终稿。

运行ZIP包含程序、模拟文件、手册及Windows运行证据；开发与重建请下载本分支完整源码，打开下述解决方案。

## 构建与开发

安装 .NET SDK **10.0.403**（global.json允许同补丁系列后续修补），Windows 使用 Visual Studio 或其他支持 .NET10/WPF 的开发工具打开 `TrimContact.sln`，把 `TrimContact.Desktop` 设为启动项目。生产无第三方 NuGet 代码依赖，WindowsDesktop 框架来自 .NET。

```powershell
dotnet build TrimContact.sln -c Release
dotnet run --project src/TrimContact.Desktop -c Release
# 或运行 .\run.bat
# 完整构建、测试及离线发布
.\build-windows.ps1
```

首次构建/发布需要联网还原官方框架包；发布完成后启动不需要网络。Linux 可交叉编译与运行核心/VM测试，不能运行 WPF：

```sh
dotnet build TrimContact.sln -c Release
dotnet run --project tests/TrimContact.Verification -c Release -- .
dotnet run --project tests/TrimContact.DesktopWorkflow -c Release -- .
python3 tools/source_inventory.py
```

## 完整流程

1. 普通启动空白；填写项目身份、来源、质量与质心，建立矩形或3–24顶点轮廓。模拟示例只从文件菜单“载入独立模拟示例…”进入。
2. 点位布置中新建方案，点击图设置新增坐标，添加至少3个非共线卡扣和所需接触点，编辑公差、预紧、刚度、摩擦和限值；可复制方案独立修改。
3. 定义共用载荷与24/48/72相位采样、公差角点，应用草稿后求解全部方案。计算可取消，失败不保留部分结果。
4. 阅读包络、名义公差曲线、独立反力/力矩核查和工程复核。比较同次结果，单接触点扫描不改变原输入。输入变更后旧结果不能导出。
5. CSV布置交换和批量修改必须先预检/预览再确认应用；变化后旧预览会阻止应用；应用后可撤销重做。由结果生成验证任务，完成需填写证据。
6. 原生文件对话框保存 `.trimproj`，另存可搬迁。项目内嵌模型、来源和任务，重开后重新计算。可选本机项目库存储，位置见项目页；库条目可从 `.bak` 复制为新项目恢复。
7. 成果菜单导出 HTML、CSV、分析JSON。结果包含来源、方法和输入SHA256；扫描保留对象、参数及基准哈希。任务证据是文字编号，不会自动复制外部附件。

覆盖保留 `.bak`，打开后的文件被外部修改会阻止覆盖。新建、打开和退出会处理未保存内容；归档项目只读，复制后继续。项目schema=1，兼容备份版合法 `.trimproj` 输入；不兼容损坏、缺字段或未知版本，不静默填值。

## 交付与限制

- 核心测试和桌面ViewModel测试的真实执行结果分别保存在 `docs/core-verification.json` 与 `docs/desktop-workflow-verification.json`。VM测试使用文件对话替身；另有 `docs/windows/windows-ui-evidence.json` 记录真实Windows视觉树、绑定与渲染检查，`published-exe-evidence.json` 记录直接启动和正常退出。
- `docs/build-log.txt`、`docs/publish-log.txt` 是构建和发布记录；`docs/source-inventory.json` 给出实际生产源码非空行与SHA256。
- `docs/操作说明书-待Windows核验.docx`、`docs/软著信息采集表-草稿.docx`、`docs/完整生产源码打印稿.docx` 及对应文字材料与当前功能对照。未收到专用申报模板，沿用旧版A4页幅与采集表字段顺序；权属、日期与申报范围须申请人核实。LibreOffice逐页PDF预览在 `docs/preview`，不代替Microsoft Word分页。
- 刚性平面、线性弹簧、准静态采样和单方向切向模型不覆盖板柔性、阻尼、共振、冲击、摩擦记忆或声学辐射。公差角点计数不是概率，用户限值不等于行业标准；模拟案例不能证明实车效果。
