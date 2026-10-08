using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;
using Nomhorrant.Core;

namespace Nomhorrant.Gh.Components
{
    /// <summary>
    /// AI Vision — tangkap viewport Rhino aktif (atau terima gambar referensi),
    /// kirim ke model multimodal, terima analisis + action packet saran revisi.
    /// </summary>
    public sealed class AiVision : GH_Component
    {
        private readonly AiClient _client = new();

        public AiVision()
            : base("AI Vision", "AIVis",
                "Tangkap viewport / analisis foto referensi → saran + action packet.",
                NomhorrantCategories.Category, NomhorrantCategories.SubCategory)
        { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Prompt", "P", "Pertanyaan/analis yang diminta terhadap gambar.", GH_ParamAccess.item);
            p.AddTextParameter("Image", "I", "Path file gambar referensi (kosongkan = tangkap viewport Rhino).", GH_ParamAccess.item);
            p.AddBooleanParameter("Capture", "R", "True untuk menjalankan analisis.", GH_ParamAccess.item, false);
            p.AddIntegerParameter("Width", "W", "Lebar tangkapan viewport (pixel).", GH_ParamAccess.item, 1280);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Analysis", "A", "Hasil analisis AI.");
            p.AddTextParameter("Packet", "JSON", "Action packet saran (bisa kosong).");
            p.AddTextParameter("Png", "PNG", "Base64 PNG hasil tangkapan (untuk inspeksi).");
            p.AddTextParameter("Error", "E", "Pesan error.");
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string prompt = "", imagePath = "";
            bool capture = false;
            int width = 1280;
            DA.GetData(0, ref prompt);
            DA.GetData(1, ref imagePath);
            DA.GetData(2, ref capture);
            DA.GetData(3, ref width);

            if (!capture) return;
            if (!_client.IsConfigured)
            {
                DA.SetData(3, "API belum dikonfigurasi.");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "API belum dikonfigurasi.");
                return;
            }

            try
            {
                string base64;
                if (!string.IsNullOrWhiteSpace(imagePath))
                {
                    if (!File.Exists(imagePath))
                    {
                        DA.SetData(3, $"File tidak ditemukan: {imagePath}");
                        return;
                    }
                    base64 = Convert.ToBase64String(File.ReadAllBytes(imagePath));
                }
                else
                {
                    base64 = Convert.ToBase64String(CaptureActiveViewport(width));
                }

                var effectivePrompt = string.IsNullOrWhiteSpace(prompt)
                    ? "Analisis geometri ini. Berikan penilaian struktur + action packet nomhorrant.action/v1 untuk perbaikan."
                    : prompt + "\n\nBalas dengan analisis singkat, lalu action packet nomhorrant.action/v1 (JSON).";

                var response = _client.ChatVisionAsync(effectivePrompt, base64).GetAwaiter().GetResult();

                DA.SetData(0, response.Text);
                DA.SetData(1, ActionProtocol.ExtractJson(response.Text) ?? "");
                DA.SetData(2, base64);
            }
            catch (Exception ex)
            {
                DA.SetData(3, ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        /// <summary>Tangkap viewport Rhino aktif ke PNG bytes.</summary>
        public static byte[] CaptureActiveViewport(int width)
        {
            var view = RhinoDoc.ActiveDoc?.Views?.ActiveView
                       ?? throw new InvalidOperationException("Tidak ada viewport aktif.");
            int w = Math.Max(64, width);
            int h = w * 9 / 16;
            Bitmap bmp = view.CaptureToBitmap(new Size(w, h), false);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        protected override Bitmap? Icon => null;
        public override Guid ComponentGuid => new("b1a27d5c-3e84-4b91-9f26-51c0a4d72002");
    }
}
