using Grasshopper;
using Grasshopper.Kernel;

namespace Nomhorrant.Gh
{
    /// <summary>Metadata assembly Grasshopper untuk Nomhorrant-165.</summary>
    public class NomhorrantGhInfo : GH_AssemblyInfo
    {
        public override string Name => "Nomhorrant-165";

        public override string Description =>
            "Asisten AI terintegrasi untuk Grasshopper: action protocol, vision, skill runner (Evolve UI).";

        public override System.Guid Id => new System.Guid("3f6c2a91-8b4d-4e57-9c10-7a2e5d4b1650");

        public override string AuthorName => "dxsevaulten";

        public override string AuthorContact => "https://github.com/dxsevaulten/Nomhorrant-165";

        public override string Version => "0.1.0";

        public override System.Drawing.Bitmap? Icon => null;
    }

    /// <summary>Konstanta kategori untuk palette Grasshopper.</summary>
    public static class NomhorrantCategories
    {
        public const string Category = "Nomhorrant";
        public const string SubCategory = "AI";
    }
}
