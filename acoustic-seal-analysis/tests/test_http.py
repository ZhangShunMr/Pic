"""Exercise the real HTTP transport, including browser-origin boundaries."""

import http.client
import json
import threading
import unittest
from http.server import ThreadingHTTPServer
from acoustic.server import Handler


class HttpTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.port = cls.server.server_port

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()

    def request(self, path, body=None, headers=None, method=None):
        connection = http.client.HTTPConnection('127.0.0.1', self.port, timeout=5)
        chosen_headers = {'Content-Type': 'application/json'}
        chosen_headers.update(headers or {})
        connection.request(method or ('POST' if body is not None else 'GET'), path, body=body, headers=chosen_headers)
        response = connection.getresponse()
        result = (response.status, dict(response.getheaders()), response.read())
        connection.close()
        return result

    def test_health_and_static(self):
        status, headers, body = self.request('/api/health')
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)['version'], '1.0.0')
        status, headers, body = self.request('/')
        self.assertEqual(status, 200)
        self.assertIn(b'/app.js', body)
        self.assertEqual(headers['X-Content-Type-Options'], 'nosniff')
        self.assertIn("frame-ancestors 'none'", headers['Content-Security-Policy'])

    def test_real_command_lifecycle(self):
        status, _, body = self.request('/api/demo', '{}')
        self.assertEqual(status, 200)
        project = json.loads(body)['project']
        payload = json.dumps({'project': project, 'action': 'metadata', 'payload': {'name': 'HTTP验收'}})
        status, _, body = self.request('/api/command', payload.encode())
        self.assertEqual(status, 200)
        result = json.loads(body)
        self.assertEqual(result['project']['name'], 'HTTP验收')
        self.assertEqual(result['analysis']['pairs'][0]['bands'][0]['difference'], 3)

    def test_cross_origin_and_host_rejected(self):
        self.assertEqual(self.request('/api/demo', '{}', {'Origin': 'https://untrusted.example'})[0], 403)
        self.assertEqual(self.request('/api/health', headers={'Host': 'untrusted.example'})[0], 403)

    def test_nonjson_and_invalid_json_rejected(self):
        self.assertEqual(self.request('/api/new', '{}', {'Content-Type': 'text/plain'})[0], 422)
        for text in ['{bad', '{"x":NaN}', '{"x":1,"x":2}', '[]']:
            with self.subTest(text=text):
                self.assertEqual(self.request('/api/new', text)[0], 422)

    def test_unknown_route_and_traversal(self):
        self.assertEqual(self.request('/api/unknown', '{}')[0], 422)
        self.assertEqual(self.request('/../acoustic/models.py')[0], 404)
        self.assertEqual(self.request('/not-found')[0], 404)


if __name__ == '__main__':
    unittest.main()
