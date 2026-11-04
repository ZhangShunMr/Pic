# 车内低频声学模态与座位声压分布设计软件 V1.0

C# / .NET 10 / Blazor Server / MVVM。本机浏览器界面，以座舱剖面为中心，上下连续呈现频响、模态、贡献、基准、扫描与复核清单。没有顶部模块导航。

## 启动

源码：安装.NET 10 SDK，打开 `CabinAcoustics.sln` 或在本目录运行 `run.bat`（Windows）、`./run.sh`（Linux/macOS）。浏览器打开 http://127.0.0.1:8786 。发布包：安装.NET 10 ASP.NET Core Runtime或SDK，解压后运行相同脚本，无需编译。只监听本机回环地址；关闭命令窗口停止服务。端口占用时可用 `dotnet Cabin.App.dll --port 8787 --data-directory ./Data` 指定其他端口。

普通启动为空白；点击“新建空白项目”建立无点位的模型，或选择独立模拟示例。修改输入后须重新“计算声场”。点击模态表查看振型，指定频率或点击曲线查看激励响应。点击“项目与成果 ↗”保存、打开和导出。

## 数据与边界

保存目录为启动脚本旁的Data；项目 `.cabin.json` 内嵌全部输入、基准、复核记录与历史，可搬迁重新导入，结果通过重算恢复。导出的研究报告HTML内嵌SVG曲线，可离线打开和打印。导出不替代项目保存；浏览器决定下载位置。

理想刚性矩形腔，所有尺寸与坐标为米。模态形状与声源激励频响分别显示；频响为含等效阻尼的非零阶截断模态复数叠加，固定100Hz标度的无量纲研究指标，不含000压缩模态，不等于Pa或dB SPL。响应场颜色按剖面最大值归一化，曲线共用耳点全频带参考。真实座舱需实测或高保真仿真核对。

## 验证与文档

`dotnet build CabinAcoustics.sln`；`dotnet run --project tests/Cabin.Verification -- docs/numerical-evidence.json examples`。浏览器验证需本机服务运行及Python Playwright，执行`python tools/browser_verify.py`。测试、真实截图、操作手册、软著采集表、源码清单和限制见 [docs/交付索引.md](docs/交付索引.md)。

未提供申请人的专用申报模板。手册沿用仓库A4基本版式；申请人、权属和日期待核实。Microsoft Word实际分页尚需在Word中确认，浏览器PDF预览不冒充Word分页验收。
