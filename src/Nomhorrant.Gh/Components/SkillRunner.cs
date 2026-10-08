using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Nomhorrant.Core;

namespace Nomhorrant.Gh.Components
{
    /// <summary>
    /// Skill Runner — jalankan skill/tool terdaftar di ToolRegistry.
    /// Skill baru yang ditambahkan (termasuk oleh MCP/agent) langsung tersedia di sini
    /// dan di Evolve UI, menjadikannya portabel antar proyek GH.
    /// </summary>
    public sealed class SkillRunner : GH_Component
    {
        public SkillRunner()
            : base("Skill Runner", "AISkill",
                "Daftar & jalankan skill AI yang terdaftar di registry.",
                NomhorrantCategories.Category, NomhorrantCategories.SubCategory)
        { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Skill", "S", "Nama skill/tool yang akan dijalankan.", GH_ParamAccess.item);
            p.AddTextParameter("Args", "A", "Argumen JSON untuk skill.", GH_ParamAccess.item, "{}");
            p.AddBooleanParameter("Run", "R", "True untuk menjalankan.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Available", "L", "Daftar skill tersedia.", GH_ParamAccess.list);
            p.AddTextParameter("Result", "O", "Hasil pemanggilan skill (JSON).", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var registry = SkillRegistry.Shared;
            DA.SetDataList(0, registry.Names);

            string skill = "", args = "{}";
            bool run = false;
            DA.GetData(0, ref skill);
            DA.GetData(1, ref args);
            DA.GetData(2, ref run);
            if (!run || string.IsNullOrWhiteSpace(skill)) return;

            try
            {
                var output = registry.Invoke(skill, args);
                DA.SetData(1, output);
            }
            catch (Exception ex)
            {
                DA.SetData(1, $"{{\"error\":\"{ex.Message.Replace("\"", "'")}\"}}");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        protected override System.Drawing.Bitmap? Icon => null;
        public override Guid ComponentGuid => new("b1a27d5c-3e84-4b91-9f26-51c0a4d72004");
    }

    /// <summary>
    /// Registry runtime untuk skill tambahan yang dieksekusi secara nyata
    /// (berbeda dari ToolRegistry yang murni sebagai skema untuk model).
    /// </summary>
    public sealed class SkillRegistry
    {
        public static readonly SkillRegistry Shared = new();

        private readonly Dictionary<string, Func<string, string>> _skills =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Names
        {
            get { lock (_skills) return new List<string>(_skills.Keys); }
        }

        public void Register(string name, Func<string, string> handler)
        {
            lock (_skills) _skills[name] = handler;
        }

        public string Invoke(string name, string argsJson)
        {
            Func<string, string>? handler;
            lock (_skills) _skills.TryGetValue(name, out handler);
            if (handler is null) throw new KeyNotFoundException($"skill tidak terdaftar: {name}");
            return handler(argsJson);
        }
    }
}
