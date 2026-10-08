# -*- coding: utf-8 -*-
"""
AI Vision — tangkap viewport Rhino / analisis foto referensi lewat API multimodal.

Inputs (GH):
    prompt   [str]   pertanyaan analisis
    image    [str]   path file gambar referensi (opsional; kosong = viewport)
    run      [bool]  True untuk menjalankan
    width    [int]   lebar tangkapan viewport (pixel)

Outputs (GH):
    analysis [str]   hasil analisis AI
    packet   [str]   action packet saran (JSON)
    png      [str]   base64 PNG hasil tangkapan
    error    [str]   pesan error
"""
from __future__ import print_function

import base64
import json
import os
import tempfile

from ai_connector import chat_completion, normalize_packet, extract_json  # noqa: E402


def capture_viewport(width=1280):
    """Tangkap viewport Rhino aktif → bytes PNG. Mengembalikan None di luar Rhino."""
    try:
        import Rhino  # type: ignore
        import System.Drawing  # type: ignore
    except ImportError:
        return None

    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None or doc.Views.ActiveView is None:
        return None

    w = max(64, int(width))
    h = w * 9 // 16
    bmp = doc.Views.ActiveView.CaptureToBitmap(System.Drawing.Size(w, h), False)
    if bmp is None:
        return None

    path = os.path.join(tempfile.gettempdir(), "nomhorrant_viewport.png")
    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png)
    with open(path, "rb") as f:
        return f.read()


def run(prompt, image_path="", run_flag=True, width=1280):
    if not run_flag:
        return "", "", "", ""
    try:
        if image_path:
            with open(image_path, "rb") as f:
                raw = f.read()
        else:
            raw = capture_viewport(width)
            if raw is None:
                return "", "", "", "viewport tidak tersedia (di luar Rhino?)"

        data_url = "data:image/png;base64," + base64.b64encode(raw).decode("ascii")
        effective = prompt or (
            "Analisis geometri ini. Berikan penilaian struktur, lalu action packet "
            'nomhorrant.action/v1: {"protocol":"nomhorrant.action/v1","intent":"...","actions":[...]}'
        )
        response = chat_completion([
            {
                "role": "user",
                "content": [
                    {"type": "text", "text": effective},
                    {"type": "image_url", "image_url": {"url": data_url, "detail": "high"}},
                ],
            }
        ])

        text = ""
        try:
            text = response["choices"][0]["message"].get("content", "") or ""
        except (KeyError, IndexError, TypeError):
            pass

        packet = normalize_packet(text)
        return text, json.dumps(packet, indent=2), base64.b64encode(raw).decode("ascii"), ""
    except Exception as exc:  # noqa: BLE001
        return "", "", "", str(exc)


analysis, packet, png, error = run(prompt or "", image or "", run, width if width else 1280)
