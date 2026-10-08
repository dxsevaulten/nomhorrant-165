using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Nomhorrant.Core
{
    /// <summary>Konfigurasi koneksi API. Kredensial dibaca dari env var / config lokal — tidak pernah dari repo.</summary>
    public sealed class AiSettings
    {
        public string ApiBase { get; set; } = Environment.GetEnvironmentVariable("NOMHORRANT_API_BASE") ?? "";
        public string ApiKey { get; set; } = Environment.GetEnvironmentVariable("NOMHORRANT_API_KEY") ?? "";
        public string Model { get; set; } = Environment.GetEnvironmentVariable("NOMHORRANT_MODEL") ?? "";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiBase) && !string.IsNullOrWhiteSpace(ApiKey);
    }

    public sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "user"; // system|user|assistant|tool
        [JsonPropertyName("content")] public object? Content { get; set; } // string | array (multimodal)
        [JsonPropertyName("tool_calls")] public List<ToolCall>? ToolCalls { get; set; }
        [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    }

    public sealed class ToolCall
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "function";
        [JsonPropertyName("function")] public ToolCallFunction Function { get; set; } = new();
    }

    public sealed class ToolCallFunction
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
    }

    /// <summary>
    /// Klien API OpenAI-compatible dengan dukungan multimodal (vision) & function calling.
    /// Dioptimalkan untuk menghasilkan action packet `nomhorrant.action/v1`, bukan teks panjang.
    /// </summary>
    public sealed class AiClient : IDisposable
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
        private readonly AiSettings _settings;
        private readonly JsonSerializerOptions _json = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public AiClient(AiSettings? settings = null) => _settings = settings ?? new AiSettings();

        public bool IsConfigured => _settings.IsConfigured;

        /// <summary>Chat biasa + tools (function calling).</summary>
        public Task<ChatResponse> ChatAsync(
            IEnumerable<ChatMessage> messages,
            IReadOnlyList<object>? tools = null,
            bool jsonOnly = true,
            CancellationToken ct = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _settings.Model,
                ["messages"] = messages,
            };
            if (tools is { Count: > 0 }) payload["tools"] = tools;
            if (jsonOnly && (tools is null || tools.Count == 0))
                payload["response_format"] = new { type = "json_object" };

            return SendAsync(payload, ct);
        }

        /// <summary>Vision: kirim gambar (PNG base64/data URL) + prompt ke model multimodal.</summary>
        public Task<ChatResponse> ChatVisionAsync(string prompt, string imageBase64OrDataUrl, string mime = "image/png")
        {
            var dataUrl = imageBase64OrDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? imageBase64OrDataUrl
                : $"data:{mime};base64,{imageBase64OrDataUrl}";

            var messages = new List<ChatMessage>
            {
                new()
                {
                    Role = "user",
                    Content = new object[]
                    {
                        new { type = "text", text = prompt },
                        new { type = "image_url", image_url = new { url = dataUrl, detail = "high" } },
                    },
                },
            };
            return ChatAsync(messages);
        }

        private async Task<ChatResponse> SendAsync(Dictionary<string, object?> payload, CancellationToken ct)
        {
            if (!_settings.IsConfigured)
                throw new InvalidOperationException(
                    "API belum dikonfigurasi. Set NOMHORRANT_API_BASE / NOMHORRANT_API_KEY / NOMHORRANT_MODEL.");

            var url = _settings.ApiBase.TrimEnd('/') + "/chat/completions";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _settings.ApiKey);
            req.Content = new StringContent(JsonSerializer.Serialize(payload, _json), Encoding.UTF8, "application/json");

            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"API {(int)resp.StatusCode}: {Truncate(body, 400)}");

            var parsed = JsonSerializer.Deserialize<ChatResponse>(body, _json)
                         ?? throw new InvalidOperationException("Respons API tidak valid.");
            return parsed;
        }

        /// <summary>Ubah tool_calls respons menjadi ActionPacket kanonik.</summary>
        public static ActionPacket ToActionPacket(ChatResponse response, string intentFallback = "")
        {
            var packet = new ActionPacket { Intent = intentFallback };
            foreach (var msg in response.Messages)
            {
                foreach (var call in msg.ToolCalls ?? new List<ToolCall>())
                {
                    var argsJson = ActionProtocol.ExtractJson(call.Function.Arguments) ?? call.Function.Arguments;
                    try
                    {
                        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
                        var item = new ActionItem
                        {
                            Tool = call.Function.Name,
                            Id = string.IsNullOrEmpty(call.Id) ? Guid.NewGuid().ToString("N")[..8] : call.Id,
                        };
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            foreach (var p in doc.RootElement.EnumerateObject())
                                item.Params[p.Name] = p.Value.Clone();
                        packet.Actions.Add(item);
                    }
                    catch (JsonException) { /* argumen rusak: lewati */ }
                }

                // Fallback: model membalas JSON action packet langsung
                if (packet.Actions.Count == 0 && msg.Content is string text)
                {
                    var parsed = ActionProtocol.Parse(text);
                    if (parsed.IsValid) return parsed;
                }
            }
            return packet;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

        public void Dispose() { /* HttpClient statis dipertahankan */ }
    }

    public sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice> Choices { get; set; } = new();

        [JsonIgnore]
        public IEnumerable<ChatMessage> Messages =>
            Choices.Select(c => c.Message).Where(m => m is not null)!;

        [JsonIgnore] public string Text => Choices.Count > 0 ? Choices[0].Message?.TextContent ?? "" : "";

        public sealed class Choice
        {
            [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
            [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
        }
    }

    public static class ChatMessageExtensions
    {
        /// <summary>Konten bisa string atau array multimodal — ambil bentuk teksnya.</summary>
        public static string? TextContent(this ChatMessage m)
        {
            if (m.Content is string s) return s;
            if (m.Content is JsonElement el && el.ValueKind == JsonValueKind.String) return el.GetString();
            return null;
        }
    }
}
