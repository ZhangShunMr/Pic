"""Loopback-only, stateless application server with bounded JSON requests."""

import argparse
import json
import mimetypes
import sys
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

from acoustic import SOFTWARE_NAME, VERSION
from acoustic.analysis import analyze
from acoustic.commands import execute
from acoustic.demo import demonstration
from acoustic.imports import csv_preview, measurements_csv, results_csv
from acoustic.models import ValidationError, empty_project, validate_project
from acoustic.report import report_html

WEB_ROOT = Path(__file__).resolve().parent.parent / "web"
MAX_REQUEST = 12 * 1024 * 1024


def reject_constant(value):
    raise ValidationError(f"不允许非有限 JSON 数值 {value}", "json")


def reject_duplicate_fields(pairs):
    output = {}
    for key, value in pairs:
        if key in output:
            raise ValidationError(f"重复 JSON 字段 {key}", "json")
        output[key] = value
    return output


def load_json(text):
    try:
        return json.loads(text, parse_constant=reject_constant, object_pairs_hook=reject_duplicate_fields)
    except json.JSONDecodeError as error:
        raise ValidationError(f"JSON 第 {error.lineno} 行第 {error.colno} 列格式错误", "json") from error


def dispatch(route, body):
    if not isinstance(body, dict):
        raise ValidationError("请求必须为 JSON 对象", "request")
    if route == "/api/new":
        project = empty_project()
        return {"project": project, "analysis": analyze(project)}
    if route == "/api/demo":
        project = demonstration()
        return {"project": project, "analysis": analyze(project)}
    if route == "/api/open-text":
        text = body.get("content")
        if not isinstance(text, str):
            raise ValidationError("项目内容须为文本", "content")
        project = validate_project(load_json(text))
        return {"project": project, "analysis": analyze(project)}
    if route == "/api/open":
        project = validate_project(body.get("project"))
        return {"project": project, "analysis": analyze(project)}
    if route == "/api/command":
        return execute(body.get("project"), body.get("action"), body.get("payload"))
    if route == "/api/analyze":
        return {"analysis": analyze(body.get("project"))}
    if route == "/api/import-preview":
        project = validate_project(body.get("project"))
        return {"preview": csv_preview(project, body.get("content"), body.get("mode"))}
    if route == "/api/export":
        project = validate_project(body.get("project"))
        kind = body.get("kind")
        analysis = analyze(project)
        if kind == "report":
            return {"content": report_html(project), "extension": ".report.html", "mime": "text/html;charset=utf-8"}
        if kind == "results":
            return {"content": results_csv(project, analysis), "extension": ".results.csv", "mime": "text/csv;charset=utf-8"}
        if kind == "measurements":
            return {"content": measurements_csv(project), "extension": ".measurements.csv", "mime": "text/csv;charset=utf-8"}
        if kind == "project":
            return {"content": json.dumps(project, ensure_ascii=False, indent=2, allow_nan=False), "extension": ".aslp.json", "mime": "application/json;charset=utf-8"}
        raise ValidationError("未知导出类型", "kind")
    raise ValidationError("未知接口", "route")


class Handler(BaseHTTPRequestHandler):
    server_version = "AcousticSeal/1.0"

    def allowed_request(self):
        expected = f"127.0.0.1:{self.server.server_port}"
        if self.headers.get("Host") not in (expected, f"localhost:{self.server.server_port}"):
            self.respond(403, {"error": {"path": "host", "message": "仅接受本机地址访问"}})
            return False
        origin = self.headers.get("Origin")
        if origin and origin not in (f"http://{expected}", f"http://localhost:{self.server.server_port}"):
            self.respond(403, {"error": {"path": "origin", "message": "拒绝跨站请求"}})
            return False
        return True

    def headers_for(self, status, content_type, size):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(size))
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'")
        self.end_headers()

    def respond(self, status, payload):
        data = json.dumps(payload, ensure_ascii=False, allow_nan=False).encode("utf-8")
        self.headers_for(status, "application/json;charset=utf-8", len(data))
        self.wfile.write(data)

    def do_GET(self):
        if not self.allowed_request():
            return
        route = urlparse(self.path).path
        if route == "/api/health":
            self.respond(200, {"name": SOFTWARE_NAME, "version": VERSION, "status": "ready"})
            return
        if route == "/":
            route = "/index.html"
        target = (WEB_ROOT / route.lstrip("/")).resolve()
        if WEB_ROOT not in target.parents or not target.is_file():
            self.respond(404, {"error": {"path": "route", "message": "文件不存在"}})
            return
        content = target.read_bytes()
        content_type = mimetypes.guess_type(target)[0] or "application/octet-stream"
        if content_type.startswith("text/") or target.suffix == ".js":
            content_type += ";charset=utf-8"
        self.headers_for(200, content_type, len(content))
        self.wfile.write(content)

    def do_POST(self):
        if not self.allowed_request():
            return
        try:
            if self.headers.get("Content-Type", "").split(";")[0] != "application/json":
                raise ValidationError("请求须使用 application/json", "contentType")
            length = int(self.headers.get("Content-Length", "0"))
            if not 0 < length <= MAX_REQUEST:
                raise ValidationError("请求必须为 1 字节至 12 MiB", "contentLength")
            if self.headers.get("Transfer-Encoding"):
                raise ValidationError("不支持分块请求", "transferEncoding")
            text = self.rfile.read(length).decode("utf-8")
            payload = load_json(text)
            result = dispatch(urlparse(self.path).path, payload)
            self.respond(200, result)
        except ValidationError as error:
            self.respond(422, {"error": error.as_dict()})
        except (UnicodeDecodeError, ValueError, TypeError, KeyError) as error:
            self.respond(422, {"error": {"path": "request", "message": "请求结构或编码无效：" + str(error)[:200]}})
        except Exception:
            traceback.print_exc()
            self.respond(500, {"error": {"path": "server", "message": "服务内部错误，请保留项目文件与终端日志"}})

    def log_message(self, message, *args):
        # No request bodies, project content or credentials are logged.
        sys.stderr.write(f"{self.log_date_time_string()} {message % args}\n")


def main():
    parser = argparse.ArgumentParser(description=SOFTWARE_NAME)
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    if not 1024 <= args.port <= 65535:
        parser.error("端口允许 1024—65535")
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.daemon_threads = True
    print(f"{SOFTWARE_NAME} V{VERSION}", flush=True)
    print(f"打开 http://127.0.0.1:{args.port}；按 Ctrl+C 退出。项目须显式下载保存。", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("服务已停止。")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
