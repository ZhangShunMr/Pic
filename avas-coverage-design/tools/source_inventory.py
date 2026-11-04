"""Count handwritten production sources with the design-stage scope."""
import hashlib,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
rows=[]; listing=[]
purposes={
 'Project':'项目、声源、观察点、设置、音频与快照实体', 'Validation':'字段、范围、集合与引用边界验证', 'Demonstration':'固定模拟输入独立入口',
 'Acoustics':'声学能量、指向与距离公式及声场计算', 'Scenarios':'快照比较、条件阻断与参数扫描', 'ReviewEngine':'输入复核与方向剖面', 'ChartRenderer':'声场和频谱SVG输出',
 'ProjectCodec':'完整结构验证、音频与快照核对、序列化和输入哈希', 'ProjectStore':'本机项目库、原子保存、备份及冲突检查', 'SpectrumCsv':'频谱模板、逐行预检和CSV输出', 'ListenerCsv':'观察点CSV解析和交换', 'ResultExport':'带来源与参数的工程报告及结果文件',
 'WaveCodec':'PCM16 RIFF读写与音频元数据', 'SpectralAudio':'FFT、数字频谱、人工参考映射及七带整形', 'AuditionEngine':'观察点相对试听、背景混合与峰值保护',
 'WorkspaceViewModel':'可观察状态、草稿、命令边界、撤销重做和结果失效', 'ObjectCommands':'声源/点/快照管理、比较、扫描与计算命令', 'FileCommands':'保存重开、频谱/音频输入与成果导出命令', 'ReviewCommands':'输入复核与剖面操作状态', 'ListenerImportCommands':'批量观察点预检与确认应用',
 'DownloadVault':'有时效与容量限制的本机成果下载缓存', 'Program':'本机服务注册、回环绑定与启动', 'App':'浏览器文档根与资源入口', 'Routes':'Blazor页面路由', '_Imports':'视图命名空间约定', 'Workspace':'导航、项目状态与整体工作台', 'VmComponent':'视图模型通知订阅、确认和上传边界', 'EditActions':'编辑应用与放弃操作', 'OriginSelect':'来源类别选择',
 'ProjectPanel':'项目档案、文件载入和项目库视图', 'SpeakerPanel':'声源位置、指向与频谱编辑', 'SettingsPanel':'背景、接收网格和工程目标编辑', 'ImportPanel':'频谱CSV预检和应用', 'LayoutPanel':'布局预览与观察点编辑', 'ResultsPanel':'声场、覆盖、频谱与贡献详情', 'ComparisonPanel':'快照维护和条件一致的差值比较', 'ScanPanel':'单源参数扫描与取值应用', 'AudioPanel':'数字音频、相对映射和试听入口', 'DeliveryPanel':'成果输出与审计视图', 'HelpPanel':'与当前功能一致的使用帮助', 'ReviewPanel':'输入复核提示表', 'TransectPanel':'方向距离剖面设置与点值表', 'ListenerImportPanel':'观察点CSV批量交换视图', 'app':'响应布局、颜色图例与可读样式'
}
for file in sorted((root/'src').rglob('*')):
 if file.suffix not in {'.cs','.razor','.css','.js'} or {'obj','bin'} & set(file.parts): continue
 text=file.read_text(); lines=text.splitlines()
 rows.append({'file':str(file.relative_to(root)),'purpose':purposes[file.stem],'nonempty_lines':sum(bool(line.strip()) for line in lines),'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
 listing.extend([f'===== {file.relative_to(root)} =====',*lines,''])
result={'scope':'手写生产源码 .cs/.razor/.css/.js；排除bin/obj、测试、文档、工具、依赖及生成文件','baseline':0,'minimum':3500,'total':sum(r['nonempty_lines'] for r in rows),'files':rows}
(root/'docs/source-inventory.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
(root/'docs/生产源码.txt').write_text('\n'.join(listing))
(root/'docs/源码清单.md').write_text('# 生产源码清单\n\n统计口径：'+result['scope']+'。基线0，最低3500非空行；实际'+str(result['total'])+'非空行。\n\n|实际参与构建文件|用途|非空行|\n|---|---|---:|\n'+'\n'.join(f"|{r['file']}|{r['purpose']}|{r['nonempty_lines']}|" for r in rows)+'\n\n源码全文见生产源码.txt，按路径排序拼接；文件校验和见source-inventory.json。正式申报页数和选取范围由申请人按当次要求核定。\n')
assert result['total']>=3500,result['total']
print(f"{len(rows)} files; {result['total']} nonempty production lines")
