# Nomhorrant Action Protocol (`nomhorrant.action/v1`)

Inti Nomhorrant-165: AI **tidak membalas teks panjang**, melainkan paket aksi JSON
yang bisa dieksekusi di Grasshopper. Berikut kontraknya.

## 1. Action Packet

```json
{
  "protocol": "nomhorrant.action/v1",
  "intent": "buat spiral lanskap",
  "actions": [
    {
      "id": "a1",
      "tool": "geometry.spiral",
      "params": { "turns": 5, "radius": 12.5, "height": 3.0 },
      "target": { "component": "Spiral_01", "input": "T" },
      "requiresApproval": true
    },
    {
      "id": "a2",
      "tool": "param.set",
      "params": { "slider": "Radius_Slider", "value": 12.5 }
    }
  ],
  "notes": "Spiral dasar untuk massa lanskap; langkah berikutnya: offset surface."
}
```

### Aturan

- `protocol` — wajib, nilai tetap `nomhorrant.action/v1`.
- `intent` — ringkasan manusiawi dari rencana (tampil di Evolve UI).
- `actions[]` — urutan eksekusi; router menjalankan berurutan sesuai `id`.
- `tool` — nama tool dari registry (lihat §3). Tool tak dikenal → aksi dilewati + warning.
- `params` — objek bebas, divalidasi per tool.
- `target` — opsional, penunjuk komponen/input tujuan di kanvas.
- `requiresApproval` — default `true`; UI menampilkan tombol Approve/Reject per aksi.

## 2. Bentuk yang juga diterima (kompatibel)

Agar cocok dengan output model yang lebih sederhana, parser menerima bentuk datar:

```json
{ "action": "create_spiral", "turns": 5, "radius": 12.5 }
```

Parser akan menormalkannya menjadi satu `actions[]` dengan
`tool = "geometry.spiral"` via tabel alias `action → tool`.

## 3. Tool Registry bawaan

| Tool | Parameter | Fungsi |
|---|---|---|
| `geometry.spiral` | `turns, radius, height` | Buat kurva spiral |
| `geometry.grid` | `xCount, yCount, size` | Grid titik/panel |
| `geometry.surface_from_points` | `points[][]` | Surface dari control net |
| `param.set` | `slider, value` | Set nilai slider di kanvas |
| `param.get` | `slider` | Baca nilai slider |
| `gh.eval_python` | `code` | Eksekusi skrip Python di Rhino (sandboxed, approval wajib) |
| `gh.create_component` | `name, inputs, outputs, code` | Cetak komponen GH mandiri baru |
| `vision.capture_viewport` | `view, width` | Tangkap viewport → PNG base64 |
| `vision.analyze_reference` | `image, prompt` | Analisis foto referensi |

## 4. Function calling (schema untuk API AI)

Saat memanggil API, Nomhorrant mendaftarkan tool sebagai JSON Schema, contoh:

```json
{
  "type": "function",
  "function": {
    "name": "geometry.spiral",
    "description": "Buat kurva spiral parametrik di Grasshopper",
    "parameters": {
      "type": "object",
      "properties": {
        "turns":  { "type": "number", "description": "Jumlah putaran" },
        "radius": { "type": "number", "description": "Radius dasar" },
        "height": { "type": "number", "description": "Kenaikan tinggi total" }
      },
      "required": ["turns", "radius"]
    }
  }
}
```

Model membalas `tool_calls` → klien mengubahnya menjadi `action packet` →
UI meminta persetujuan → router mengeksekusi.

## 5. Balasan eksekusi

Router mengirim balasan kembali ke percakapan (dan ke model sebagai `tool` result):

```json
{
  "protocol": "nomhorrant.result/v1",
  "results": [
    { "id": "a1", "status": "ok", "outputs": { "curveId": "GUID-..." } },
    { "id": "a2", "status": "rejected", "reason": "user rejected" }
  ],
  "canvasState": { "sliders": { "Radius_Slider": 12.5 } }
}
```

Dengan loop ini AI selalu tahu keadaan kanvas terkini → presisi parametrik meningkat
dan revisi bisa berulang (*iterative co-driving*).
