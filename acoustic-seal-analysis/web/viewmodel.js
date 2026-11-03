/** Observable state and commands: the ViewModel in the local MVVM application. */

import { ApiService, download, filename } from './service.js';

export const trialTypes = {
  baseline: '基线',
  single: '单项封堵',
  combination: '组合封堵',
  recovery: '恢复',
};

export const sourceTypes = {
  simulation: '模拟',
  measured: '实测',
  manual: '人工填写',
};

export const purposes = {
  effect: '封堵声级差',
  drift: '基线漂移',
  recovery: '解除封堵恢复',
};

export const statuses = {
  valid: '可比较',
  blocked: '已阻断',
  missing: '缺失',
  empty: '无有效数据',
  within: '阈值内',
  alert: '超出阈值',
  unavailable: '无法检查',
  insufficient: '样本不足',
};

export class ViewModel {
  constructor() {
    this.api = new ApiService();
    this.listeners = new Set();
    this.project = null;
    this.analysis = null;
    this.page = 'project';
    this.selected = {};
    this.dirty = false;
    this.formDirty = false;
    this.busy = false;
    this.csvText = '';
    this.csvMode = 'merge';
    this.csvPreview = null;
    this.trialSearch = '';
    this.trialFilter = '';
    this.pairFilter = '';
    this.scanLow = null;
    this.scanHigh = null;
    this.exportMessage = '';
    this.lastDownload = '';
    this.noticeListeners = new Set();
  }

  subscribe(listener) {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  onNotice(listener) {
    this.noticeListeners.add(listener);
  }

  notify(message, error = false) {
    for (const listener of this.noticeListeners) {
      listener(message, error);
    }
  }

  emit() {
    for (const listener of this.listeners) {
      listener(this);
    }
  }

  async run(task) {
    if (this.busy) {
      return;
    }
    this.busy = true;
    document.querySelector('#busy').hidden = false;
    try {
      await task();
    } catch (error) {
      this.notify(error.message, true);
    } finally {
      this.busy = false;
      document.querySelector('#busy').hidden = true;
    }
  }

  accept(response, dirty = true) {
    this.project = response.project;
    this.analysis = response.analysis;
    this.dirty = dirty;
    this.formDirty = false;
    this.csvPreview = null;
    this.emit();
  }

  async initialize() {
    await this.run(async () => {
      this.accept(await this.api.post('new'), false);
    });
  }

  mayDiscardForm() {
    return !this.formDirty || window.confirm('表单中有尚未应用的输入。确定放弃这些输入吗？');
  }

  mayReplace() {
    return (!this.dirty && !this.formDirty) || window.confirm('当前项目或表单有未保存内容。请先下载项目文件；确定放弃当前工作副本并继续吗？');
  }

  navigate(page) {
    if (!this.mayDiscardForm()) {
      return;
    }
    this.page = page;
    this.formDirty = false;
    this.emit();
    window.scrollTo({ top: 0 });
  }

  select(collection, id) {
    if (!this.mayDiscardForm()) {
      return;
    }
    this.selected[collection] = id;
    this.formDirty = false;
    this.emit();
  }

  item(collection) {
    return this.project?.[collection]?.find(item => item.id === this.selected[collection]) || null;
  }

  async newProject() {
    if (!this.mayReplace()) {
      return;
    }
    await this.run(async () => {
      this.selected = {};
      this.page = 'project';
      this.accept(await this.api.post('new'), false);
      this.notify('已建立空白项目。');
    });
  }

  async loadDemo() {
    if (!this.mayReplace()) {
      return;
    }
    if (!window.confirm('载入固定模拟样例：2 个封堵位置、1 个工况、8 个试验和 6 个显式配对。数据全部模拟，不能作为实测依据。继续？')) {
      return;
    }
    await this.run(async () => {
      this.selected = {};
      this.page = 'results';
      this.accept(await this.api.post('demo'));
      this.notify('模拟样例已载入。界面和导出均保留模拟标识。');
    });
  }

  async openFile(file) {
    if (!file || !this.mayReplace()) {
      return;
    }
    await this.run(async () => {
      const response = await this.api.open(file);
      this.selected = {};
      this.page = 'project';
      this.accept(response, false);
      this.notify(`已打开 ${file.name}；项目修订 ${this.project.revision}。`);
    });
  }

  async command(action, payload = {}, message = '操作完成') {
    await this.run(async () => {
      const response = await this.api.post('command', {
        project: this.project,
        action,
        payload,
      });
      if (action === 'upsert' && response.resultId) {
        this.selected[payload.collection] = response.resultId;
      }
      if (action === 'duplicateTrial') {
        this.selected.trials = response.resultId;
      }
      this.accept(response);
      this.notify(message);
    });
  }

  async saveItem(collection, item) {
    await this.command('upsert', { collection, item }, '对象已应用到项目。请下载项目以持久保存。');
  }

  async deleteItem(collection, id) {
    const item = this.project[collection].find(entry => entry.id === id);
    if (!item || !window.confirm(`确定删除“${item.name || id}”？被引用的对象将阻止删除。`)) {
      return;
    }
    await this.command('delete', { collection, id }, '对象已删除。');
  }

  async duplicateTrial(id) {
    await this.command('duplicateTrial', { id }, '已复制试验结构，测量和配对未复制，请复核时间与来源。');
  }

  async previewCsv() {
    await this.run(async () => {
      const response = await this.api.post('import-preview', {
        project: this.project,
        content: this.csvText,
        mode: this.csvMode,
      });
      this.csvPreview = response.preview;
      this.formDirty = false;
      this.emit();
      this.notify('预检通过，尚未更改测量。核对覆盖范围后确认导入。');
    });
  }

  async applyCsv() {
    if (!this.csvPreview) {
      this.notify('请先预检 CSV。', true);
      return;
    }
    if (!window.confirm(`将修改 ${this.csvPreview.trials} 个试验、${this.csvPreview.rows} 行频带，覆盖 ${this.csvPreview.overwritten} 个已有频带。确定导入？`)) {
      return;
    }
    await this.command('import', {
      content: this.csvText,
      mode: this.csvMode,
    }, 'CSV 已整体导入；来源沿用试验元数据。');
  }

  async export(kind) {
    if (this.formDirty) {
      this.notify('请先应用或取消表单输入，导出只包含已应用的项目数据。', true);
      return;
    }
    await this.run(async () => {
      const response = await this.api.export(this.project, kind);
      const target = filename(this.project.name) + response.extension;
      download(response.content, target, response.mime);
      this.lastDownload = target;
      this.exportMessage = `已发起下载：${target}。实际路径由浏览器下载设置决定，请在下载记录中确认文件存在。`;
      if (kind === 'project') {
        this.dirty = false;
      }
      this.emit();
      this.notify(this.exportMessage);
    });
  }

  async recordAnalysis() {
    await this.command('recordAnalysis', {}, '已将当前阻断原因与参数写入项目审计。');
  }

  sourceSummary() {
    const counts = { simulation: 0, measured: 0, manual: 0 };
    for (const trial of this.project?.trials || []) {
      counts[trial.sourceKind] += 1;
    }
    return Object.entries(counts)
      .filter(([, count]) => count)
      .map(([kind, count]) => `${sourceTypes[kind]} ${count}`)
      .join(' · ') || '尚无试验数据';
  }

  visibleTrials() {
    const query = this.trialSearch.trim().toLowerCase();
    return this.project.trials.filter(trial => {
      const matchesText = `${trial.name} ${trial.id} ${trial.repeatGroup}`.toLowerCase().includes(query);
      return matchesText && (!this.trialFilter || trial.type === this.trialFilter);
    });
  }

  visiblePairs() {
    return this.analysis.pairs.filter(pair => !this.pairFilter || pair.purpose === this.pairFilter);
  }
}
