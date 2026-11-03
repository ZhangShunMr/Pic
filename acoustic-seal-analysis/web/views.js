/** Pure rendering functions. All user strings pass through escape(). */

import { trialTypes, sourceTypes, purposes, statuses } from './viewmodel.js';

export function escape(value = '') {
  return String(value ?? '').replace(/[&<>"']/g, char => ({
    '&': '&amp;',
    '<': '&lt;',
    '>': '&gt;',
    '"': '&quot;',
    "'": '&#39;',
  })[char]);
}

export function number(value, digits = 3) {
  return value === null || value === undefined ? '—' : Number(value).toFixed(digits);
}

function button(label, action, extra = '', className = '') {
  return `<button type="button" class="${className}" data-action="${action}" ${extra}>${label}</button>`;
}

function field(label, name, value = '', options = {}) {
  const required = options.required ? 'required' : '';
  const type = options.type || 'text';
  const attributes = options.attributes || '';
  const help = options.help ? `<small>${escape(options.help)}</small>` : '';
  if (options.area) {
    return `<label class="field ${options.wide ? 'wide' : ''}"><span>${label}</span><textarea name="${name}" ${required} ${attributes}>${escape(value)}</textarea>${help}</label>`;
  }
  return `<label class="field ${options.wide ? 'wide' : ''}"><span>${label}</span><input name="${name}" type="${type}" value="${escape(value)}" ${required} ${attributes}>${help}</label>`;
}

function select(label, name, value, options, help = '') {
  const items = Object.entries(options).map(([key, text]) => {
    return `<option value="${escape(key)}" ${value === key ? 'selected' : ''}>${escape(text)}</option>`;
  }).join('');
  return `<label class="field"><span>${label}</span><select name="${name}">${items}</select>${help ? `<small>${escape(help)}</small>` : ''}</label>`;
}

function check(label, name, checked = false) {
  return `<label class="check"><input type="checkbox" name="${name}" ${checked ? 'checked' : ''}>${label}</label>`;
}

function empty(message, detail = '') {
  return `<div class="empty"><strong>${escape(message)}</strong><p>${escape(detail)}</p></div>`;
}

function badge(status) {
  return `<span class="badge ${escape(status)}">${escape(statuses[status] || status)}</span>`;
}

function table(headers, rows, emptyMessage = '暂无数据') {
  return `<div class="table-wrap"><table><thead><tr>${headers.map(header => `<th>${header}</th>`).join('')}</tr></thead><tbody>${rows.length ? rows.join('') : `<tr><td colspan="${headers.length}" class="muted">${escape(emptyMessage)}</td></tr>`}</tbody></table></div>`;
}

function row(cells) {
  return `<tr>${cells.map(cell => `<td>${cell}</td>`).join('')}</tr>`;
}

function heading(eyebrow, title, description, actions = '') {
  return `<div class="page-heading"><div><p class="eyebrow">${eyebrow}</p><h1>${title}</h1><p class="muted">${description}</p></div><div class="actions">${actions}</div></div>`;
}

function editorTitle(item, noun) {
  return `<div class="section-title"><h2>${item ? '编辑' : '新增'}${noun}</h2>${item ? `<span class="mono">${escape(item.id)}</span>` : '<span class="muted">保存后自动生成标识</span>'}</div>`;
}

function formActions(collection, item) {
  return `<div class="form-actions"><button class="primary" type="submit">应用${item ? '修改' : '新增'}</button>${button('清空 / 新增', 'select', `data-collection="${collection}" data-id=""`)}${item ? button('删除', 'delete', `data-collection="${collection}" data-id="${escape(item.id)}"`, 'danger') : ''}<span class="muted">应用后请下载项目文件</span></div>`;
}

function objectList(vm, collection, description) {
  const items = vm.project[collection];
  return `<aside class="object-list"><div class="section-title"><h2>${description}</h2><span>${items.length}</span></div>${items.length ? items.map(item => `<button class="object ${vm.selected[collection] === item.id ? 'selected' : ''}" type="button" data-action="select" data-collection="${collection}" data-id="${escape(item.id)}"><strong>${escape(item.name || item.id)}</strong><small>${escape(item.zone || item.vehicle || item.id)}</small></button>`).join('') : empty('尚无记录', '在右侧填写并应用新增。')}</aside>`;
}

const pages = [
  ['project', '01', '项目工作台'],
  ['sites', '02', '封堵位置'],
  ['conditions', '03', '工况与口径'],
  ['trials', '04', '试验与频带'],
  ['import', '05', '批量导入'],
  ['pairs', '06', '显式配对'],
  ['results', '07', '比较与统计'],
  ['quality', '08', '质量与复核'],
  ['export', '09', '保存与报告'],
];

export function shell(vm, content) {
  const archived = vm.project.status === 'archived';
  return `<div class="app-shell">
    <aside class="sidebar">
      <div class="brand"><span class="brand-mark">AS</span><div><strong>车身声学试验</strong><small>泄漏路径与封堵分析 · V1.0</small></div></div>
      <div class="project-tag"><span class="status-dot"></span><strong>${escape(vm.project.name)}</strong><small>修订 ${vm.project.revision} · ${archived ? '已归档，只读' : '编辑中'}</small></div>
      <nav aria-label="工作流程">${pages.map(([key, index, title]) => `<button data-action="navigate" data-page="${key}" class="nav-item ${vm.page === key ? 'active' : ''}" ${vm.page === key ? 'aria-current="page"' : ''}><span>${index}</span>${title}</button>`).join('')}</nav>
      <div class="sidebar-bottom"><p>本机工作副本</p><small>下载 .aslp.json 才能持久保存。<br>无外部服务与网络资源依赖。</small></div>
    </aside>
    <div class="workspace">
      <header class="topbar"><span>封堵试验分析 / ${pages.find(page => page[0] === vm.page)?.[2]}</span><div class="actions"><span class="save-state ${vm.dirty ? 'unsaved' : ''}">${vm.dirty ? '● 有未下载修改' : '工作副本已载入 / 下载已发起'}</span>${button('打开项目', 'open')}${button('下载项目', 'save', '', 'primary')}</div></header>
      <div class="source-strip"><span>${escape(vm.sourceSummary())}</span><span>${archived ? '归档只读' : 'ΔL = 基线 − 目标，单位 dB'}</span></div>
      <main id="main-content">${content}</main>
      <footer>车身声学泄漏路径与封堵试验分析软件 V1.0 · 结果为测得声级差；单项分贝不可相加预测组合。</footer>
    </div>
  </div>`;
}

export function projectPage(vm) {
  const project = vm.project;
  const counts = vm.analysis.counts;
  const metrics = [
    [counts.sites, '封堵位置'],
    [counts.trials, '试验记录'],
    [counts.valid, '可比较配对'],
    [counts.blocked, '已阻断配对'],
  ];
  return heading('PROJECT / 项目管理', '项目工作台', '建立可追溯的试验对象，从工况确认到成果归档。', button('新建空白项目', 'new') + button('载入模拟样例', 'demo')) + `
    <div class="metrics">${metrics.map(([value, label]) => `<div class="metric"><span>${label}</span><strong>${value}</strong></div>`).join('')}</div>
    <div class="two-columns"><section class="panel">
      <h2>项目信息</h2>
      <form id="metadata-form" class="form-grid">
        ${field('项目名称 *', 'name', project.name, { required: true, wide: true, attributes: 'maxlength="160"' })}
        ${field('负责人（用户填写）', 'owner', project.owner, { attributes: 'maxlength="120"' })}
        ${field('项目状态', 'statusDisplay', project.status === 'archived' ? '归档只读' : '活动项目', { attributes: 'disabled' })}
        ${field('项目背景、试验目标及数据说明', 'description', project.description, { area: true, wide: true, attributes: 'maxlength="8000" rows="5"' })}
        <div class="form-actions"><button type="submit" class="primary">应用项目信息</button>${button('复制项目', 'clone')}${button(project.status === 'archived' ? '解除归档' : '归档为只读', project.status === 'archived' ? 'restore' : 'archive')}</div>
      </form>
      <dl class="details"><dt>项目标识</dt><dd class="mono">${escape(project.id)}</dd><dt>创建时间</dt><dd>${escape(project.createdAt)}</dd><dt>最近修改</dt><dd>${escape(project.updatedAt)}</dd><dt>文件格式</dt><dd>.aslp.json · schema ${project.schemaVersion}</dd></dl>
    </section><section class="panel workflow">
      <h2>建议操作顺序</h2>
      <ol><li><strong>定义位置与工况</strong><p>记录材料、封堵工艺、测点、标定和计权；缺少口径将阻断比较。</p></li><li><strong>组织试验和输入频带</strong><p>基线、单项、组合、恢复分别建档，明确来源。可逐项录入或使用 CSV。</p></li><li><strong>显式配对并复核</strong><p>为每条目标试验选择基线；检查重复性、漂移与恢复。</p></li><li><strong>保存、重开、输出</strong><p>下载项目，在新会话打开核对；导出带来源的报告和明细。</p></li></ol>
      <div class="notice">${escape(vm.analysis.limitation)}</div>
    </section></div>`;
}

export function sitesPage(vm) {
  const item = vm.item('sites');
  const site = item || {};
  return heading('LOCATIONS / 封堵对象', '封堵位置管理', '描述可复现的位置、材料和覆盖范围；被试验引用的位置不可直接删除。') + `
    <div class="editor-layout">${objectList(vm, 'sites', '位置列表')}<section class="panel">
      ${editorTitle(item, '封堵位置')}
      <form id="site-form" class="form-grid" data-id="${escape(site.id || '')}">
        ${field('位置名称 *', 'name', site.name, { required: true, attributes: 'maxlength="120"' })}
        ${field('车身区域', 'zone', site.zone, { attributes: 'maxlength="120"' })}
        ${field('材料与规格', 'material', site.material, { wide: true, help: '按实际使用填写；示例内容不得当作实物记录。' })}
        ${field('封堵步骤与覆盖范围', 'procedure', site.procedure, { area: true, wide: true, attributes: 'rows="5" maxlength="4000"' })}
        ${field('备注与恢复注意事项', 'notes', site.notes, { area: true, wide: true, attributes: 'maxlength="4000"' })}
        ${formActions('sites', item)}
      </form>
    </section></div>`;
}

export function conditionsPage(vm) {
  const item = vm.item('conditions');
  const condition = item || {
    weighting: 'Z',
    timeWeighting: 'Leq',
    bandType: 'octave',
    unit: 'dB re 20 µPa',
    independentBands: false,
  };
  return heading('CONDITIONS / 可比性基础', '工况与测量口径', '可先存草稿；必要字段缺失会阻断配对。修改被引用工况会重新计算关联结果，并写入审计。') + `
    <div class="editor-layout">${objectList(vm, 'conditions', '工况列表')}<section class="panel">
      ${editorTitle(item, '工况')}
      <form id="condition-form" class="form-grid" data-id="${escape(condition.id || '')}">
        ${field('工况名称 *', 'name', condition.name, { required: true, wide: true, attributes: 'maxlength="120"' })}
        ${field('车辆标识', 'vehicle', condition.vehicle, { help: '必须一致；请按项目隐私要求使用代号。' })}
        ${field('测点及安装位置', 'microphone', condition.microphone)}
        ${field('声源 / 激励方式', 'excitation', condition.excitation)}
        ${field('路面 / 试验场', 'surface', condition.surface)}
        ${field('挡位', 'gear', condition.gear)}
        ${field('车速 km/h', 'speed', condition.speed, { type: 'number', attributes: 'min="0" max="500" step="any"', help: '静态试验可填 0；空白表示未知。' })}
        ${field('转速 r/min', 'rpm', condition.rpm, { type: 'number', attributes: 'min="0" max="30000" step="any"' })}
        ${field('温度 °C', 'temperature', condition.temperature, { type: 'number', attributes: 'min="-80" max="100" step="any"' })}
        ${field('积分时长 s', 'duration', condition.duration, { type: 'number', attributes: 'min="0.001" max="86400" step="any"' })}
        ${select('频率计权', 'weighting', condition.weighting, { '': '未填写', Z: 'Z', A: 'A', C: 'C' }, '录入数据须已采用此计权，软件不再次加权。')}
        ${field('时间计权 / 积分定义', 'timeWeighting', condition.timeWeighting, { help: '例如 Leq。必须与仪器数据口径一致。' })}
        ${select('频带制', 'bandType', condition.bandType, { '': '未填写', octave: '倍频程', 'third-octave': '三分之一倍频程', custom: '自定义' })}
        ${field('仪器标识', 'instrument', condition.instrument)}
        ${field('标定链路及状态', 'calibration', condition.calibration, { help: '可填写标定记录编号和状态；软件不认证有效性。' })}
        ${select('声级单位及参考', 'unit', condition.unit, { '': '未填写', 'dB re 20 µPa': 'dB re 20 µPa' })}
        <div class="wide">${check('确认输入频带互不重叠，允许计算“录入频带范围”的合成声级', 'independentBands', condition.independentBands)}</div>
        ${field('工况备注', 'notes', condition.notes, { area: true, wide: true })}
        ${formActions('conditions', item)}
      </form>
    </section></div>`;
}

function bandRows(trial) {
  return Object.entries(trial.bands || {}).sort((a, b) => Number(a[0]) - Number(b[0])).map(([frequency, level]) => `${frequency},${level ?? ''}`).join('\n');
}

export function trialsPage(vm) {
  const item = vm.item('trials');
  const trial = item || {
    type: 'baseline',
    sourceKind: 'manual',
    timestamp: new Date().toISOString(),
    conditionId: vm.project.conditions[0]?.id || '',
    siteIds: [],
    bands: {},
  };
  const conditions = Object.fromEntries(vm.project.conditions.map(entry => [entry.id, entry.name]));
  const trialRows = vm.visibleTrials().map(entry => row([
    `<button class="text-button" data-action="select" data-collection="trials" data-id="${escape(entry.id)}">${escape(entry.name)}</button><small class="mono block">${escape(entry.id)}</small>`,
    trialTypes[entry.type],
    `<span class="source ${entry.sourceKind}">${sourceTypes[entry.sourceKind]}</span>`,
    escape(vm.project.conditions.find(condition => condition.id === entry.conditionId)?.name),
    String(Object.values(entry.bands).filter(value => value !== null).length),
    escape(entry.repeatGroup || '—'),
  ]));
  return heading('TRIALS / 试验档案', '试验与频带数据', '逐次记录实际测量；复制试验只复制结构，避免复制数值被误认作独立重复。', button('新增试验', 'select', 'data-collection="trials" data-id=""')) + `
    <section class="panel"><div class="filters">
      <label>查找名称、标识或重复组 <input id="trial-search" type="search" value="${escape(vm.trialSearch)}" placeholder="输入后按回车搜索"></label>
      ${select('类型筛选', 'trialFilter', vm.trialFilter, { '': '全部类型', ...trialTypes })}
      <span class="muted">${vm.visibleTrials().length} / ${vm.project.trials.length} 条</span>
    </div>${table(['试验', '类型', '来源', '工况', '有效频带', '重复组'], trialRows)}</section>
    <section class="panel trial-editor">
      ${editorTitle(item, '试验')}
      ${!vm.project.conditions.length ? '<div class="notice">请先在“工况与口径”建立工况，再应用试验。</div>' : ''}
      <form id="trial-form" class="form-grid" data-id="${escape(trial.id || '')}">
        ${field('试验名称 *', 'name', trial.name, { required: true, wide: true, attributes: 'maxlength="160"' })}
        ${select('试验类型', 'type', trial.type, trialTypes)}
        ${select('关联工况', 'conditionId', trial.conditionId, { '': '请选择工况', ...conditions })}
        <fieldset class="wide site-checks"><legend>封堵位置（基线/恢复不选；单项选一；组合至少选二）</legend>${vm.project.sites.map(site => `<label class="check"><input type="checkbox" name="siteIds" value="${escape(site.id)}" ${trial.siteIds.includes(site.id) ? 'checked' : ''}>${escape(site.name)}</label>`).join('') || '<span class="muted">还没有封堵位置。</span>'}</fieldset>
        ${field('试验时间 ISO 8601 *', 'timestamp', trial.timestamp, { required: true, help: '包含时区，例如 2026-01-01T10:00:00+08:00。' })}
        ${field('重复组', 'repeatGroup', trial.repeatGroup, { help: '同名组内按类型、位置、来源分开检查，工况仍须一致。' })}
        ${select('数据来源类别', 'sourceKind', trial.sourceKind, sourceTypes)}
        ${field('操作员', 'operator', trial.operator)}
        ${field('来源说明 *', 'sourceNote', trial.sourceNote, { area: true, required: true, wide: true, help: '实测填写数据文件/试验记录来源；模拟说明生成方法，人工填写说明依据。' })}
        ${field('频带数据（每行 Hz,声级dB）', 'bandsText', bandRows(trial), { area: true, wide: true, attributes: 'rows="7" spellcheck="false"', help: '例如 125,70。声级空白或 NA 表示缺失，绝不补零；不带表头。频率 1—100000 Hz；声级 -100—200 dB。' })}
        ${field('试验备注', 'notes', trial.notes, { area: true, wide: true })}
        ${formActions('trials', item)}
        ${item ? `<div class="wide">${button('复制试验结构（不复制测量）', 'duplicate-trial', `data-id="${escape(item.id)}"`)}</div>` : ''}
      </form>
    </section>`;
}

export function importPage(vm) {
  const preview = vm.csvPreview;
  const trialMap = new Map(vm.project.trials.map(trial => [trial.id, trial]));
  return heading('IMPORT / 批量输入', 'CSV 频带导入', '先建立试验元数据，再以试验标识导入频带。预检不改项目，错误会取消整次导入。') + `
    <div class="two-columns"><section class="panel">
      <h2>选择或粘贴 CSV</h2>
      <div class="actions">${button('选择 UTF-8 CSV 文件', 'choose-csv')}${button('下载当前原始频带 CSV', 'export', 'data-kind="measurements"')}</div>
      <form id="import-form" class="form-grid">
        ${select('导入模式', 'csvMode', vm.csvMode, { merge: '合并：保留其他频带，覆盖同频带', replace: '替换：清空受影响试验全部旧频带' })}
        ${field('CSV 内容', 'csvText', vm.csvText, { area: true, wide: true, attributes: 'rows="15" spellcheck="false"', help: '表头严格为 trial_id,frequency_hz,level_db；逗号分隔，句点小数。' })}
        <div class="form-actions"><button class="primary" type="submit">预检 CSV</button>${button('确认导入', 'apply-csv', preview ? '' : 'disabled')}</div>
      </form>
    </section><section class="panel">
      <h2>格式与预检结果</h2>
      <pre class="code-sample">trial_id,frequency_hz,level_db\nb1,125,70\nb1,250,\ns1,125,67</pre>
      <p>示例中的 b1 和 s1 必须替换成当前试验标识。声级缺失允许空白、NA、N/A、null。重复频带、未知试验、越界数值会指出行号。</p>
      <div class="notice">来源类别和说明沿用试验档案。导入不认证数据来源；请先在“试验与频带”复核。</div>
      ${preview ? `<dl class="details"><dt>数据行</dt><dd>${preview.rows}</dd><dt>受影响试验</dt><dd>${preview.trials}</dd><dt>缺失值</dt><dd>${preview.missing}</dd><dt>同频带覆盖</dt><dd>${preview.overwritten}</dd><dt>模式</dt><dd>${escape(preview.mode)}</dd></dl>${table(['试验', '导入频带数'], Object.entries(preview.updates).map(([id, bands]) => row([escape(trialMap.get(id).name), String(Object.keys(bands).length)])))}` : empty('尚未预检', '预检通过后核对覆盖数，再确认导入。')}
    </section></div>`;
}

export function pairsPage(vm) {
  const item = vm.item('pairs');
  const pair = item || { purpose: 'effect' };
  const baselines = Object.fromEntries(vm.project.trials.filter(trial => trial.type === 'baseline').map(trial => [trial.id, `${trial.name} [${sourceTypes[trial.sourceKind]}]`]));
  const targets = Object.fromEntries(vm.project.trials.map(trial => [trial.id, `${trial.name} · ${trialTypes[trial.type]}`]));
  return heading('PAIRING / 明确基准', '显式试验配对', '选择基线和目标，不依赖试验名称、导入顺序或时间自动推断对应关系。') + `
    <section class="panel">
      ${table(['基线 → 目标', '用途', '可比状态', '操作'], vm.analysis.pairs.map(result => row([
        `${escape(result.baselineName)} → ${escape(result.targetName)}`,
        purposes[result.purpose],
        `${badge(result.status)}${result.reasons.length ? `<small class="block error-text">${escape(result.reasons.join('；'))}</small>` : ''}`,
        button('编辑', 'select', `data-collection="pairs" data-id="${escape(result.id)}"`),
      ])))}
    </section><section class="panel">
      ${editorTitle(item, '配对')}
      <form id="pair-form" class="form-grid" data-id="${escape(pair.id || '')}">
        ${select('用途', 'purpose', pair.purpose, purposes)}
        ${select('基准：基线试验', 'baselineId', pair.baselineId, { '': '请选择基线', ...baselines })}
        ${select('目标试验', 'targetId', pair.targetId, { '': '请选择目标', ...targets })}
        <div class="help-box">封堵声级差：目标为单项或组合；基线漂移：目标为另一基线；恢复：目标为解除封堵后的恢复试验。类型不匹配会拒绝保存。</div>
        ${field('配对理由和备注', 'notes', pair.notes, { area: true, wide: true })}
        ${formActions('pairs', item)}
      </form>
    </section>`;
}

function pairChart(pair) {
  const bands = pair.bands.filter(band => band.difference !== null);
  if (!bands.length) {
    return empty('无可绘制结果', pair.reasons.join('；') || '请录入共同有效频带。');
  }
  const width = 760;
  const height = 240;
  const left = 55;
  const right = 740;
  const baselineY = 132;
  const maximum = Math.max(1, ...bands.map(band => Math.abs(band.difference)));
  const scale = 85 / maximum;
  const parts = [
    `<svg class="chart" viewBox="0 0 ${width} ${height}" role="img" aria-label="封堵前后测得声级差，横轴Hz，纵轴dB">`,
    `<line x1="${left}" y1="${baselineY}" x2="${right}" y2="${baselineY}" class="zero-line"/>`,
    '<text x="5" y="20">ΔL / dB</text>',
    `<text x="8" y="50">${number(maximum, 1)}</text>`,
    `<text x="20" y="136">0</text>`,
    `<text x="5" y="220">${number(-maximum, 1)}</text>`,
  ];
  for (const [index, band] of bands.entries()) {
    const x = left + (index + 0.5) * (right - left) / bands.length;
    const barWidth = Math.min(50, (right - left) / bands.length * 0.55);
    const barHeight = Math.abs(band.difference) * scale;
    const y = band.difference >= 0 ? baselineY - barHeight : baselineY;
    parts.push(`<rect x="${x - barWidth / 2}" y="${y}" width="${barWidth}" height="${barHeight}" class="${band.difference >= 0 ? 'positive-bar' : 'negative-bar'}"><title>${band.frequency} Hz：${number(band.difference)} dB</title></rect>`);
    parts.push(`<text x="${x}" y="${band.difference >= 0 ? y - 8 : y + barHeight + 15}" text-anchor="middle">${number(band.difference, 2)}</text>`);
    parts.push(`<text x="${x}" y="238" text-anchor="middle">${band.frequency}</text>`);
  }
  parts.push('<text x="705" y="20">Hz</text>', '</svg>');
  return parts.join('');
}

export function resultsPage(vm) {
  const analysis = vm.analysis;
  const siteNames = ids => ids.map(id => vm.project.sites.find(site => site.id === id)?.name || id).join(' + ');
  const pairs = vm.visiblePairs();
  return heading('ANALYSIS / 测得声级差', '比较与统计', '正值表示目标声级低于基线；负值表示目标较高。组合由组合试验实测数据计算。', button('记录当前分析复核', 'record-analysis')) + `
    <div class="notice">${escape(analysis.limitation)}</div>
    <section class="panel filters">${select('配对用途', 'pairFilter', vm.pairFilter, { '': '全部用途', ...purposes })}<span class="muted">当前修订 ${vm.project.revision} · 参数修改后自动重新计算</span></section>
    ${pairs.length ? pairs.map(pair => `<section class="panel result-card">
      <div class="section-title"><div><p class="eyebrow">${purposes[pair.purpose]} · ${sourceTypes[pair.sourceKind]}</p><h2>${escape(pair.baselineName)} → ${escape(pair.targetName)}</h2></div>${badge(pair.status)}</div>
      ${pair.reasons.length ? `<div class="error-box">阻断原因：${escape(pair.reasons.join('；'))}</div>` : ''}
      ${pair.warnings.length ? `<p class="muted">${escape(pair.warnings.join('；'))}</p>` : ''}
      ${pairChart(pair)}
      ${table(['频率 Hz', '基线 dB', '目标 dB', '基线 − 目标 dB', '状态'], pair.bands.map(band => row([
        String(band.frequency), number(band.baseline), number(band.target), `<strong>${number(band.difference)}</strong>`, badge(band.status),
      ])))}
      <div class="result-note"><strong>录入频带合成声级差：${number(pair.totalDifference)} dB</strong><span>${escape(pair.totalNote)}</span></div>
      ${pair.screening ? `<div class="screening">${badge(pair.screening.status)} 最大频带绝对差 ${number(pair.screening.maximumAbsoluteDifference)} dB / 用户阈值 ${number(pair.screening.threshold)} dB。${escape(pair.screening.meaning)}</div>` : ''}
      <details><summary>查看配对来源与时间顺序</summary><p>基线：${escape(pair.baselineSource)}</p><p>目标：${escape(pair.targetSource)}</p><p>目标减基线时间：${pair.elapsedSeconds} s</p><p class="mono">配对 ${escape(pair.id)}</p></details>
    </section>`).join('') : empty('暂无配对结果', '先建试验并到“显式配对”建立基准与目标。')}
    ${matrixSection(vm)}
    <section class="panel"><h2>同封堵方案的重复配对统计</h2><p class="muted">配对差算术均值与两组能量平均声级之差分别列出，不可混用。95% 区间仅描述独立配对差均值，未包含仪器系统不确定性。</p>
      ${analysis.effects.map(effect => `<h3>${escape(siteNames(effect.siteIds))} · ${sourceTypes[effect.sourceKind]}</h3>${effect.reasons.length ? `<div class="error-box">${escape(effect.reasons.join('；'))}</div>` : ''}${table(['Hz', 'n', '配对差均值 dB', '差值SD dB', '组能量均值差 dB', '95% 区间 dB'], effect.bands.map(band => row([
        String(band.frequency), String(band.n), number(band.meanDifference), number(band.sdDifference), number(band.groupEnergyMeanDifference), band.ci95 ? `${number(band.ci95[0])} 至 ${number(band.ci95[1])}` : '不生成',
      ])))}<p class="muted">${escape(effect.bands[0]?.ciNote || '')}</p>`).join('') || '<p>尚无可用封堵配对。</p>'}
    </section>`;
}

const parameterDefinitions = [
  ['speedTolerance', '车速容差 km/h', 0, 50, '默认 0.5；绝对差超出即阻断。'],
  ['rpmTolerance', '转速容差 r/min', 0, 5000, '默认 20；静态试验也需明确填写转速。'],
  ['temperatureTolerance', '温度容差 °C', 0, 30, '默认 2；是工程设定，非标准限值。'],
  ['durationTolerance', '积分时长容差 s', 0, 3600, '默认 0.01；确保统计时段口径一致。'],
  ['driftThreshold', '漂移绝对差阈值 dB', 0, 30, '默认 1；逐频带绝对差最大值筛查。'],
  ['recoveryThreshold', '恢复绝对差阈值 dB', 0, 30, '默认 1；仅作工程筛查。'],
  ['repeatabilityThreshold', '重复性样本SD阈值 dB', 0, 30, '默认 1；n<2 不判定重复性。'],
];

export function qualityPage(vm) {
  const analysis = vm.analysis;
  return heading('REVIEW / 质量复核', '重复性、漂移与恢复', '所有阈值为用户工程设置；不代表标准判定。异常和阻断原因随报告保留。') + `
    <section class="panel"><h2>分析参数</h2><form id="parameters-form" class="form-grid three">
      ${parameterDefinitions.map(([key, label, low, high, help]) => field(label, key, vm.project.parameters[key], { type: 'number', required: true, attributes: `min="${low}" max="${high}" step="any"`, help })).join('')}
      <div class="form-actions"><button type="submit" class="primary">应用参数并重新计算</button>${button('记录当前分析复核', 'record-analysis')}</div>
    </form></section>
    <section class="panel"><h2>逐组重复性</h2><p class="muted">对同重复组、同类型、同封堵位置、同来源且工况可比的试验统计。SD 为 dB 数值样本标准差；能量均值另列。</p>
      ${analysis.repeatability.map(group => `<h3>${escape(group.name)} · ${trialTypes[group.type]} · ${sourceTypes[group.sourceKind]}</h3>${group.reasons.length ? `<div class="error-box">${escape(group.reasons.join('；'))}</div>` : ''}${table(['Hz', 'n', '缺失', '算术均值 dB', '能量均值 dB', '样本SD dB', '极差 dB', '状态'], group.bands.map(band => row([
        String(band.frequency), String(band.n), String(band.missing), number(band.mean), number(band.energyMean), number(band.sd), number(band.range), badge(band.status),
      ])))}`).join('') || empty('还没有重复组', '在试验档案填写重复组名称。')}
    </section>
    <section class="panel"><h2>漂移与恢复摘要</h2>
      ${table(['配对', '用途', '最大频带绝对差 dB', '用户阈值 dB', '状态'], analysis.pairs.filter(pair => pair.screening).map(pair => row([
        `${escape(pair.baselineName)} → ${escape(pair.targetName)}`, purposes[pair.purpose], number(pair.screening.maximumAbsoluteDifference), number(pair.screening.threshold), badge(pair.screening.status),
      ])))}
      <p class="muted">详细频带值见“比较与统计”；未设置对应配对时不能推断稳定或恢复。</p>
    </section>
    ${coverageSection(vm)}
    <section class="panel"><h2>数据质量提示</h2>${table(['级别', '对象标识', '原因与处理线索'], analysis.issues.map(issue => row([
      escape({ error: '阻断', warning: '警示', info: '提示' }[issue.level]), `<span class="mono">${escape(issue.object)}</span>`, escape(issue.message),
    ])))}</section>
    <section class="panel"><h2>操作审计（最近 100 条）</h2><p class="muted">完整审计随项目和报告保留；这是本地操作记录，不提供防篡改签名。</p>${table(['时间', '操作', '详情'], vm.project.audit.slice(-100).reverse().map(entry => row([
      escape(entry.time), escape(entry.action), escape(entry.detail),
    ])))}</section>`;
}

export function exportPage(vm) {
  return heading('DELIVERY / 保存与输出', '保存、重开与报告', '下载后的项目包含全部频带和来源，不依赖原始 CSV 路径；浏览器不会自动保存当前工作副本。') + `
    <div class="export-grid">
      <section class="panel export-card"><span class="file-type">JSON</span><h2>项目文件</h2><p>保存元数据、位置、工况、试验、显式配对、参数和审计。下次用“打开项目”恢复。</p>${button('下载 .aslp.json 项目', 'save', '', 'primary')}</section>
      <section class="panel export-card"><span class="file-type">HTML</span><h2>自包含分析报告</h2><p>包含原始频带、配对差、图表、重复性、漂移、恢复、阻断原因和来源。浏览器打开可打印为 PDF。</p>${button('导出 HTML 报告', 'export', 'data-kind="report"', 'primary')}</section>
      <section class="panel export-card"><span class="file-type">CSV</span><h2>结果明细</h2><p>逐配对、逐频带列出基线、目标、测得声级差、状态和原因。UTF-8 BOM 便于表格软件识别中文。</p>${button('导出结果 CSV', 'export', 'data-kind="results"')}</section>
      <section class="panel export-card"><span class="file-type">CSV</span><h2>原始频带</h2><p>标准长表可重新导入；缺失值导出为空白。试验来源元数据保存在项目文件和报告中。</p>${button('导出原始频带 CSV', 'export', 'data-kind="measurements"')}</section>
    </div>
    ${vm.exportMessage ? `<div class="success-box">${escape(vm.exportMessage)}</div>` : ''}
    <section class="panel"><h2>保存后重开核对</h2><ol class="steps"><li>先应用所有表单输入，再点击“下载项目”。</li><li>在浏览器下载记录中确认文件已落盘，记录实际目录；软件无法读取浏览器下载设置。</li><li>点击“打开项目”，选择刚下载的 .aslp.json，核对项目名、修订号、试验数和配对结果。</li><li>将项目文件复制到另一目录后再打开，可验证内嵌数据的可搬迁性。</li><li>导出报告并核对来源标签、Hz、dB、工况、阈值和阻断原因。报告不替代项目文件。</li></ol></section>
    <section class="panel"><h2>使用边界</h2><p>${escape(vm.analysis.limitation)}</p><p>本版本处理已提取的频带声级，不读取音频、不自动标定、不重算 A/C/Z 计权、不管理多人并发。实际测量、封堵作业和数据合规由试验人员负责。敏感试验资料请按本单位要求保存。</p></section>`;
}

export function render(vm) {
  if (!vm.project) {
    return '<div class="startup"><h1>正在连接本地声学分析服务…</h1><p>若长时间无响应，请确认 Python 服务已启动。</p></div>';
  }
  const renderers = {
    project: projectPage,
    sites: sitesPage,
    conditions: conditionsPage,
    trials: trialsPage,
    import: importPage,
    pairs: pairsPage,
    results: resultsPage,
    quality: qualityPage,
    export: exportPage,
  };
  return shell(vm, (renderers[vm.page] || projectPage)(vm));
}

function coverageSection(vm) {
  const coverage = vm.analysis.coverage;
  const trialRows = coverage.timeline.map(trial => row([
    escape(trial.timestamp),
    escape(trial.name),
    trialTypes[trial.type],
    sourceTypes[trial.sourceKind],
    `${trial.validBands} / ${trial.declaredBands}`,
    `${trial.validTargetPairs} / ${trial.targetPairs}`,
  ]));
  const conditionRows = coverage.conditions.map(condition => row([
    escape(condition.name),
    String(condition.trialCount),
    condition.complete ? badge('valid') : badge('blocked'),
    escape(condition.missing.join('、') || '已填齐必要字段；仍需复核实际记录'),
  ]));
  const siteRows = coverage.sites.map(site => row([
    escape(site.name),
    String(site.singleTrials),
    String(site.combinationTrials),
    String(site.validEffectPairs),
    site.missingSingle ? '尚无单项试验' : '已有单项记录',
  ]));
  const frequencyRows = coverage.frequencies.map(frequency => row([
    String(frequency.frequency),
    String(frequency.valid),
    String(frequency.missing),
    String(frequency.absent),
    String(frequency.declared),
  ]));
  return `
    <section class="panel">
      <h2>试验覆盖与数据完整性</h2>
      <p class="muted">${escape(coverage.note)}</p>
      <div class="coverage-types">
        ${Object.entries(coverage.typeCounts).map(([type, count]) => `<div><strong>${count}</strong><span>${trialTypes[type]}</span></div>`).join('')}
      </div>
      <h3>工况字段完整性</h3>
      ${table(['工况', '关联试验数', '字段状态', '缺失内容 / 提示'], conditionRows)}
      <h3>位置覆盖</h3>
      ${table(['位置', '单项试验数', '组合试验数', '可比较封堵配对数', '提示'], siteRows)}
      <h3>频带覆盖</h3>
      <p class="muted">明确缺失表示频带键存在但声级为空；未声明表示该试验没有此频带键。两者均不补零。</p>
      ${table(['Hz', '有效记录数', '明确缺失数', '未声明数', '声明记录数'], frequencyRows)}
      <h3>试验时间线</h3>
      <p class="muted">时间排序仅供复核，不自动生成配对；可比较数只统计本试验作为目标的配对。</p>
      ${table(['带时区时间', '试验', '类型', '来源', '有效 / 声明频带', '可比较 / 目标配对'], trialRows)}
    </section>`;
}

function matrixSection(vm) {
  const matrix = vm.analysis.matrix;
  const low = vm.scanLow;
  const high = vm.scanHigh;
  const rows = matrix.rows.filter(row => (low === null || row.frequency >= low) && (high === null || row.frequency <= high));
  const headings = ['频率 Hz'];
  for (const column of matrix.columns) {
    headings.push(`${escape(column.targetName)}<small class="block">基线：${escape(column.baselineName)} · ${sourceTypes[column.sourceKind]}</small>`);
  }
  const cells = rows.map(record => row([
    String(record.frequency),
    ...record.cells.map(cell => cell.difference === null ? badge(cell.status) : `<strong class="${cell.difference < 0 ? 'increase-value' : 'decrease-value'}">${number(cell.difference)}</strong>`),
  ]));
  return `
    <section class="panel">
      <h2>单项与组合测得差值矩阵</h2>
      <p class="muted">${escape(matrix.note)}</p>
      <form id="scan-form" class="scan-form">
        ${field('扫描下限 Hz（空白不限）', 'scanLow', low, { type: 'number', attributes: 'min="1" max="100000" step="any"' })}
        ${field('扫描上限 Hz（空白不限）', 'scanHigh', high, { type: 'number', attributes: 'min="1" max="100000" step="any"' })}
        <button type="submit">筛选显示频带</button>
        ${button('显示全部频带', 'reset-scan')}
      </form>
      <p class="muted">显示 ${rows.length} / ${matrix.rows.length} 个频带。筛选只影响当前矩阵，不修改原始数据或报告。</p>
      ${table(headings, cells, '没有符合范围的频带或尚无封堵配对')}
      <p class="muted">表内数值单位 dB，正值为目标较低，负值为目标较高；缺失或阻断单独显示。矩阵不会计算“单项相加”列。</p>
    </section>`;
}
