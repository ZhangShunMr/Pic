# 汽车附件电机电磁激励频率设计软件 V1.0

独立 C#/.NET 10 本地软件，MVVM，浏览器承载 Blazor Server 界面。以电机剖面对象和条件检查器为主，左侧项目目录、纵向工作簿；没有顶部模块按钮切换。新增文件均位于本目录，基于 `codex/cloud-dev-20261102`，不修改 main 或其他软件。

## 运行

安装 .NET SDK 10.0.100 或同版本带允许的后续 SDK。在本目录运行：

```sh
dotnet build MotorFrequency.sln
dotnet run --project src/MotorFrequency.App
```

打开 `http://127.0.0.1:8774`。Linux/macOS 可执行 `./run.sh`；Windows 可执行 `run.bat`。Visual Studio 打开 `MotorFrequency.sln`，将 MotorFrequency.App 设为启动项目，需支持 .NET 10 的 Visual Studio。云环境已经实际构建启动，未在 Windows Visual Studio 按 F5 验证。

端口与存储目录：`dotnet run --project src/MotorFrequency.App -- --port 8775 --data-directory /your/writable/path`。默认目录是应用内容根目录下 `Data`。仅监听本机回环地址，无账号、不用于远程多人部署。生产无第三方 NuGet 依赖。

## 完整操作

1. 普通启动为空白。新建项目，在电机图旁选择“项目档案”，填写名称、负责人和目的并应用。独立模拟示例不会自动覆盖未保存工作。
2. 选择转子配置电机拓扑、极对数；无刷选择槽对象配置槽数。有刷使用换向片数，软件不为有刷自动生成同步电气基频。选择转速与范围设置扫描和观察转速。字段是草稿，应用后才计算。
3. 理想齿槽候选需确认理想对称假设并填写依据。可添加带依据的任意机械阶次，勾选决定是否参与绘图、交叠和匹配。
4. 添加敏感频段及来源，核对解析交叠区间，点击区间中点观察。频率—转速图包含候选线、敏感带、定转速频谱峰点和选中快照虚线。
5. 导入 UTF-8 两列 CSV，填写转速、单位、参考量、工况和来源，预检后确认导入。错误整批拒绝。每条频谱单独绘制幅值，避免混用不同单位；内部谱峰与全部邻近候选保留。
6. 记录当前方案快照，修改参数后统一在当前观察转速比较。参数扫查枚举给定整数，反算观察频率对应的候选转速；扫查结果为会话工具，需另行导出留存。
7. 保存到项目库，确认实际路径；从项目库重开。再次保存保留 `.bak`，读取备份会建立副本。下载 `.motorproj` 可搬迁导入；相同标识导入不隐式覆盖已有磁盘项目。
8. 导出扫描/交叠/谱峰/比较 CSV、频率图 SVG、自包含 HTML 报告、原始点或输入核对 JSON。CSV 文本字段转义公式起始字符。下载目标为浏览器下载目录，成功消息表示已发起下载，需核实实际文件。

## 计算边界

机械转频 n/60；无刷同步电气基频 p×n/60；槽通过 Q×n/60 是相对运动参考；理想齿槽 LCM(Q,2p)×n/60 须限定对称结构。有刷只给 K×n/60 的单刷片通过参考，默认不乘电刷数。自定义阶次 f=k×n/60。

电气周期、实际电磁力空间/时间阶次、齿槽周期和片通过不是同一个量。软件不能仅凭 p/Q/K 指认声压主峰，也不估算电磁力或噪声幅值。邻近是绝对频差容差内的频率关系，不等于因果。敏感频段交叠不证明共振。示例为固定模拟公式，不能证明真实车辆效果。

## 验证与材料

```sh
dotnet run --project tests/MotorFrequency.Verification
python3 tools/source_inventory.py
python3 tools/browser_verify.py
python3 tools/build_materials.py
```

开发工具依赖：Python 的 playwright、python-docx、PyMuPDF、Pillow，以及 Chromium、LibreOffice；它们不是软件生产依赖。浏览器脚本需先启动应用，使用单独的临时数据目录。

[交付索引](docs/交付索引.md)列出材料、证据与限制。DOCX 操作手册、采集表、源码排印稿已经生成，截图来自本次实际 Chromium。PDF 是 LibreOffice 预览，不能替代规范要求的 Microsoft Word 实际分页验收；用户专用模板、Word 终稿和申请人权属/开发/发表事实仍待核实。不把预览写成登记终稿。
