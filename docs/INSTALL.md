# Instalasi Nomhorrant-165 (Rhino 8)

Ada 3 jalur instalasi. Pilih sesuai kebutuhan.

## A. Paket `.yak` (disarankan)

1. Buka halaman **Releases** repo ini → unduh `Nomhorrant-165-<versi>.yak`.
2. Rhino 8 → jalankan `_PackageManager`.
3. Klik **Install from File** → pilih file `.yak` yang tadi.
4. Restart Rhino 8.
5. Buka Grasshopper → muncul tab baru **Nomhorrant** berisi 4 komponen.

Membuat paket `.yak` sendiri:

```powershell
dotnet build Nomhorrant-165.sln -c Release
cd src/Nomhorrant.Gh/bin/Release
yak build          # hasilkan .yak di folder yang sama
```

## B. Manual copy (`.gha` + dependensi)

1. `dotnet build Nomhorrant-165.sln -c Release`.
2. Salin isi `bin/Release/` (`Nomhorrant.*.dll`, `Nomhorrant.Gh.gha`) ke folder
   Grasshopper Libraries:
   - `%APPDATA%\Grasshopper\Libraries\`
3. Buka Grasshopper → **File → Special Folders → Components Folder** untuk memastikan lokasinya.
4. Jika Grasshopper menandai assembly "blocked" (file dari internet):
   klik kanan file → Properties → **Unblock** → centang → OK.

## C. Skrip Python (tanpa kompilasi)

Untuk coba-coba cepat atau lingkungan yang tidak bisa memuat assembly:

1. Buka Grasshopper → tarik komponen **GhPython** (atau **Python 3**, lebih disarankan di Rhino 8).
2. Salin isi `python/ai_connector.py` (atau `python/ai_vision.py`) ke editor skrip komponen.
3. Set input/output komponen sesuai komentar `# Inputs:` / `# Outputs:` di bagian atas skrip.
4. Set environment variable `NOMHORRANT_API_BASE`, `NOMHORRANT_API_KEY`, `NOMHORRANT_MODEL`
   di Windows (System Properties → Environment Variables), lalu restart Rhino.

## Konfigurasi API

| Variabel | Isi | Wajib |
|---|---|---|
| `NOMHORRANT_API_BASE` | URL endpoint OpenAI-compatible, mis. `https://<host>/v1` | ya |
| `NOMHORRANT_API_KEY` | Kunci API (rahasia) | ya |
| `NOMHORRANT_MODEL` | Nama model | ya |
| `NOMHORRANT_BRIDGE_PORT` | Port TCP bridge (default `8765`) | tidak |

Alternatif: file `config.local.json` di root instalasi (tidak ikut ter-commit):

```json
{
  "apiBase": "https://<host>/v1",
  "apiKey": "<RAHASIA>",
  "model": "<model>",
  "accessHash": "<sha256:salt>"
}
```

> ⚠️ Jangan pernah menaruh API key di dalam file `.gh`, komponen GH, atau repo publik.

## MCP Bridge (untuk agent AI eksternal)

1. Di Rhino: jalankan `python/rhino_bridge.py` lewat `_RunPythonScript`
   (atau taruh di folder startup script Rhino) — bridge mendengar di `127.0.0.1:8765`.
2. Jalankan server MCP:

   ```bash
   python3 mcp/server.py
   ```

3. Daftarkan di klien MCP Anda (mis. OpenClaw / MiMo Claw), contoh `mcp.json`:

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

4. Sekarang agent bisa memanggil tool: `gh_eval_python`, `gh_set_param`,
   `gh_get_objects`, `viewport_capture`, `gh_open_file`, `gh_list_components`.

## Troubleshooting

| Gejala | Solusi |
|---|---|
| Tab Nomhorrant tidak muncul | Cek folder Libraries, Unblock file, restart Rhino |
| `Unauthorized` dari API | Cek `NOMHORRANT_API_KEY` & `NOMHORRANT_API_BASE`, restart Rhino setelah ganti env |
| Bridge tidak bisa connect | Pastikan `rhino_bridge.py` berjalan di Rhino & port tidak dipakai aplikasi lain |
| Vision mengembalikan gambar kosong | Pastikan viewport aktif; coba zoom extents dulu |
