#!/usr/bin/env python3
"""Day 17 local HTTP API used by the Unity login demo."""

import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse


PLAYER_ID = "player-1001"
TOKEN = "demo-token-player-1001"


class MockApiHandler(BaseHTTPRequestHandler):
    server_version = "GameDemoMockApi/1.0"
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        path = urlparse(self.path).path

        if path == "/health":
            self._write_json(200, {"status": "ok"})
            return

        if path == f"/api/player/{PLAYER_ID}":
            if self._should_fail_temporarily():
                return

            self._delay_if_needed()

            if self.headers.get("Authorization") != f"Bearer {TOKEN}":
                self._write_json(401, {"code": "INVALID_TOKEN", "message": "登录状态已失效，请重新登录。"})
                return

            self._write_success({
                "id": PLAYER_ID,
                "nickname": "ShadowHunter",
                "level": 17,
                "experience": 1680,
            })
            return

        self._write_json(404, {"code": "NOT_FOUND", "message": "接口不存在。"})

    def do_POST(self):
        path = urlparse(self.path).path

        if path != "/api/login":
            self._write_json(404, {"code": "NOT_FOUND", "message": "接口不存在。"})
            return

        if self._should_fail_temporarily():
            return

        self._delay_if_needed()

        try:
            length = int(self.headers.get("Content-Length", "0"))
            body = json.loads(self.rfile.read(length).decode("utf-8"))
        except (ValueError, UnicodeDecodeError, json.JSONDecodeError):
            self._write_json(400, {"code": "INVALID_JSON", "message": "请求 JSON 格式错误。"})
            return

        if body.get("account") != "demo" or body.get("password") != "123456":
            self._write_json(401, {"code": "INVALID_CREDENTIALS", "message": "账号或密码错误。"})
            return

        self._write_success({"token": TOKEN, "playerId": PLAYER_ID, "expiresInSeconds": 3600})

    def _should_fail_temporarily(self):
        self.server.api_request_count += 1

        if self.server.api_request_count <= self.server.fail_first:
            self._write_json(500, {"code": "TEMPORARY_FAILURE", "message": "模拟服务器临时故障。"})
            return True

        return False

    def _delay_if_needed(self):
        if self.server.delay_seconds > 0:
            time.sleep(self.server.delay_seconds)

    def _write_success(self, data):
        if self.server.malformed_json:
            self._write_bytes(200, b'{"malformed":')
            return

        self._write_json(200, data)

    def _write_json(self, status_code, data):
        payload = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self._write_bytes(status_code, payload)

    def _write_bytes(self, status_code, payload):
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()

        try:
            self.wfile.write(payload)
        except OSError:
            pass

    def log_message(self, format_string, *args):
        print(f"[{self.log_date_time_string()}] {format_string % args}")


def parse_args():
    parser = argparse.ArgumentParser(description="GameDemo Day 17 local mock API")
    parser.add_argument("--port", type=int, default=8080, help="listening port (default: 8080)")
    parser.add_argument("--delay", type=float, default=0, help="delay every API response by N seconds")
    parser.add_argument("--fail-first", type=int, default=0, help="return HTTP 500 for the first N API requests")
    parser.add_argument("--malformed-json", action="store_true", help="return malformed JSON for successful requests")
    return parser.parse_args()


def main():
    args = parse_args()
    server = ThreadingHTTPServer(("127.0.0.1", args.port), MockApiHandler)
    server.delay_seconds = max(0, args.delay)
    server.fail_first = max(0, args.fail_first)
    server.malformed_json = args.malformed_json
    server.api_request_count = 0

    print(f"Mock API: http://127.0.0.1:{args.port}")
    print("Account: demo / 123456")
    print(f"delay={server.delay_seconds}s, fail_first={server.fail_first}, malformed_json={server.malformed_json}")
    print("Press Ctrl+C to stop.")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping mock API...")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
