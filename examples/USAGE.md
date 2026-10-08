# Contoh Pemakaian Nomhorrant-165

## Skenario 1: Massa lanskap organik dari prompt

1. Tarik komponen **AI Connector**.
2. Isi `Prompt`:
   > buat spiral 5 putaran, radius 12.5, tinggi 3 untuk massa lanskap
3. Set `Run` = true → output `Packet` berisi action packet JSON.
4. Sambungkan `Packet` ke **Action Router**.
5. Set `Approve` = true → output `Numbers` berisi `[turns, radius, height]`
   yang bisa dipasang ke slider & komponen spiral.

## Skenario 2: Vision loop — revisi dari viewport

1. Posisikan viewport 3D pada bentuk yang sedang dikerjakan.
2. Tarik **AI Vision**, isi `Prompt`:
   > apakah struktur ini stabil? sarankan perbaikan parametrik
3. Set `Capture` = true → output `Analysis` + `Packet` saran revisi.
4. Jalankan packet lewat **Action Router** → lihat perubahan angka.

## Skenario 3: Foto referensi → konsep cluster

1. Di **Evolve UI** (panel Nomhorrant), klik **📎 Attach** → pilih foto referensi lanskap.
2. Prompt otomatis terisi: analisis foto → usulan komponen/cluster GH.
3. Review action plan di panel → **✔ Approve Aksi**.

## Skenario 4: Agent eksternal lewat MCP

1. Jalankan bridge: `_RunPythonScript` → `python/rhino_bridge.py`.
2. Daftarkan `mcp/server.py` di klien MCP Anda.
3. Perintahkan agent:
   > tangkap viewport saya, analisis, lalu ubah slider Radius jadi 14
4. Agent memanggil `viewport_capture` → analisis → `gh_set_param`.

## Skenario 5: Komponen mandiri baru dari AI

1. Minta lewat prompt:
   > buat komponen cluster "ContourDeck" dengan input surface, count, dan offset
2. AI memanggil tool `gh.create_component` → komponen baru terdaftar sebagai
   user object → bisa dipakai ulang di proyek lain (portabilitas progres param).
