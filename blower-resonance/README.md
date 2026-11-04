# 空调鼓风机风量挡位与共振避让设计软件 V1.0

C# / .NET 10 / Blazor Server / MVVM。独立项目，未改动仓库已有软件或规范。解决方案入口：[BlowerResonance.sln](BlowerResonance.sln)。

安装.NET SDK 10.0.403（允许同10.0最新补丁），在本目录执行：

```sh
dotnet build BlowerResonance.sln
./run.sh
# Windows: run.bat
```

浏览器打开 http://127.0.0.1:5178 。服务必须保持运行。无外部CDN和数据库依赖；首次构建会还原微软框架静态资源包。Visual Studio可打开.sln，将Blower.App设为启动项目；Visual Studio图形启动和Windows未在本次Linux环境执行。

界面为常驻对象树、转速—频率图、选中对象属性、可展开结果区。顶部仅软件身份与工作操作，没有模块切换。普通启动空白；在左侧“项目文件与成果”载入模拟示例。

典型操作：

1. 选中项目，填写来源等属性并应用。
2. 添加阶次、模态、方案和挡位，每次编辑后先应用。
3. 图形自动计算；点击挡位或模态直接编辑，底部核对交叉、明细和依据。
4. 在“约束扫描”搜索并采纳为独立候选，选择另一方案比较。
5. 从潜在接近组合生成验证清单，填写方法、负责人、证据和状态。
6. 保存项目，从项目库重开；在“导入与成果输出”生成下载并点击下载链接。

默认项目库位于 `src/Blower.App/.project-library`。可用环境变量 `BLOWER_PROJECT_ROOT` 指定目录。项目保存为schema1的`.blowerproj`，全部输入内嵌；每次覆盖保留`.bak`，存在并发修改时拒绝覆盖。浏览器只保存会话，不是自动持久保存；离开前应用属性并保存。每个浏览器会话独立，服务端项目库共享，适用于可信本机工程环境。默认启动仅绑定回环地址；不要对外开放未配置认证的服务。

示例为可重复模拟设计，不是试验数据。40叶片、600Hz模态、40阶在900rpm精确交叉。默认半宽18Hz、转速容差±20rpm时敏感转速区853～947rpm。完整项目、报告和CSV见[examples](examples)。

验证命令：

```sh
dotnet run --project tests/Blower.Verification -- docs/numerical-evidence.json
# 保持服务运行；另需Python及playwright，系统chromium
python3 tools/browser_verify.py
python3 tools/source_inventory.py
python3 tools/build_materials.py
```

操作说明和软著材料见[docs/交付索引.md](docs/交付索引.md)，计算与输入约定见[docs/算法与文件格式.md](docs/算法与文件格式.md)。测试、工具、材料均未计入生产源码行数。

适用边界：软件仅核对设计频率位置，既不读取波形，也不预测声压、响应幅值、风量或效率。接近标记不是共振确认，扫描结果不是降噪保证；需要台架和整车验证。申报身份、权属、开发完成与发表日期待申请人核实。Microsoft Word实际分页待申请人复核；本次仅能提供其他渲染器的分页证据。
