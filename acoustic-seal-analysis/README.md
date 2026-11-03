# 车身声学泄漏路径与封堵试验分析软件 V2.0

本版本按分支中《软件设计准则.md》《软著编写要求.md》（用户提交dbd4c84）整体重建。生产软件为C#/.NET10，入口为 **AcousticSeal.sln**，采用MVVM。旧Python服务、JavaScript业务代码、截图、验证记录和软著稿已由本版本替换，历史版本仍可通过Git查看。

## 运行与解决方案

安装.NET SDK **10.0.403**（global.json允许同版本带的后续补丁）。生产代码无第三方NuGet依赖，只用.NET与ASP.NET Core共享框架。

```sh
dotnet build AcousticSeal.sln
dotnet run --project src/AcousticSeal.App
```

浏览器打开 `http://127.0.0.1:8765`。端口占用：`dotnet run --project src/AcousticSeal.App -- --port 8766`。数据目录默认 `src/AcousticSeal.App/Data`，可指定 `--data-directory /your/writable/directory`。Linux/macOS可用`./run.sh`，Windows可用`run.bat`。

在支持.NET10的Visual Studio（例如Visual Studio 2026）中打开AcousticSeal.sln，将AcousticSeal.App设为启动项目后运行；launchSettings提供本机浏览器启动配置。云端已实际构建.sln并通过命令启动，未在Windows Visual Studio图形环境按F5，因此该操作的GUI验证仍待执行。.NET10与Visual Studio版本对应关系参见[Microsoft安装说明](https://learn.microsoft.com/en-us/dotnet/core/install/windows)。Blazor Server是C#本地应用，浏览器为界面宿主，不是WPF桌面程序。

## 完整工作流程

1. 普通启动为空白；填写项目信息与试验目标并应用。
2. 定义封堵位置、材料、覆盖和恢复工艺；记录完整工况和计权、标定、单位。
3. 建立基线、单项、组合、恢复试验，逐频带编辑或粘贴，保留来源。CSV支持预检、合并/替换和错误整批取消。
4. 用户显式建立配对，或选择一个基线并勾选目标后逐行确认。不可比条件/来源阻断差值并保存原因。
5. 比较页切换差值图与原始频谱，扫描频率范围，查看单项/组合矩阵及重复配对统计；质量页检查重复性、漂移、恢复、覆盖、原始核对表和工程规则情景。
6. 点击“保存项目”实际写入本机.sealproj；覆盖前保留.bak，磁盘外部变化阻止覆盖。项目库可打开及载入备份。当前会话支持20步撤销/重做，累计审计保留。
7. 保存后从项目库重开；也可下载并搬迁.sealproj，再用文件入口导入。导入旧版.aslp.json会明确迁移，原文件不改。同标识导入不直接覆盖项目库文件，需要先打开磁盘版续改或复制新项目。
8. 记录复核快照，导出自包含HTML、结果CSV、原始频带CSV或分析JSON。结果带版本、单位、来源、参数、对象标识及输入哈希。项目版本核对支持按GUID/频率比较两份项目输入，保持工作项目不变。

## 计算与数据边界

结果为封堵前后测得声级差，不是路径独立能量贡献；单项分贝不可相加预测组合。缺失不补零。配对差均值与组能量均值差分别列出；n=1和试验复用时不生成区间。频带合成仅限用户确认互不重叠、频带集合相同且无缺失的数据，不能冒充全频总声级。默认容差和筛查阈值是工程初值，非标准限值。规则情景只重新判读同一输入，不改变测量，不推荐放宽参数。

全部演示数据来自`src/AcousticSeal.Core/Domain/Demonstration.cs`固定模拟数列，非实测。软件不读取音频、不再次加权、不认证标定、标准符合性或登记事实。来源分类含模拟、实测（用户声明）、人工与估算。图片/音频仅文字引用，实际频带内嵌于项目。

## 验证、材料与已知限制

```sh
dotnet run --project tests/AcousticSeal.Verification
python tools/source_inventory.py
```

核心验证工程使用C#，无测试框架NuGet依赖。真实浏览器检查与材料工具的可选依赖见requirements-dev.txt；它们不是生产软件。验证证据和逐条符合性记录见[交付索引](docs/交付索引.md)。

Word实际分页、用户专用模板、申请人及权属/开发/发表事实尚待核实；浏览器PDF预览不能替代Word不少于10页的终稿验收。未验证Windows/macOS实际启动、真实车辆效果、最大输入规模性能或多人远程部署。仅监听本机，不提供账号或远程协作。未保存工作副本随刷新/关闭或服务会话丢失；保存和下载后应确认实际文件。
