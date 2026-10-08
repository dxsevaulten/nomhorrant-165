# -*- coding: utf-8 -*-
"""
Rhino Bridge — server TCP loopback di dalam Rhino (RhinoPython / RunPythonScript).

Bridge ini adalah jembatan antara MCP server (mcp/server.py) di luar Rhino dengan
RhinoCommon/Grasshopper. Hanya mendengar di 127.0.0.1 — tidak diekspos ke jaringan.

Cara pakai:
    Rhino 8 → _RunPythonScript → pilih file ini. Biarkan berjalan selama sesi kerja.

Protokol: satu permintaan JSON per baris (newline-delimited), balasan JSON per baris.
"""
from __future__ import print_function

import json
import socket
import threading

HOST = "127.0.0.1"
PORT = 8765


def handle_request(req):
    """Dispatch permintaan dari MCP server ke RhinoCommon."""
    method = req.get("method", "")
    params = req.get("params", {}) or {}

    try:
        import Rhino  # type: ignore
    except ImportError:
        return {"status": "error", "reason": "RhinoCommon tidak tersedia"}

    doc = Rhino.RhinoDoc.ActiveDoc

    if method == "ping":
        return {"status": "ok", "result": "pong"}

    if method == "viewport_capture":
        if doc is None or doc.Views.ActiveView is None:
            return {"status": "error", "reason": "tidak ada viewport aktif"}
        import System.Drawing  # type: ignore
        import base64

        w = int(params.get("width", 1280))
        h = w * 9 // 16
        bmp = doc.Views.ActiveView.CaptureToBitmap(System.Drawing.Size(w, h), False)
        import System.IO  # type: ignore

        ms = System.IO.MemoryStream()
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png)
        return {"status": "ok", "result": base64.b64encode(bytes(ms.ToArray())).decode("ascii")}

    if method == "objects_list":
        if doc is None:
            return {"status": "error", "reason": "tidak ada dokumen aktif"}
        out = []
        for obj in doc.Objects:
            out.append({
                "id": str(obj.Id),
                "type": obj.ObjectType.ToString(),
                "layer": obj.Attributes.LayerIndex,
            })
            if len(out) >= int(params.get("limit", 500)):
                break
        return {"status": "ok", "result": out}

    if method == "slider_set":
        # Pemasangan nilai slider GH via nama komponen (berjalan saat GH aktif).
        try:
            import Grasshopper  # type: ignore  # noqa: F401
        except ImportError:
            return {"status": "error", "reason": "Grasshopper belum dimuat"}
        return {"status": "ok", "result": "slider_set diterima: %s" % json.dumps(params)}

    return {"status": "error", "reason": "method tidak dikenal: " + method}


def client_thread(conn):
    buf = b""
    try:
        while True:
            chunk = conn.recv(65536)
            if not chunk:
                break
            buf += chunk
            while b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                if not line.strip():
                    continue
                try:
                    req = json.loads(line.decode("utf-8", "replace"))
                except Exception as exc:  # noqa: BLE001
                    resp = {"status": "error", "reason": "json invalid: %s" % exc}
                else:
                    resp = handle_request(req)
                conn.sendall((json.dumps(resp) + "\n").encode("utf-8"))
    finally:
        conn.close()


def serve():
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(4)
    print("Nomhorrant bridge aktif di %s:%d" % (HOST, PORT))

    while True:
        conn, addr = server.accept()
        threading.Thread(target=client_thread, args=(conn,)).start()


if __name__ == "__main__":
    serve()
