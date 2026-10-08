# Arsitektur Nomhorrant-165

## Tujuan Desain

1. **Akses penuh ke Grasshopper** — baca/tulis objek, parameter, viewport.
2. **AI sebagai kernet** — AI merencanakan (action packet), manusia menyetujui, GH mengeksekusi.
3. **UI mandiri multifungsi** — chat, attach, skill, vision, action log dalam satu panel (Evolve UI).
4. **Portabilitas** — komponen bisa dipakai via `.gha`, skrip Python, atau MCP dari agent luar.
5. **Mudah diinstal dari GitHub** — paket `.yak` + fallback skrip Python.

## Modul

```
src/Nomhorrant.Core     → logika murni (netstandard-compatible):
                          AiClient, ActionProtocol, ToolRegistry, AccessGate
src/Nomhorrant.Gh       → assembly Grasshopper (.gha): 4 komponen
src/Nomhorrant.UI       → Eto.Forms: Evolve Panel (dockable di Rhino, juga jalan standalone)
src/Nomhorrant.Mcp/     → server MCP stdio (Python, zero-dependency)
python/                 → skrip GhPython/RhinoPython (fallback tanpa kompilasi)
tools/                  → utilitas (generator hash akses, dll)
```

## Alur Data

### 1. Prompt → Geometri
```
User prompt ─► AiClient.chat(tools=registry) ─► tool_calls JSON
        ─► ActionProtocol.normalize() ─► Action Packet
        ─► Evolve UI: approve/reject per aksi
        ─► ActionRouter.Execute() ─► RhinoCommon/GH API ─► objek di kanvas
        ─► hasil (nomhorrant.result/v1) dikirim balik ke model sebagai tool result
```

### 2. Vision Loop (analisis viewport / foto referensi)
```
RhinoView.CaptureToBitmap() ─► PNG bytes ─► base64 data URL
        ─► AiClient.chat_vision(image, prompt)
        ─► saran + action packet ─► (approve) ─► eksekusi
```

### 3. MCP (agent eksternal)
```
MCP client (OpenClaw / MiMo Claw / dll)
   │  stdio JSON-RPC 2.0
   ▼
mcp/server.py  ── TCP 127.0.0.1:8765 ──►  python/rhino_bridge.py (di dalam Rhino)
                                             │
                                             ▼
                                        RhinoCommon / GH API
```

Bridge hanya loopback; tidak ada layanan yang terbuka ke jaringan publik.

## Keputusan Teknis

| Keputusan | Alasan |
|---|---|
| Eto.Forms untuk UI | Cross-platform & native di Rhino (Rhino 8 di Windows/macOS) |
| Endpoint OpenAI-compatible | Bisa dipasangkan ke berbagai provider, termasuk API MiMo |
| Action packet terstruktur | Output AI bisa divalidasi & di-approve — bukan teks bebas yang langsung dieksekusi |
| Python fallback | Jalur darurat tanpa kompilasi; mempercepat eksperimentasi di GH |
| Hash + salt untuk gerbang akses | Kode verifikasi tidak pernah tersimpan dalam bentuk plaintext |

## Perluasan: komponen mandiri baru

`gh.create_component` memungkinkan AI mencetak komponen GH baru (name/inputs/outputs/code)
sesuai kebutuhan skill kerja Anda — komponen hasil cetak disimpan sebagai
User Object di folder Grasshopper sehingga bisa dipakai ulang (portabilitas progres param).

## Batasan yang perlu diketahui

- Eksekusi aksi berjalan pada thread UI Rhino → aksi panjang perlu `RhinoApp.InvokeOnUiThread`.
- `gh.eval_python` adalah fitur berdaya tinggi — selalu di belakang persetujuan eksplisit.
- Capturing viewport di Grasshopper headless tidak tersedia; butuh sesi Rhino aktif.
