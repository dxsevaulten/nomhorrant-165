# MCP Bridge Nomhorrant-165

Server MCP (Model Context Protocol) untuk menghubungkan agent AI eksternal
(OpenClaw, MiMo Claw, Claude Desktop, klien MCP lain) dengan Rhino 8 / Grasshopper.

## Komponen

| File | Peran |
|---|---|
| `server.py` | Server MCP stdio (JSON-RPC 2.0, zero dependency) |
| `../python/rhino_bridge.py` | Bridge TCP loopback yang berjalan di dalam Rhino |

## Tools yang diekspos

- `gh_eval_python` — eksekusi skrip RhinoPython (selalu di belakang persetujuan pengguna)
- `gh_get_objects` — daftar objek di dokumen Rhino
- `viewport_capture` — tangkap viewport → PNG base64 (bahan vision AI)
- `gh_set_param` — set nilai slider/parameter GH
- `gh_open_file` — buka file `.3dm` / `.gh`

## Aktivasi

1. Rhino 8 → `_RunPythonScript` → jalankan `python/rhino_bridge.py`
   (mendengar di `127.0.0.1:8765` saja).
2. Daftarkan server di klien MCP:

```json
{
  "mcpServers": {
    "nomhorrant-165": {
      "command": "python3",
      "args": ["/path/ke/nomhorrant-165/mcp/server.py"]
    }
  }
}
```

3. Selesai — tool langsung muncul di klien MCP Anda.

## Catatan keamanan

- Bridge hanya di loopback; tidak ada port yang dibuka ke jaringan.
- `gh_eval_python` bersifat powerful — permintaannya diteruskan apa adanya,
  eksekusi tetap berada di sesi Rhino Anda dengan persetujuan.
- Tidak ada kredensial yang disimpan di modul ini.
