using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace Cordyceps
{
    public class CordycepsInfo : GH_AssemblyInfo
    {
        public override string Name => "Cordyceps";

        public override Bitmap Icon => null;

        public override string Description => "Grasshopper MCP Bridge - Claude takes control of Grasshopper";

        public override Guid Id => new Guid("a7b8c9d0-e1f2-3456-7890-abcdef012345");

        public override string AuthorName => "Brooks Talley";

        public override string AuthorContact => "https://github.com/brookstalley/cordyceps";

        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}
