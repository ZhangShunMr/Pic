/** Transport and file boundary. No business calculations live in views. */

export class ApiService {
  async post(route, body = {}) {
    let response;
    try {
      response = await fetch(`/api/${route}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
    } catch (error) {
      throw new Error('无法连接本地服务。请保留当前页面，确认 Python 服务仍在运行后重试。');
    }
    let data;
    try {
      data = await response.json();
    } catch (error) {
      throw new Error('服务返回无效内容，请检查终端日志。');
    }
    if (!response.ok) {
      const problem = data.error;
      throw new Error(problem ? `${problem.path}：${problem.message}` : `请求失败 ${response.status}`);
    }
    return data;
  }

  async open(file) {
    if (file.size > 12 * 1024 * 1024) {
      throw new Error('项目文件不得超过 12 MiB。');
    }
    try {
      const buffer = await file.arrayBuffer();
      const text = new TextDecoder('utf-8', { fatal: true }).decode(buffer);
      // Send text as an API string so the Python strict JSON decoder can detect
      // duplicate fields rather than losing them through browser JSON.parse.
      return await this.post('open-text', { content: text });
    } catch (error) {
      throw new Error(`打开失败：${error.message}`);
    }
  }

  async export(project, kind) {
    return this.post('export', { project, kind });
  }
}

export function download(content, filename, mime) {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30000);
}

export function filename(value) {
  return value.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_').slice(0, 100) || '声学项目';
}

export async function readCsv(file) {
  if (file.size > 5 * 1024 * 1024) {
    throw new Error('CSV 文件不得超过 5 MiB。');
  }
  const buffer = await file.arrayBuffer();
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(buffer);
  } catch (error) {
    throw new Error('CSV 必须为 UTF-8 编码。请在表格软件中另存为 CSV UTF-8 后重试。');
  }
}
