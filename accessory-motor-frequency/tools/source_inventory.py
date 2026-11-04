from pathlib import Path
import hashlib
import json

root = Path(__file__).resolve().parents[1]
roles = {
    'Models.cs': '领域对象、参数、来源、候选与结果记录',
    'ChartGeometry.cs': '独立SVG导出与保极值的绘图点抽稀',
    'Demonstration.cs': '明确标记来源的独立模拟示例',
    'ExportService.cs': '可追溯CSV与静态HTML报告',
    'FrequencyEngine.cs': '候选周期、扫描、解析交叠、谱峰与比较',
    'Labels.cs': '拓扑来源名称与数值呈现',
    'ParameterSweep.cs': '整数参数枚举与转速反算',
    'ProjectCodec.cs': '严格项目编解码、克隆与文本哈希',
    'ProjectReview.cs': '输入证据与后续复核清单',
    'ProjectValidator.cs': '领域数值边界、来源和引用验证',
    'RequiredSchema.cs': '必填字段、空值与重复JSON字段检查',
    'SpectrumCsv.cs': '频谱全量预检、来源记录与原始点交换',
    'SpectrumStatistics.cs': '输入频率网格与幅值汇总',
    'DocumentFile.cs': '原生项目文件会话、冲突检测与备份写入',
    'EntryDrafts.cs': '新增和条件草稿的绑定通知',
    'MotorDraft.cs': '拓扑参数草稿与显式类型解析',
    'ObservableObject.cs': 'MVVM通知、命令与原生交互抽象',
    'WorkspaceViewModel.cs': '工作区状态、选择联动与有效结果读取',
    'WorkspaceCommands.cs': '项目生命周期、草稿应用、历史与未保存保护',
    'WorkspaceAnalysis.cs': '候选频带频谱业务、快照、扫查与导出命令',
    'App.xaml': '原生应用资源、样式与控件约定',
    'App.xaml.cs': 'WPF生命周期与未处理界面异常反馈',
    'MainWindow.xaml': '原生窗口菜单、纵向导航、工作区与条件检查器',
    'MainWindow.xaml.cs': '窗口初始化、关闭保护与本机帮助',
    'CandidateBrushConverter.cs': '候选图例与轨迹的稳定颜色映射',
    'NativeInteraction.cs': 'Windows原生文件选择、确认与成果定位',
    'PlotBase.cs': '原生图形坐标、文字、网格与空状态',
    'FrequencyPlot.cs': '候选频率、频带、快照及谱峰WPF绘图',
    'SpectrumPlot.cs': '单条原始频谱WPF绘图与内部峰标记',
}
view_roles = {
    'Overview': '项目档案与输入核对', 'Motor': '电机拓扑和转速输入',
    'Candidate': '候选依据、频率图和自定义阶次', 'Band': '频带来源与解析交叠',
    'Spectrum': '频谱导入、预检、口径与邻近核对', 'Sweep': '参数扫查和反算',
    'Compare': '方案快照与统一条件比较', 'Output': '原生文件保存和成果输出',
    'History': '变更记录、撤销重做与归档',
}
files = sorted(p for p in (root / 'src').rglob('*')
    if p.suffix in ('.cs', '.xaml') and not {'obj', 'bin'}.intersection(p.parts))
items = []
for path in files:
    text = path.read_text(encoding='utf-8')
    role = roles.get(path.name)
    if role is None:
        for view, description in view_roles.items():
            if path.name.startswith(view + 'View.'):
                role = description + ('视图初始化' if path.suffix == '.cs' else '原生控件绑定')
                break
    if role is None:
        raise RuntimeError(f'No role declared: {path}')
    items.append({'path': path.relative_to(root).as_posix(), 'purpose': role,
        'nonblank_lines': sum(bool(line.strip()) for line in text.splitlines()),
        'physical_lines': len(text.splitlines()), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
output = {'scope': 'Handwritten src/**/*.cs and src/**/*.xaml, nonblank lines; excludes tests, scripts, docs, obj/bin and dependencies',
    'old_baseline_nonblank_lines': 3661, 'target': 3500,
    'target_reason': 'Native platform replacement, not feature-expansion reconstruction; see docs/开发方案.md',
    'files': items, 'nonblank_lines': sum(x['nonblank_lines'] for x in items)}
(root / 'docs/source-inventory.json').write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding='utf-8')
lines = ['# 生产源码清单 V2.0', '', output['scope'], '',
    f"实际生产文件{len(items)}个，非空行{output['nonblank_lines']}；最低目标3500。旧基线3661，原生平台替换口径见开发方案。", '',
    '|文件|非空行|用途|', '|---|---:|---|']
lines += [f"|{i['path']}|{i['nonblank_lines']}|{i['purpose']}|" for i in items]
(root / 'docs/源码清单.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
if output['nonblank_lines'] < output['target']:
    raise RuntimeError('Production source target not met')
print(json.dumps({'files': len(items), 'nonblank_lines': output['nonblank_lines'], 'target': output['target']}))
