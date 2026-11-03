/** View event adapters: translate controls into ViewModel commands. */

import { ViewModel } from './viewmodel.js';
import { render } from './views.js';
import { readCsv } from './service.js';

const vm = new ViewModel();
const app = document.querySelector('#app');
const toast = document.querySelector('#toast');
let toastTimer;

function update() {
  app.innerHTML = render(vm);
  if (vm.project?.status === 'archived') {
    for (const form of app.querySelectorAll('form')) {
      if (form.id !== 'metadata-form') {
        for (const control of form.elements) {
          control.disabled = true;
        }
      } else {
        for (const control of form.elements) {
          if (!['restore', 'clone'].includes(control.dataset.action)) {
            control.disabled = true;
          }
        }
      }
    }
  }
}

vm.subscribe(update);
vm.onNotice((message, error) => {
  toast.textContent = message;
  toast.hidden = false;
  toast.className = error ? 'error' : 'success';
  toast.setAttribute('role', error ? 'alert' : 'status');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => {
    toast.hidden = true;
  }, error ? 16000 : 9000);
});

function dataOf(form) {
  return new FormData(form);
}

function string(data, key) {
  return String(data.get(key) ?? '').trim();
}

function numeric(data, key, nullable = false) {
  const text = string(data, key);
  if (!text && nullable) {
    return null;
  }
  if (!text || !Number.isFinite(Number(text))) {
    throw new Error(`${key} 必须为有限数值。`);
  }
  return Number(text);
}

function bandsFromText(text) {
  const output = {};
  if (!text.trim()) {
    return output;
  }
  for (const [index, line] of text.split(/\r?\n/).entries()) {
    if (!line.trim()) {
      continue;
    }
    const columns = line.split(',');
    if (columns.length !== 2) {
      throw new Error(`频带第 ${index + 1} 行应为 Hz,声级dB 两列。`);
    }
    const frequency = columns[0].trim();
    const level = columns[1].trim();
    if (!frequency || !Number.isFinite(Number(frequency))) {
      throw new Error(`频带第 ${index + 1} 行频率无效。`);
    }
    const key = String(Number(frequency));
    if (Object.hasOwn(output, key)) {
      throw new Error(`频带第 ${index + 1} 行频率重复，未应用输入。`);
    }
    if (['', 'NA', 'N/A', 'null'].includes(level)) {
      output[key] = null;
    } else if (!Number.isFinite(Number(level))) {
      throw new Error(`频带第 ${index + 1} 行声级无效。`);
    } else {
      output[key] = Number(level);
    }
  }
  return output;
}

function itemIdentity(form) {
  return form.dataset.id || undefined;
}

async function submitMetadata(form) {
  const data = dataOf(form);
  await vm.command('metadata', {
    name: string(data, 'name'),
    owner: string(data, 'owner'),
    description: string(data, 'description'),
  }, '项目信息已应用。');
}

async function submitSite(form) {
  const data = dataOf(form);
  await vm.saveItem('sites', {
    id: itemIdentity(form),
    name: string(data, 'name'),
    zone: string(data, 'zone'),
    material: string(data, 'material'),
    procedure: string(data, 'procedure'),
    notes: string(data, 'notes'),
  });
}

async function submitCondition(form) {
  const data = dataOf(form);
  const condition = {
    id: itemIdentity(form),
    name: string(data, 'name'),
    notes: string(data, 'notes'),
    independentBands: data.has('independentBands'),
  };
  const textFields = [
    'vehicle',
    'microphone',
    'excitation',
    'surface',
    'gear',
    'weighting',
    'timeWeighting',
    'bandType',
    'instrument',
    'calibration',
    'unit',
  ];
  for (const key of textFields) {
    condition[key] = string(data, key);
  }
  for (const key of ['speed', 'rpm', 'temperature', 'duration']) {
    condition[key] = numeric(data, key, true);
  }
  await vm.saveItem('conditions', condition);
}

async function submitTrial(form) {
  const data = dataOf(form);
  const trial = {
    id: itemIdentity(form),
    name: string(data, 'name'),
    type: string(data, 'type'),
    conditionId: string(data, 'conditionId'),
    siteIds: data.getAll('siteIds'),
    timestamp: string(data, 'timestamp'),
    repeatGroup: string(data, 'repeatGroup'),
    sourceKind: string(data, 'sourceKind'),
    sourceNote: string(data, 'sourceNote'),
    operator: string(data, 'operator'),
    notes: string(data, 'notes'),
    bands: bandsFromText(string(data, 'bandsText')),
  };
  await vm.saveItem('trials', trial);
}

async function submitPair(form) {
  const data = dataOf(form);
  await vm.saveItem('pairs', {
    id: itemIdentity(form),
    baselineId: string(data, 'baselineId'),
    targetId: string(data, 'targetId'),
    purpose: string(data, 'purpose'),
    notes: string(data, 'notes'),
  });
}

async function submitParameters(form) {
  const data = dataOf(form);
  const payload = {};
  for (const key of Object.keys(vm.project.parameters)) {
    payload[key] = numeric(data, key);
  }
  await vm.command('parameters', payload, '参数已应用，全部结果已重新计算。');
}

async function submitImport(form) {
  const data = dataOf(form);
  vm.csvText = String(data.get('csvText') || '');
  vm.csvMode = string(data, 'csvMode');
  await vm.previewCsv();
}

async function submitScan(form) {
  const data = dataOf(form);
  const low = numeric(data, 'scanLow', true);
  const high = numeric(data, 'scanHigh', true);
  if (low !== null && high !== null && low > high) {
    throw new Error('频带下限不得大于上限。');
  }
  vm.scanLow = low;
  vm.scanHigh = high;
  vm.formDirty = false;
  vm.emit();
}

const submitters = {
  'metadata-form': submitMetadata,
  'site-form': submitSite,
  'condition-form': submitCondition,
  'trial-form': submitTrial,
  'pair-form': submitPair,
  'parameters-form': submitParameters,
  'import-form': submitImport,
  'scan-form': submitScan,
};

app.addEventListener('submit', async event => {
  event.preventDefault();
  if (vm.busy) {
    return;
  }
  const form = event.target;
  try {
    await submitters[form.id]?.(form);
  } catch (error) {
    vm.notify(error.message, true);
  }
});

app.addEventListener('input', event => {
  const control = event.target;
  if (control.closest('form')) {
    vm.formDirty = true;
  }
  if (control.name === 'csvText' || control.name === 'csvMode') {
    vm.csvText = app.querySelector('[name="csvText"]').value;
    vm.csvMode = app.querySelector('[name="csvMode"]').value;
    vm.csvPreview = null;
    const apply = app.querySelector('[data-action="apply-csv"]');
    if (apply) {
      apply.disabled = true;
    }
  }
});

app.addEventListener('change', event => {
  const control = event.target;
  if (control.name === 'trialFilter') {
    if (!vm.mayDiscardForm()) {
      control.value = vm.trialFilter;
      return;
    }
    vm.trialFilter = control.value;
    vm.formDirty = false;
    vm.emit();
  }
  if (control.name === 'pairFilter') {
    vm.pairFilter = control.value;
    vm.emit();
  }
});

app.addEventListener('keydown', event => {
  if (event.target.id === 'trial-search' && event.key === 'Enter') {
    event.preventDefault();
    if (!vm.mayDiscardForm()) {
      return;
    }
    vm.trialSearch = event.target.value;
    vm.formDirty = false;
    vm.emit();
  }
});

const actions = {
  navigate: button => vm.navigate(button.dataset.page),
  select: button => vm.select(button.dataset.collection, button.dataset.id),
  delete: button => vm.deleteItem(button.dataset.collection, button.dataset.id),
  new: () => vm.newProject(),
  demo: () => vm.loadDemo(),
  open: () => document.querySelector('#open-file').click(),
  save: () => vm.export('project'),
  export: button => vm.export(button.dataset.kind),
  clone: () => {
    if (vm.mayDiscardForm()) {
      return vm.command('clone', {}, '已生成独立项目副本。');
    }
  },
  archive: () => {
    if (vm.formDirty) {
      vm.notify('请先应用或取消表单输入，再归档项目。', true);
      return;
    }
    if (window.confirm('归档后项目只读，可在项目页解除归档。继续？')) {
      return vm.command('archive', {}, '项目已归档。');
    }
  },
  restore: () => vm.command('restore', {}, '已解除归档。'),
  'choose-csv': () => document.querySelector('#csv-file').click(),
  'apply-csv': () => vm.applyCsv(),
  'duplicate-trial': button => {
    if (vm.mayDiscardForm()) {
      return vm.duplicateTrial(button.dataset.id);
    }
  },
  'record-analysis': () => {
    if (vm.formDirty) {
      vm.notify('请先应用表单，复核记录只针对已应用的数据和参数。', true);
      return;
    }
    return vm.recordAnalysis();
  },
  'reset-scan': () => {
    vm.scanLow = null;
    vm.scanHigh = null;
    vm.formDirty = false;
    vm.emit();
  },
};

app.addEventListener('click', async event => {
  const button = event.target.closest('[data-action]');
  if (!button || button.disabled || vm.busy) {
    return;
  }
  try {
    await actions[button.dataset.action]?.(button);
  } catch (error) {
    vm.notify(error.message, true);
  }
});

document.querySelector('#open-file').addEventListener('change', async event => {
  await vm.openFile(event.target.files[0]);
  event.target.value = '';
});

document.querySelector('#csv-file').addEventListener('change', async event => {
  const file = event.target.files[0];
  if (!file) {
    return;
  }
  try {
    vm.csvText = await readCsv(file);
    vm.csvPreview = null;
    vm.formDirty = true;
    vm.emit();
    vm.notify(`已读取 ${file.name}，请预检后导入。`);
  } catch (error) {
    vm.notify(error.message, true);
  } finally {
    event.target.value = '';
  }
});

window.addEventListener('beforeunload', event => {
  if (vm.dirty || vm.formDirty) {
    event.preventDefault();
    event.returnValue = '';
  }
});

update();
await vm.initialize();
