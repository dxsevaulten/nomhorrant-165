#!/usr/bin/env python3
"""
Nomhorrant-165 MCP Server — Model Context Protocol (stdio, JSON-RPC 2.0).

Mengekspos Rhino/Grasshopper sebagai tools untuk agent AI eksternal
(OpenClaw / MiMo Claw / klien MCP lain). Zero dependency — hanya stdlib Python.

Dijalankan oleh klien MCP sebagai subprocess:
    python3 mcp/server.py

Berkomunikasi dengan Rhino lewat python/rhino_bridge.py (TCP 127.0.0.1:8765).
"""
from __future__ import annotations

import json
import os
import socket
import sys

PROTOCOL_VERSION = "2024-11-05"
BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = int(os.environ.get("NOMHORRANT_BRIDGE_PORT", "8765"))

TOOLS = [
    {
        "name": "gh_eval_python",
        "description": "Eksekusi skrip Python Rhino (butuh persetujuan pengguna di sisi Rhino).",
        "inputSchema": {
            "type": "object",
            "properties": {"code": {"type": "string", "description": "kode Python/RhinoScript"}},
            "required": ["code"],
        },
    },
    {
        "name": "gh_get_objects",
        "description": "Daftar objek di dokumen Rhino aktif.",
        "inputSchema": {
            "type": "object",
            "properties": {"limit": {"type": "integer", "description": "maksimum objek (default 500)"}},
        },
    },
    {
        "name": "viewport_capture",
        "description": "Tangkap viewport Rhino aktif menjadi PNG (base64).",
        "inputSchema": {
            "type": "object",
            "properties": {"width": {"type": "integer", "description": "lebar pixel (default 1280)"}},
        },
    },
    {
        "name": "gh_set_param",
        "description": "Set parameter/slider Grasshopper bernama.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "slider": {"type": "string"},
                "value": {"type": "number"},
            },
            "required": ["slider", "value"],
        },
    },
    {
        "name": "gh_open_file",
        "description": "Buka file .3dm/.gh (dijalankan di sisi Rhino).",
        "inputSchema": {
            "type": "object",
            "properties": {"path": {"type": "string"}},
            "required": ["path"],
        },
    },
]


def bridge_call(method: str, params: dict) -> dict:
    """Kirim satu permintaan ke bridge Rhino (newline-delimited JSON)."""
    payload = (json.dumps({"method": method, "params": params}) + "\n").encode("utf-8")
    with socket.create_connection((BRIDGE_HOST, BRIDGE_PORT), timeout=10) as sock:
        sock.sendall(payload)
        buf = b""
        while b"\n" not in buf:
            chunk = sock.recv(65536)
            if not chunk:
                break
            buf += chunk
    if not buf:
        return {"status": "error", "reason": "bridge tidak membalas"}
    return json.loads(buf.split(b"\n", 1)[0].decode("utf-8", "replace"))


def call_tool(name: str, arguments: dict) -> dict:
    if name == "viewport_capture":
        return bridge_call("viewport_capture", {"width": arguments.get("width", 1280)})
    if name == "gh_get_objects":
        return bridge_call("objects_list", {"limit": arguments.get("limit", 500)})
    if name == "gh_set_param":
        return bridge_call("slider_set", {"slider": arguments.get("slider"), "value": arguments.get("value")})
    if name == "gh_open_file":
        return bridge_call("file_open", {"path": arguments.get("path")})
    if name == "gh_eval_python":
        # Eksekusi kode adalah aksi berdaya tinggi — hanya diteruskan, eksekusi
        # nyata tetap berada di sisi Rhino dengan persetujuan pengguna.
        return bridge_call("eval_python", {"code": arguments.get("code", "")})
    return {"status": "error", "reason": "tool tidak dikenal: " + name}


def make_result(payload: dict) -> dict:
    text = json.dumps(payload, ensure_ascii=False, indent=2)
    return {"content": [{"type": "text", "text": text}], "isError": payload.get("status") == "error"}


def handle(request: dict) -> dict | None:
    method = request.get("method", "")
    req_id = request.get("id")

    if method == "initialize":
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "result": {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "nomhorrant-165", "version": "0.1.0"},
            },
        }

    if method == "notifications/initialized":
        return None  # notifikasi tanpa balasan

    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": req_id, "result": {"tools": TOOLS}}

    if method == "tools/call":
        params = request.get("params", {}) or {}
        name = params.get("name", "")
        args = params.get("arguments", {}) or {}
        try:
            result = call_tool(name, args)
        except Exception as exc:  # noqa: BLE001
            result = {"status": "error", "reason": str(exc)}
        return {"jsonrpc": "2.0", "id": req_id, "result": make_result(result)}

    if method == "ping":
        return {"jsonrpc": "2.0", "id": req_id, "result": {}}

    if req_id is not None:
        return {
            "jsonrpc": "2.0",
            "id": req_id,
            "error": {"code": -32601, "message": "method tidak dikenal: " + method},
        }
    return None


def main() -> None:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            request = json.loads(line)
        except json.JSONDecodeError:
            continue
        response = handle(request)
        if response is not None:
            sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
            sys.stdout.flush()


if __name__ == "__main__":
    main()
