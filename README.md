# Nomhorrant-165

**Plugin Grasshopper untuk Rhino 8 dengan asisten AI terintegrasi ("Evolve UI").**

Nomhorrant-165 menghubungkan kerja parametrik Grasshopper dengan AI mandiri lewat API
(function calling / tool use). AI tidak hanya membalas teks — ia menghasilkan
**action packet JSON** yang bisa langsung dijalankan ke kanvas GH, membaca viewport 3D
secara vision, menganalisis foto referensi, dan menjalankan skill/komponen tambahan.

> Pola kerjanya seperti **kernet**: AI menganalisis & merencanakan, Anda menyetujui,
> Grasshopper mengeksekusi. Setiap aksi punya tombol approve/reject.

---

## Fitur

| Lapisan | Fungsi |
|---|---|
| **Komponen GH** (`Nomhorrant.Gh`) | `AI Connector`, `AI Vision`, `Action Router`, `Skill Runner` — akses penuh kanvas GH dari script |
| **Evolve UI** (`Nomhorrant.UI`) | Panel dockable mandiri ala IDE: chat, attach file/foto, skill bar, vision capture, log aksi, approve/reject |
| **MCP Bridge** (`mcp/`) | Server Model Context Protocol (stdio, JSON-RPC 2.0) → agent AI luar (OpenClaw/MiMo Claw, dll) bisa mengendalikan Rhino/GH |
| **Python fallback** (`python/`) | Skrip GhPython/RhinoPython siap tempel — jalan tanpa kompilasi, cocok untuk eksperimen cepat |
| **Action Protocol** | Skema JSON `nomhorrant.action/v1` — AI membalas data parameter pengendali geometri, bukan teks panjang |

## Arsitektur Singkat

```
┌────────────────────────────┐        ┌───────────────────────────────┐
│  Evolve UI (Eto / standalone)│◄─────►│  Nomhorrant.Core              │
│  chat · skills · vision      │       │  AiClient · ActionProtocol    │
└──────────────┬───────────────┘       │  ToolRegistry · AccessGate    │
               │                       └──────────────┬────────────────┘
┌──────────────▼───────────────┐                      │
│  Grasshopper canvas          │◄─────────────────────┤
│  AI Connector · Action Router│                      │
└──────────────┬───────────────┘       ┌──────────────▼────────────────┐
               │                       │  MCP server (stdio)           │
               ▼                       │  ↔ TCP bridge 127.0.0.1:8765  │
       Rhino viewport / geometry       └───────────────────────────────┘
```

Detail lengkap: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
Protokol JSON: [`docs/JSON_PROTOCOL.md`](docs/JSON_PROTOCOL.md).
Instalasi: [`docs/INSTALL.md`](docs/INSTALL.md).

## Instalasi Cepat

1. Unduh `Nomhorrant-165-x.y.z.yak` dari halaman **Releases** repo ini.
2. Buka Rhino 8 → ketik `_PackageManager` → tab *Packages* → *Install from file* → pilih file `.yak`.
3. Restart Rhino, buka Grasshopper → tab **Nomhorrant**.
4. Set endpoint AI (lihat [Konfigurasi](#konfigurasi)).

Alternatif tanpa kompilasi: tempel skrip di `python/` ke komponen GhPython (detail di INSTALL.md).

## Konfigurasi

Kredensial **tidak pernah** disimpan di repo. Sediakan lewat environment variable:

```bash
NOMHORRANT_API_BASE=https://<endpoint-anda>/v1     # endpoint OpenAI-compatible
NOMHORRANT_API_KEY=<kunci-api-anda>                # rahasia — jangan commit
NOMHORRANT_MODEL=<model-anda>
```

Atau lewat file lokal `config.local.json` (sudah di-`.gitignore`):

```json
{ "apiBase": "...", "apiKey": "...", "model": "...", "accessHash": "..." }
```

### Gerbang akses (verification code)

Evolve UI bisa dikunci dengan kode verifikasi. Yang disimpan hanyalah
**SHA-256 + salt**, bukan kodenya:

```bash
python3 tools/gen_access_hash.py        # ikuti prompt, hasilkan hash
# tempel hasilnya ke config.local.json → "accessHash"
```

## Contoh Pemakaian

1. **Prompt → geometri**: tarik `AI Connector`, isi prompt
   *"buat spiral 5 putaran, radius 12.5, tinggi 3"* → komponen menghasilkan
   action packet → sambungkan ke `Action Router` → keluar tree parameter
   (turns/radius/height) yang bisa dipasang ke slider & komponen spiral.
2. **Vision loop**: `AI Vision` menangkap viewport Rhino → kirim ke model multimodal →
   balasan berupa saran revisi + action packet baru → jalankan lewat router.
3. **Foto referensi**: attach gambar lanskap ke Evolve UI → AI mengusulkan
   cluster komponen GH yang cocok → terima untuk dicetak ke kanvas.
4. **Agent luar (MCP)**: jalankan `python mcp/server.py`, daftarkan sebagai MCP server
   di klien AI Anda → agent bisa `gh_eval_python`, `gh_set_param`, `viewport_capture`.

## Pengembangan

```bash
dotnet build Nomhorrant-165.sln -c Release      # butuh Rhino 8 terpasang (Windows)
python3 -m py_compile python/*.py mcp/server.py tools/*.py   # cek sintaks python
```

## Keamanan

- API key/token: env var atau `config.local.json` (gitignored) — jangan pernah di-hardcode.
- Bridge MCP hanya mendengar di `127.0.0.1` (loopback), tidak diekspos ke jaringan.
- Eksekusi aksi selalu butuh persetujuan eksplisit di UI (mode *co-driver*).
- Repo ini **private**. Akses GitHub diatur lewat undangan akun, bukan password repo.

## Lisensi

MIT — lihat [`LICENSE`](LICENSE).
