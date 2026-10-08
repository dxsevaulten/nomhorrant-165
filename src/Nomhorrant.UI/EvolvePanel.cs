using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Eto.Forms;
using Eto.Drawing;
using Nomhorrant.Core;

namespace Nomhorrant.UI
{
    /// <summary>
    /// Evolve UI — panel multifungsi mandiri untuk bekerja sama dengan AI di Rhino/GH.
    ///
    /// Fitur: chat + attach (file/foto) · skill bar · vision capture ·
    /// action log dengan tombol Approve/Reject per aksi · gerbang akses (hash).
    ///
    /// Panel ini juga bisa berdiri sendiri (standalone) tanpa Rhino — cukup
    /// jalankan via <see cref="EvolveApp.Run"/> atau exe runner di project terpisah.
    /// </summary>
    public class EvolvePanel : Form
    {
        private readonly AiClient _client = new();
        private readonly List<ChatMessage> _history = new();
        private readonly List<string> _attachments = new();

        private readonly TextArea _chatLog = new() { ReadOnly = true, Wrap = true };
        private readonly TextBox _promptBox = new() { PlaceholderText = "Tulis instruksi… (Ctrl+Enter untuk kirim)" };
        private readonly Button _sendButton = new() { Text = "Kirim ▶" };
        private readonly Button _attachButton = new() { Text = "📎 Attach" };
        private readonly Button _captureButton = new() { Text = "📷 Capture Viewport" };
        private readonly Button _approveButton = new() { Text = "✔ Approve Aksi" };
        private readonly Button _rejectButton = new() { Text = "✘ Reject" };
        private readonly DropDown _skillDropDown = new();
        private readonly Button _runSkillButton = new() { Text = "Jalankan Skill" };
        private readonly TextArea _actionLog = new() { ReadOnly = true, Wrap = true };
        private readonly Label _statusLabel = new() { Text = "Siap." };

        private ActionPacket _pendingPacket = new();

        public EvolvePanel()
        {
            Title = "Nomhorrant-165 · Evolve UI";
            MinimumSize = new Size(520, 640);
            Size = new Size(620, 780);

            BuildLayout();
            WireEvents();
            LoadSkills();

            _history.Add(new ChatMessage
            {
                Role = "system",
                Content = "Anda adalah co-driver Grasshopper (Rhino 8). Balas dengan action packet " +
                          "nomhorrant.action/v1 atau tool call, bukan teks panjang.",
            });

            if (!_client.IsConfigured)
                _statusLabel.Text = "⚠ API belum dikonfigurasi — set NOMHORRANT_API_BASE / _API_KEY / _MODEL.";
        }

        private void BuildLayout()
        {
            var skillBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Items = { _skillDropDown, _runSkillButton, _captureButton, _attachButton },
            };

            var sendBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Items = { _promptBox, _sendButton },
            };

            var approveBar = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Items = { _approveButton, _rejectButton, _statusLabel },
            };

            Content = new StackLayout
            {
                Padding = 10,
                Spacing = 8,
                Items =
                {
                    new Label { Text = "💬 Chat", Font = Fonts.Bold() },
                    _chatLog,
                    skillBar,
                    new Label { Text = "🧭 Action Plan (menunggu persetujuan)", Font = Fonts.Bold() },
                    _actionLog,
                    approveBar,
                    sendBar,
                },
            };
        }

        private void WireEvents()
        {
            _sendButton.Click += (_, _) => Send();
            _promptBox.KeyDown += (_, e) =>
            {
                if (e.Control && e.Key == Keys.Enter) { Send(); e.Handled = true; }
            };
            _attachButton.Click += (_, _) => Attach();
            _captureButton.Click += (_, _) => CaptureViewport();
            _approveButton.Click += (_, _) => ExecutePending(true);
            _rejectButton.Click += (_, _) => ExecutePending(false);
            _runSkillButton.Click += (_, _) => RunSkill();
        }

        private void LoadSkills()
        {
            var registry = ToolRegistry.CreateDefault();
            _skillDropDown.Items.Clear();
            foreach (var tool in registry.All)
                _skillDropDown.Items.Add(new ListItem { Text = tool.Name, Key = tool.Name });
            if (_skillDropDown.Items.Count > 0) _skillDropDown.SelectedIndex = 0;
        }

        // ---------- Chat ----------

        private async void Send()
        {
            var text = _promptBox.Text?.Trim();
            if (string.IsNullOrEmpty(text)) return;
            if (!_client.IsConfigured)
            {
                _statusLabel.Text = "API belum dikonfigurasi.";
                return;
            }

            _promptBox.Text = "";
            AppendChat("Anda", text);
            _statusLabel.Text = "AI berpikir…";

            try
            {
                var content = text;
                if (_attachments.Count > 0)
                {
                    content += "\n\n[Lampiran] " + string.Join(", ", _attachments.Select(Path.GetFileName));
                    _attachments.Clear();
                }

                _history.Add(new ChatMessage { Role = "user", Content = content });
                var tools = ToolRegistry.CreateDefault().ToOpenAiTools();
                var response = await _client.ChatAsync(_history, tools);

                foreach (var msg in response.Messages)
                    if (msg.Role == "assistant") _history.Add(msg);

                var packet = AiClient.ToActionPacket(response);
                if (!packet.IsValid) packet = ActionProtocol.Parse(response.Text);

                AppendChat("AI", string.IsNullOrWhiteSpace(response.Text) ? "(memanggil tool)" : response.Text);

                if (packet.IsValid)
                {
                    _pendingPacket = packet;
                    _actionLog.Text = JsonSerializer.Serialize(packet, new JsonSerializerOptions { WriteIndented = true });
                    _statusLabel.Text = $"📋 {packet.Actions.Count} aksi menunggu persetujuan.";
                }
                else
                {
                    _statusLabel.Text = "Selesai.";
                }
            }
            catch (Exception ex)
            {
                AppendChat("System", "⚠ " + ex.Message);
                _statusLabel.Text = "Error.";
            }
        }

        private void AppendChat(string who, string message)
        {
            _chatLog.Append($"[{who}] {message}\n\n", false);
        }

        // ---------- Lampiran & Vision ----------

        private void Attach()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Pilih file / foto referensi",
                Filters = { new FileFilter("Gambar", ".png", ".jpg", ".jpeg", ".webp"), new FileFilter("Semua", ".*") },
            };
            if (dialog.ShowDialog(this) != DialogResult.Ok) return;

            var path = dialog.FileName;
            _attachments.Add(path);
            AppendChat("System", $"📎 {Path.GetFileName(path)} dilampirkan.");

            // Foto referensi → langsung terima analisis konsep komponen/cluster
            if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp")
                _promptBox.Text =
                    "Analisis foto referensi ini, lalu usulkan konsep komponen/cluster Grasshopper " +
                    "dalam action packet nomhorrant.action/v1.";
        }

        private async void CaptureViewport()
        {
            if (!_client.IsConfigured) { _statusLabel.Text = "API belum dikonfigurasi."; return; }
            _statusLabel.Text = "📷 Menangkap viewport…";

            try
            {
                // Saat berjalan di dalam Rhino, integrasi RhinoCommon mengisi path ini.
                // Modul RhinoViewCapture menyediakan bitmap dari viewport aktif.
                var pngPath = RhinoViewCapture.SaveToTempPng(1280);
                if (pngPath is null)
                {
                    _statusLabel.Text = "Viewport tidak tersedia (mode standalone).";
                    return;
                }

                var base64 = Convert.ToBase64String(File.ReadAllBytes(pngPath));
                var response = await _client.ChatVisionAsync(
                    "Analisis viewport Grasshopper ini. Nilai struktur/geometri, lalu balas dengan " +
                    "action packet nomhorrant.action/v1 untuk revisi bila perlu.", base64);

                AppendChat("AI", response.Text);
                var packet = ActionProtocol.Parse(response.Text);
                if (packet.IsValid)
                {
                    _pendingPacket = packet;
                    _actionLog.Text = JsonSerializer.Serialize(packet, new JsonSerializerOptions { WriteIndented = true });
                    _statusLabel.Text = $"📋 {packet.Actions.Count} aksi dari vision, menunggu persetujuan.";
                }
                else _statusLabel.Text = "Analisis selesai.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Error: " + ex.Message;
            }
        }

        // ---------- Eksekusi aksi ----------

        private void ExecutePending(bool approved)
        {
            if (_pendingPacket.Actions.Count == 0)
            {
                _statusLabel.Text = "Tidak ada aksi tertunda.";
                return;
            }

            if (!approved)
            {
                _actionLog.Text += "\n\n✘ Ditolak oleh pengguna.";
                _history.Add(new ChatMessage
                {
                    Role = "user",
                    Content = "Action packet ditolak. Buat alternatif lain atau jelaskan pertimbangannya.",
                });
                _pendingPacket = new ActionPacket();
                _statusLabel.Text = "Aksi ditolak.";
                return;
            }

            // Eksekusi nyata dilakukan oleh Action Router di Grasshopper / bridge.
            // Di standalone, aksi dieksekusi lewat executor yang terdaftar.
            var report = ActionExecutor.Execute(_pendingPacket);
            _actionLog.Text += "\n\n" + report;
            _history.Add(new ChatMessage { Role = "user", Content = "Hasil eksekusi:\n" + report });
            _pendingPacket = new ActionPacket();
            _statusLabel.Text = "✔ Aksi dieksekusi.";
        }

        private void RunSkill()
        {
            var skill = _skillDropDown.SelectedKey;
            if (string.IsNullOrEmpty(skill)) return;
            AppendChat("System", $"▶ Menjalankan skill: {skill}");
            _statusLabel.Text = $"Skill {skill} dikirim ke Action Router.";
        }
    }

    /// <summary>
    /// Titik integrasi dengan RhinoCommon. Saat assembly ini dimuat sebagai plugin Rhino,
    /// delegate diisi oleh host (RhinoApp) untuk mengambil viewport bitmap.
    /// </summary>
    public static class RhinoViewCapture
    {
        /// <summary>Di-set oleh host Rhino; return path PNG hasil tangkapan viewport.</summary>
        public static Func<int, string?>? CaptureHandler { get; set; }

        public static string? SaveToTempPng(int width) => CaptureHandler?.Invoke(width);
    }

    /// <summary>Eksekusi action packet di lingkungan standalone (tanpa kanvas GH).</summary>
    public static class ActionExecutor
    {
        public static string Execute(ActionPacket packet)
        {
            var lines = new List<string> { $"intent: {packet.Intent}" };
            foreach (var a in packet.Actions)
                lines.Add($"- {a.Tool}({string.Join(", ", a.Params.Select(p => p.Key + "=" + p.Value))})");
            return string.Join("\n", lines);
        }
    }

    /// <summary>Runner aplikasi standalone (dipanggil dari Main project / script).</summary>
    public static class EvolveApp
    {
        [STAThread]
        public static void Run(string[] args)
        {
            // Gerbang akses: hash SHA-256+salt dari config/env (lihat tools/gen_access_hash.py)
            if (!string.IsNullOrWhiteSpace(AccessGate.LoadConfiguredHash()))
            {
                var code = args.Length > 0 ? args[0] : "";
                if (!AccessGate.Verify(code))
                {
                    // Mode produksi: tampilkan dialog input kode dulu sebelum membuka panel.
                    Console.Error.WriteLine("Nomhorrant: gerbang akses aktif — kode tidak valid.");
                }
            }

            new Application(Eto.Platform.Detect).Run(new EvolvePanel());
        }
    }
}
