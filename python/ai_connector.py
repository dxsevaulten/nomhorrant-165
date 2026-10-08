# -*- coding: utf-8 -*-
"""
AI Connector — komponen GhPython/RhinoPython untuk Grasshopper (Rhino 8).
Tempel skrip ini ke komponen GhPython (atau Python 3) — tanpa kompilasi assembly.

Inputs (GH):
    prompt   [str]   instruksi untuk AI
    context  [str]   konteks kanvas (opsional)
    run      [bool]  True untuk memanggil API
    system   [str]   system prompt tambahan (opsional)

Outputs (GH):
    packet   [str]   action packet JSON (nomhorrant.action/v1)
    intent   [str]   ringkasan rencana
    tools    [str]   daftar tool terpakai, satu per baris
    error    [str]   pesan error

Konfigurasi: env var NOMHORRANT_API_BASE / NOMHORRANT_API_KEY / NOMHORRANT_MODEL.
"""
from __future__ import print_function

import json
import os
import base64

try:  # Rhino 8 (IronPython / CPython via RhinoScriptSyntax tidak wajib)
    import urllib.request as _urllib
    _urlopen = _urllib.urlopen
    _Request = _urllib.Request
except ImportError:  # IronPython 2.7 lama
    import urllib2 as _urllib
    _urlopen = _urllib.urlopen
    _Request = _urllib.Request

PROTOCOL = "nomhorrant.action/v1"

SYSTEM_PROMPT = (
    "Anda adalah co-driver Grasshopper (Rhino 8). Balas HANYA dengan action packet JSON "
    'berformat {"protocol":"nomhorrant.action/v1","intent":"...","actions":[...]}, '
    "atau panggil tool yang tersedia. Jangan membalas teks panjang."
)

TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "geometry.spiral",
            "description": "Buat kurva spiral parametrik di Grasshopper.",
            "parameters": {
                "type": "object",
                "properties": {
                    "turns": {"type": "number", "description": "jumlah putaran"},
                    "radius": {"type": "number", "description": "radius dasar"},
                    "height": {"type": "number", "description": "kenaikan tinggi total"},
                },
                "required": ["turns", "radius"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "geometry.grid",
            "description": "Buat grid titik/panel.",
            "parameters": {
                "type": "object",
                "properties": {
                    "xCount": {"type": "integer"},
                    "yCount": {"type": "integer"},
                    "size": {"type": "number"},
                },
                "required": ["xCount", "yCount", "size"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "param.set",
            "description": "Set nilai slider pada kanvas Grasshopper.",
            "parameters": {
                "type": "object",
                "properties": {
                    "slider": {"type": "string"},
                    "value": {"type": "number"},
                },
                "required": ["slider", "value"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "vision.capture_viewport",
            "description": "Tangkap viewport Rhino aktif menjadi PNG.",
            "parameters": {"type": "object", "properties": {}},
        },
    },
]

FLAT_ALIASES = {
    "create_spiral": "geometry.spiral",
    "spiral": "geometry.spiral",
    "create_grid": "geometry.grid",
    "grid": "geometry.grid",
    "set_slider": "param.set",
    "capture_viewport": "vision.capture_viewport",
}


def _api_config():
    cfg = {
        "apiBase": os.environ.get("NOMHORRANT_API_BASE", "").rstrip("/"),
        "apiKey": os.environ.get("NOMHORRANT_API_KEY", ""),
        "model": os.environ.get("NOMHORRANT_MODEL", ""),
    }
    local = os.path.join(os.path.dirname(__file__), "..", "config.local.json")
    if os.path.exists(local):
        try:
            with open(local, "r") as f:
                merged = json.load(f)
            for key in ("apiBase", "apiKey", "model"):
                if not cfg.get(key) and merged.get(key):
                    cfg[key] = merged[key]
        except Exception:
            pass
    return cfg


def extract_json(raw):
    """Ambil objek/array JSON pertama dari teks bebas (termasuk code fence)."""
    if not raw:
        return None
    text = raw.strip()
    if text.startswith("```"):
        end = text.rfind("```")
        text = text[3:end].strip()
        if text[:4].lower() == "json":
            text = text[4:].strip()
    start = -1
    for i, ch in enumerate(text):
        if ch in "{[":
            start = i
            break
    if start < 0:
        return None
    depth = 0
    in_string = False
    escaped = False
    for i in range(start, len(text)):
        ch = text[i]
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
            continue
        if ch in "{[":
            depth += 1
        elif ch in "}]":
            depth -= 1
            if depth == 0:
                return text[start : i + 1]
    return None


def normalize_packet(raw):
    """Teks balasan AI → action packet kanonik."""
    packet = {"protocol": PROTOCOL, "intent": "", "actions": [], "notes": ""}
    blob = extract_json(raw)
    if not blob:
        return packet
    try:
        data = json.loads(blob)
    except Exception:
        return packet

    if isinstance(data, dict) and isinstance(data.get("actions"), list):
        packet["intent"] = data.get("intent", "")
        packet["notes"] = data.get("notes", "")
        for act in data["actions"]:
            if isinstance(act, dict) and act.get("tool"):
                act["tool"] = FLAT_ALIASES.get(act["tool"], act["tool"])
                packet["actions"].append(act)
    elif isinstance(data, dict) and data.get("action"):
        tool = FLAT_ALIASES.get(data["action"], data["action"])
        params = {k: v for k, v in data.items() if k not in ("action", "id")}
        packet["actions"].append({"id": data.get("id", "a1"), "tool": tool, "params": params})
        packet["intent"] = tool
    return packet


def chat_completion(messages, tools=None):
    """Panggil endpoint OpenAI-compatible (chat/completions)."""
    cfg = _api_config()
    if not cfg["apiBase"] or not cfg["apiKey"]:
        raise RuntimeError("API belum dikonfigurasi: set NOMHORRANT_API_BASE / NOMHORRANT_API_KEY / NOMHORRANT_MODEL")

    payload = {"model": cfg["model"], "messages": messages}
    if tools:
        payload["tools"] = tools

    req = _Request(
        cfg["apiBase"] + "/chat/completions",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Authorization": "Bearer " + cfg["apiKey"],
        },
    )
    resp = _urlopen(req, timeout=180)
    body = resp.read()
    if isinstance(body, bytes):
        body = body.decode("utf-8", "replace")
    return json.loads(body)


def run(prompt, context="", system=""):
    """Alur utama: prompt → API → action packet (dict)."""
    content = prompt if not context else prompt + "\n\n[Konteks kanvas]\n" + context
    messages = [
        {"role": "system", "content": SYSTEM_PROMPT + (" " + system if system else "")},
        {"role": "user", "content": content},
    ]
    response = chat_completion(messages, tools=TOOLS)

    # 1) tool_calls → action packet
    try:
        message = response["choices"][0]["message"]
    except (KeyError, IndexError, TypeError):
        return normalize_packet("")

    actions = []
    for call in message.get("tool_calls") or []:
        fn = call.get("function", {})
        args_raw = fn.get("arguments", "{}")
        try:
            args = json.loads(args_raw) if args_raw else {}
        except Exception:
            args = {}
        actions.append({
            "id": call.get("id", "a%d" % (len(actions) + 1)),
            "tool": FLAT_ALIASES.get(fn.get("name", ""), fn.get("name", "")),
            "params": args,
        })

    if actions:
        return {"protocol": PROTOCOL, "intent": prompt[:80], "actions": actions, "notes": ""}

    # 2) fallback: balasan JSON langsung
    return normalize_packet(message.get("content", ""))


# ===================== Grasshopper entry point =====================
# Komponen GhPython mengekspos variabel global: prompt, context, run, system
# dan membaca hasil dari packet, intent, tools, error.

packet, intent, tools, error = "", "", "", ""
try:
    if run:
        if not prompt:
            error = "prompt kosong"
        else:
            result = run(prompt, context or "", system or "")
            packet = json.dumps(result, indent=2)
            intent = result.get("intent", "")
            tools = "\n".join(a.get("tool", "") for a in result.get("actions", []))
except Exception as exc:  # noqa: BLE001 — error dikirim balik ke kanvas
    error = str(exc)
