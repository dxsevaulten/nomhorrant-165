using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nomhorrant.Core
{
    /// <summary>Deskripsi satu tool/skill yang didaftarkan ke model (function calling).</summary>
    public sealed class ToolSpec
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("parameters")] public JsonElement Parameters { get; set; }

        /// <summary>Ubah ke format `tools` OpenAI-compatible.</summary>
        public object ToOpenAiTool() => new
        {
            type = "function",
            function = new { name = Name, description = Description, parameters = Parameters },
        };
    }

    /// <summary>
    /// Registry pusat: daftar skill/tool yang bisa dipanggil AI.
    /// Modul lain (komponen GH, Evolve UI, MCP) mendaftarkan tool-nya di sini,
    /// sehingga skill baru langsung tersedia di semua frontend.
    /// </summary>
    public sealed class ToolRegistry
    {
        private readonly Dictionary<string, ToolSpec> _tools = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        public void Register(ToolSpec spec)
        {
            if (string.IsNullOrWhiteSpace(spec.Name)) throw new ArgumentException("tool name wajib");
            lock (_gate) _tools[spec.Name] = spec;
        }

        public bool Unregister(string name)
        {
            lock (_gate) return _tools.Remove(name);
        }

        public bool TryGet(string name, out ToolSpec spec)
        {
            lock (_gate) return _tools.TryGetValue(name, out spec!);
        }

        public IReadOnlyList<ToolSpec> All
        {
            get { lock (_gate) return _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList(); }
        }

        /// <summary>Format payload `tools` untuk request chat completion.</summary>
        public List<object> ToOpenAiTools() => All.Select(t => t.ToOpenAiTool()).ToList<object>();

        private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

        /// <summary>Tool bawaan Nomhorrant (lihat docs/JSON_PROTOCOL.md).</summary>
        public static ToolRegistry CreateDefault()
        {
            var r = new ToolRegistry();

            r.Register(new ToolSpec
            {
                Name = "geometry.spiral",
                Description = "Buat kurva spiral parametrik di Grasshopper.",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{
                    'turns':{'type':'number','description':'jumlah putaran'},
                    'radius':{'type':'number','description':'radius dasar'},
                    'height':{'type':'number','description':'kenaikan tinggi total'}},
                  'required':['turns','radius']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "geometry.grid",
                Description = "Buat grid titik/panel untuk pola lanskap.",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{
                    'xCount':{'type':'integer'},'yCount':{'type':'integer'},
                    'size':{'type':'number'}},
                  'required':['xCount','yCount','size']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "geometry.surface_from_points",
                Description = "Bangun surface dari control net titik.",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{'points':{'type':'array','items':{'type':'array','items':{'type':'number'}}}},
                  'required':['points']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "param.set",
                Description = "Set nilai slider pada kanvas Grasshopper.",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{
                    'slider':{'type':'string','description':'nama/label slider'},
                    'value':{'type':'number'}},
                  'required':['slider','value']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "param.get",
                Description = "Baca nilai slider saat ini.",
                Parameters = Schema(@"{
                  'type':'object','properties':{'slider':{'type':'string'}},'required':['slider']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "gh.eval_python",
                Description = "Eksekusi skrip Python Rhino di kanvas (butuh persetujuan pengguna).",
                Parameters = Schema(@"{
                  'type':'object','properties':{'code':{'type':'string'}},'required':['code']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "gh.create_component",
                Description = "Cetak komponen Grasshopper mandiri baru (user object).",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{
                    'name':{'type':'string'},
                    'inputs':{'type':'array','items':{'type':'string'}},
                    'outputs':{'type':'array','items':{'type':'string'}},
                    'code':{'type':'string','description':'isi skrip komponen'}},
                  'required':['name','inputs','outputs','code']}"),
            });

            r.Register(new ToolSpec
            {
                Name = "vision.capture_viewport",
                Description = "Tangkap viewport Rhino aktif menjadi PNG.",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{'width':{'type':'integer','description':'lebar pixel hasil tangkapan'}},
                  'required':[]}"),
            });

            r.Register(new ToolSpec
            {
                Name = "vision.analyze_reference",
                Description = "Analisis foto referensi untuk menyusun konsep komponen/cluster GH.",
                Parameters = Schema(@"{
                  'type':'object',
                  'properties':{
                    'image':{'type':'string','description':'base64 PNG/JPG atau data URL'},
                    'prompt':{'type':'string'}},
                  'required':['image','prompt']}"),
            });

            return r;
        }
    }
}
