using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nomhorrant.Core
{
    /// <summary>Satu aksi yang akan dijalankan di Grasshopper.</summary>
    public sealed class ActionItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        [JsonPropertyName("tool")] public string Tool { get; set; } = "";
        [JsonPropertyName("params")] public Dictionary<string, JsonElement> Params { get; set; } = new();
        [JsonPropertyName("target")] public Dictionary<string, string>? Target { get; set; }
        [JsonPropertyName("requiresApproval")] public bool RequiresApproval { get; set; } = true;

        public double GetNumber(string key, double fallback = 0)
            => Params.TryGetValue(key, out var v) && v.TryGetDouble(out var d) ? d : fallback;

        public string GetString(string key, string fallback = "")
            => Params.TryGetValue(key, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : v.ToString()) : fallback;
    }

    /// <summary>Paket aksi hasil AI (nomhorrant.action/v1).</summary>
    public sealed class ActionPacket
    {
        [JsonPropertyName("protocol")] public string Protocol { get; set; } = "nomhorrant.action/v1";
        [JsonPropertyName("intent")] public string Intent { get; set; } = "";
        [JsonPropertyName("actions")] public List<ActionItem> Actions { get; set; } = new();
        [JsonPropertyName("notes")] public string Notes { get; set; } = "";

        [JsonIgnore] public bool IsValid => Actions.Count > 0 && !string.IsNullOrWhiteSpace(Actions[0].Tool);
    }

    /// <summary>Hasil eksekusi untuk dikirim balik ke model (nomhorrant.result/v1).</summary>
    public sealed class ActionResult
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "ok"; // ok | rejected | error
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("outputs")] public Dictionary<string, object?> Outputs { get; set; } = new();
    }

    /// <summary>
    /// Parser & normalizer action packet.
    /// Menerima bentuk penuh (protocol/actions) maupun bentuk datar {"action":"...", ...}.
    /// </summary>
    public static class ActionProtocol
    {
        public const string Version = "nomhorrant.action/v1";

        /// <summary>Alias bentuk datar: nama "action" → nama tool registry.</summary>
        public static readonly IReadOnlyDictionary<string, string> FlatAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["create_spiral"] = "geometry.spiral",
                ["spiral"]        = "geometry.spiral",
                ["create_grid"]   = "geometry.grid",
                ["grid"]          = "geometry.grid",
                ["surface"]       = "geometry.surface_from_points",
                ["set_slider"]    = "param.set",
                ["get_slider"]    = "param.get",
                ["eval_python"]   = "gh.eval_python",
                ["create_component"] = "gh.create_component",
                ["capture_viewport"] = "vision.capture_viewport",
                ["analyze_reference"] = "vision.analyze_reference",
            };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Parse teks balasan AI menjadi ActionPacket.
        /// Toleran terhadap code fence, teks pengantar, dan bentuk datar.
        /// </summary>
        public static ActionPacket Parse(string raw)
        {
            var json = ExtractJson(raw);
            if (json is null) return new ActionPacket();

            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                return Normalize(doc.RootElement);
            }
            catch (JsonException)
            {
                return new ActionPacket();
            }
        }

        /// <summary>Normalisasi sebuah elemen JSON menjadi ActionPacket kanonik.</summary>
        public static ActionPacket Normalize(JsonElement root)
        {
            var packet = new ActionPacket();

            if (root.ValueKind != JsonValueKind.Object) return packet;

            // Bentuk penuh: {"protocol":..., "intent":..., "actions":[...]}
            if (root.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array)
            {
                if (root.TryGetProperty("intent", out var intent)) packet.Intent = intent.ToString();
                if (root.TryGetProperty("notes", out var notes)) packet.Notes = notes.ToString();
                foreach (var a in acts.EnumerateArray())
                {
                    var item = DeserializeItem(a);
                    if (item is not null) packet.Actions.Add(item);
                }
                return packet;
            }

            // Bentuk datar: {"action":"create_spiral","turns":5,...}
            if (root.TryGetProperty("action", out var act))
            {
                var item = DeserializeItem(root);
                if (item is not null)
                {
                    item.Tool = ResolveToolName(item.Tool);
                    packet.Actions.Add(item);
                    packet.Intent = item.Tool;
                }
            }
            return packet;
        }

        /// <summary>Bungkus hasil eksekusi sebagai JSON siap kirim ke model.</summary>
        public static string SerializeResults(IEnumerable<ActionResult> results, string? canvasStateJson = null)
        {
            var payload = new Dictionary<string, object?>
            {
                ["protocol"] = "nomhorrant.result/v1",
                ["results"] = results.ToList(),
            };
            if (canvasStateJson is not null) payload["canvasState"] = JsonSerializer.Deserialize<JsonElement>(canvasStateJson);
            return JsonSerializer.Serialize(payload, JsonOpts);
        }

        private static ActionItem? DeserializeItem(JsonElement a)
        {
            if (a.ValueKind != JsonValueKind.Object) return null;
            try
            {
                var item = a.Deserialize<ActionItem>(JsonOpts);
                if (item is null) return null;
                item.Tool = ResolveToolName(item.Tool);
                return string.IsNullOrWhiteSpace(item.Tool) ? null : item;
            }
            catch (JsonException) { return null; }
        }

        private static string ResolveToolName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            return FlatAliases.TryGetValue(name.Trim(), out var mapped) ? mapped : name.Trim();
        }

        /// <summary>Cari JSON object/array pertama dalam teks bebas (mis. dibungkus code fence).</summary>
        public static string? ExtractJson(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var text = raw.Trim();

            // Buang code fence ```json ... ```
            if (text.StartsWith("```"))
            {
                var end = text.LastIndexOf("```", StringComparison.Ordinal);
                text = text[3..end].Trim();
                if (text.StartsWith("json", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
            }

            int start = text.IndexOfAny(new[] { '{', '[' });
            if (start < 0) return null;

            int depth = 0;
            bool inString = false, escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c is '{' or '[') depth++;
                else if (c is '}' or ']')
                {
                    depth--;
                    if (depth == 0) return text[start..(i + 1)];
                }
            }
            return null;
        }
    }
}
