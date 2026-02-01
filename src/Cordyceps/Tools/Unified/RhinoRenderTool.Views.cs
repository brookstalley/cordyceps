using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoRenderTool
    {
        #region Named View Actions

        private string ActionViewSave(string name, string view)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("name is required");

                var targetView = GetView(doc, view);
                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View not found: {view ?? "active"}");

                var vp = targetView.ActiveViewport;

                // Check if named view already exists
                var existingIndex = doc.NamedViews.FindByName(name);
                if (existingIndex >= 0)
                {
                    // Update existing named view
                    doc.NamedViews.Delete(existingIndex);
                }

                var index = doc.NamedViews.Add(name, vp.Id);

                return JsonConvert.SerializeObject(new
                {
                    success = index >= 0,
                    name,
                    index,
                    updated = existingIndex >= 0,
                    viewportName = vp.Name,
                    location = $"{vp.CameraLocation.X:F3},{vp.CameraLocation.Y:F3},{vp.CameraLocation.Z:F3}",
                    target = $"{vp.CameraTarget.X:F3},{vp.CameraTarget.Y:F3},{vp.CameraTarget.Z:F3}"
                });
            });
        }

        private string ActionViewLoad(string name, string view)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("name is required");

                var index = doc.NamedViews.FindByName(name);
                if (index < 0)
                {
                    var available = doc.NamedViews.Count > 0
                        ? string.Join(", ", Enumerable.Range(0, doc.NamedViews.Count).Select(i => doc.NamedViews[i].Name))
                        : "(none)";
                    return ToolHelpers.ErrorResponse($"Named view '{name}' not found. Available: {available}");
                }

                var targetView = GetView(doc, view);
                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View not found: {view ?? "active"}");

                var restored = doc.NamedViews.Restore(index, targetView.ActiveViewport);
                doc.Views.Redraw();

                var vp = targetView.ActiveViewport;
                return JsonConvert.SerializeObject(new
                {
                    success = restored,
                    name,
                    viewportName = vp.Name,
                    location = $"{vp.CameraLocation.X:F3},{vp.CameraLocation.Y:F3},{vp.CameraLocation.Z:F3}",
                    target = $"{vp.CameraTarget.X:F3},{vp.CameraTarget.Y:F3},{vp.CameraTarget.Z:F3}"
                });
            });
        }

        private string ActionViewList()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var views = new List<object>();
                for (int i = 0; i < doc.NamedViews.Count; i++)
                {
                    var namedView = doc.NamedViews[i];
                    views.Add(new
                    {
                        index = i,
                        name = namedView.Name
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = views.Count,
                    namedViews = views
                });
            });
        }

        private string ActionViewDelete(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("name is required");

                var index = doc.NamedViews.FindByName(name);
                if (index < 0)
                    return ToolHelpers.ErrorResponse($"Named view '{name}' not found");

                var deleted = doc.NamedViews.Delete(index);
                return JsonConvert.SerializeObject(new { success = deleted, name, deleted });
            });
        }

        #endregion
    }
}
