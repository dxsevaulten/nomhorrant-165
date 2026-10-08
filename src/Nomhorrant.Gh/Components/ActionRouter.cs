using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Nomhorrant.Core;

namespace Nomhorrant.Gh.Components
{
    /// <summary>
    /// Action Router — eksekusi action packet di kanvas Grasshopper.
    /// Setiap aksi butuh persetujuan eksplisit (mode co-driver): input Approve = true.
    /// </summary>
    public sealed class ActionRouter : GH_Component
    {
        public ActionRouter()
            : base("Action Router", "AIRoute",
                "Jalankan action packet JSON ke komponen/slider/objek Grasshopper.",
                NomhorrantCategories.Category, NomhorrantCategories.SubCategory)
        { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Packet", "JSON", "Action packet dari AI Connector / AI Vision.", GH_ParamAccess.item);
            p.AddBooleanParameter("Approve", "A", "True = izinkan eksekusi semua aksi.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Numbers", "N", "Angka hasil (param slider, dimensi, dst).", GH_ParamAccess.tree);
            p.AddTextParameter("Geometry", "G", "Ringkasan geometri yang dibuat.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Laporan eksekusi per aksi (nomhorrant.result/v1).", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string json = "";
            bool approve = false;
            DA.GetData(0, ref json);
            DA.GetData(1, ref approve);

            if (string.IsNullOrWhiteSpace(json)) return;

            var packet = ActionProtocol.Parse(json);
            if (!packet.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Packet tidak valid / tidak ada aksi.");
                DA.SetData(2, "{\"protocol\":\"nomhorrant.result/v1\",\"results\":[]}");
                return;
            }

            var results = new List<ActionResult>();
            var numbers = new DataTree<GH_Number>();
            var geometrySummaries = new List<string>();

            for (int i = 0; i < packet.Actions.Count; i++)
            {
                var action = packet.Actions[i];
                if (action.RequiresApproval && !approve)
                {
                    results.Add(new ActionResult { Id = action.Id, Status = "rejected", Reason = "menunggu persetujuan (Approve=false)" });
                    continue;
                }

                try
                {
                    var result = Execute(action, numbers, geometrySummaries, i);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new ActionResult { Id = action.Id, Status = "error", Reason = ex.Message });
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{action.Tool}: {ex.Message}");
                }
            }

            DA.SetDataTree(0, numbers);
            DA.SetDataList(1, geometrySummaries);
            DA.SetData(2, ActionProtocol.SerializeResults(results));
        }

        private ActionResult Execute(ActionItem action, DataTree<GH_Number> numbers, List<string> summaries, int branch)
        {
            var path = new GH_Path(branch);
            var result = new ActionResult { Id = action.Id, Status = "ok" };

            switch (action.Tool)
            {
                case "geometry.spiral":
                {
                    double turns = action.GetNumber("turns", 5);
                    double radius = action.GetNumber("radius", 10);
                    double height = action.GetNumber("height", 3);
                    var pts = new List<Point3d>();
                    int steps = Math.Max(16, (int)(turns * 36));
                    for (int i = 0; i <= steps; i++)
                    {
                        double t = (double)i / steps;
                        double angle = 2 * Math.PI * turns * t;
                        pts.Add(new Point3d(radius * Math.Cos(angle), radius * Math.Sin(angle), height * t));
                    }
                    var curve = new PolylineCurve(pts);
                    summaries.Add($"spiral turns={turns} r={radius} h={height} pts={pts.Count}");
                    foreach (var v in new[] { turns, radius, height }) numbers.Add(new GH_Number(v), path);
                    result.Outputs["points"] = pts.Count;
                    break;
                }
                case "geometry.grid":
                {
                    int xc = (int)action.GetNumber("xCount", 10);
                    int yc = (int)action.GetNumber("yCount", 10);
                    double size = action.GetNumber("size", 1);
                    summaries.Add($"grid {xc}x{yc} size={size}");
                    foreach (var v in new double[] { xc, yc, size }) numbers.Add(new GH_Number(v), path);
                    result.Outputs["cells"] = xc * yc;
                    break;
                }
                case "param.set":
                {
                    string slider = action.GetString("slider");
                    double value = action.GetNumber("value");
                    // Nilai dikirim sebagai output Numbers; pemasangan ke slider asli
                    // dilakukan lewat GhSliderSetter (python/rhino_bridge.py) bila bridge aktif.
                    numbers.Add(new GH_Number(value), path);
                    summaries.Add($"param.set {slider}={value}");
                    result.Outputs["slider"] = slider;
                    result.Outputs["value"] = value;
                    break;
                }
                case "param.get":
                    summaries.Add($"param.get {action.GetString("slider")}");
                    break;

                case "geometry.surface_from_points":
                    summaries.Add("surface_from_points (membutuhkan control net; lihat laporan tool)");
                    result.Outputs["note"] = "gunakan GhPython fallback untuk control net kompleks";
                    break;

                default:
                    result.Status = "error";
                    result.Reason = $"tool tidak dikenal: {action.Tool}";
                    break;
            }

            return result;
        }

        protected override System.Drawing.Bitmap? Icon => null;
        public override Guid ComponentGuid => new("b1a27d5c-3e84-4b91-9f26-51c0a4d72003");
    }
}
