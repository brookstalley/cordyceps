using Cordyceps.Core;

namespace Cordyceps.Tools.Unified
{
    public partial class GhCanvasTool
    {
        #region Outline Action

        private static ActionInfo OutlineActionInfo()
        {
            return new ActionInfo
            {
                Name = "outline",
                Description = "Compact overview of the canvas (not built yet)",
                Optional = new[] { "id", "group", "depth" },
                Example = "action='outline'"
            };
        }

        private string ActionOutline(string id, string group, int depth)
        {
            return ToolHelpers.ErrorResponse("outline is not built yet");
        }

        #endregion
    }
}
