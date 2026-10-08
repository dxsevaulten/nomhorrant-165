using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Nomhorrant.Core;

namespace Nomhorrant.Gh.Components
{
    /// <summary>
    /// AI Connector — jembatan utama: prompt + konteks kanvas → AI → Action Packet JSON.
    /// Output bisa langsung disambung ke Action Router atau dilihat di panel.
    /// </summary>
    public sealed class AiConnector : GH_Component
    {
        private readonly AiClient _client = new();

        public AiConnector()
            : base("AI Connector", "AIConn",
                "Kirim prompt + konteks ke API AI, terima action packet nomhorrant.action/v1.",
                NomhorrantCategories.Category, NomhorrantCategories.SubCategory)
        { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Prompt", "P", "Instruksi untuk AI (bahasa natural).", GH_ParamAccess.item);
            p.AddTextParameter("Context", "C", "Konteks tambahan: nama komponen, nilai slider, riwayat. (opsional)", GH_ParamAccess.item);
            p.AddBooleanParameter("Run", "R", "True untuk memanggil API.", GH_ParamAccess.item, false);
            p.AddTextParameter("System", "S", "System prompt tambahan (opsional).", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Packet", "JSON", "Action packet (nomhorrant.action/v1).");
            p.AddTextParameter("Intent", "I", "Ringkasan rencana kerja.");
            p.AddTextParameter("Tools", "T", "Daftar tool terpakai, satu per baris.");
            p.AddTextParameter("Error", "E", "Pesan error bila pemanggilan gagal.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string prompt = "", context = "", system = "";
            bool run = false;
            DA.GetData(0, ref prompt);
            DA.GetData(1, ref context);
            DA.GetData(2, ref run);
            DA.GetData(3, ref system);

            if (!run) { return; }
            if (string.IsNullOrWhiteSpace(prompt))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Prompt kosong.");
                return;
            }
            if (!_client.IsConfigured)
            {
                DA.SetData(3, "API belum dikonfigurasi (NOMHORRANT_API_BASE / NOMHORRANT_API_KEY / NOMHORRANT_MODEL).");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "API belum dikonfigurasi.");
                return;
            }

            try
            {
                var registry = ToolRegistry.CreateDefault();

                var systemText =
                    "Anda adalah co-driver Grasshopper (Rhino 8). Balas HANYA dengan action packet JSON " +
                    "berformat nomhorrant.action/v1: {\"protocol\":\"nomhorrant.action/v1\",\"intent\":\"...\",\"actions\":[...]} " +
                    "atau panggil tool yang tersedia. Jangan membalas teks panjang. " +
                    system;

                var messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = systemText },
                    new() { Role = "user", Content = string.IsNullOrWhiteSpace(context) ? prompt : prompt + "\n\n[Konteks kanvas]\n" + context },
                };

                var response = _client.ChatAsync(messages, registry.ToOpenAiTools())
                                        .GetAwaiter().GetResult();

                var packet = AiClient.ToActionPacket(response, intentFallback: prompt);
                if (!packet.IsValid)
                {
                    // coba parse dari teks biasa
                    packet = ActionProtocol.Parse(response.Text);
                }

                DA.SetData(0, JsonSerializer.Serialize(packet, new JsonSerializerOptions { WriteIndented = true }));
                DA.SetData(1, packet.Intent);
                DA.SetData(2, string.Join("\n", packet.Actions.Select(a => a.Tool)));

                if (!packet.IsValid)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "AI tidak menghasilkan action packet yang valid.");
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"{packet.Actions.Count} aksi siap → sambungkan ke Action Router.");
            }
            catch (Exception ex)
            {
                DA.SetData(3, ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => null;
        public override Guid ComponentGuid => new("b1a27d5c-3e84-4b91-9f26-51c0a4d72001");
    }
}
