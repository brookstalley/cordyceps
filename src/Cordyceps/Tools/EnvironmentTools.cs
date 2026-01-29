using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.Render;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Rhino render environment operations (enumerate, select, create, delete)
    /// </summary>
    [McpServerToolType]
    public class EnvironmentTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public EnvironmentTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("List all render environments in the Rhino document.")]
        public string RhinoGetEnvironments()
        {
            _server?.RecordCommand("rhino_get_environments");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var environments = new List<object>();

                foreach (var env in rhinoDoc.RenderEnvironments)
                {
                    var simEnv = env.SimulateEnvironment(true);

                    var envInfo = new Dictionary<string, object>
                    {
                        ["id"] = env.Id.ToString(),
                        ["name"] = env.Name,
                        ["typeName"] = env.TypeName
                    };

                    if (simEnv != null)
                    {
                        envInfo["backgroundColor"] = ToolHelpers.ColorToHex(simEnv.BackgroundColor);
                    }

                    environments.Add(envInfo);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = environments.Count,
                    environments
                });
            });
        }

        [McpServerTool, Description("Get the current render environment for each usage type (background, lighting, reflection).")]
        public string RhinoGetCurrentEnvironment()
        {
            _server?.RecordCommand("rhino_get_current_environment");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;

                object GetEnvInfo(RenderSettings.EnvironmentUsage usage)
                {
                    var env = rs.RenderEnvironment(usage, RenderSettings.EnvironmentPurpose.Standard);
                    if (env == null) return null;
                    return new { id = env.Id.ToString(), name = env.Name };
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    background = GetEnvInfo(RenderSettings.EnvironmentUsage.Background),
                    lighting = GetEnvInfo(RenderSettings.EnvironmentUsage.Skylighting),
                    reflection = GetEnvInfo(RenderSettings.EnvironmentUsage.Reflection)
                });
            });
        }

        [McpServerTool, Description("Set the current render environment for a usage type.")]
        public string RhinoSetCurrentEnvironment(
            [Description("Environment name or GUID")] string environment,
            [Description("Usage type: 'background', 'lighting', 'reflection', or 'all' (default)")] string usage = "all")
        {
            _server?.RecordCommand("rhino_set_current_environment");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(environment))
                    return ToolHelpers.ErrorResponse("Environment name or GUID is required");

                // Find the environment by name or GUID
                RenderEnvironment targetEnv = null;

                // Try by GUID first
                if (Guid.TryParse(environment, out var guid))
                {
                    targetEnv = rhinoDoc.RenderEnvironments.FirstOrDefault(e => e.Id == guid);
                }

                // Try by name
                if (targetEnv == null)
                {
                    targetEnv = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                        e.Name.Equals(environment, StringComparison.OrdinalIgnoreCase));
                }

                if (targetEnv == null)
                {
                    var available = string.Join(", ", rhinoDoc.RenderEnvironments.Select(e => e.Name));
                    return ToolHelpers.ErrorResponse($"Environment '{environment}' not found. Available: {available}");
                }

                var rs = rhinoDoc.RenderSettings;
                var modified = new List<string>();

                usage = usage?.ToLowerInvariant() ?? "all";

                switch (usage)
                {
                    case "background":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Background, targetEnv);
                        modified.Add("background");
                        break;
                    case "lighting":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Skylighting, targetEnv);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting, true);
                        modified.Add("lighting");
                        break;
                    case "reflection":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Reflection, targetEnv);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Reflection, true);
                        modified.Add("reflection");
                        break;
                    case "all":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Background, targetEnv);
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Skylighting, targetEnv);
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Reflection, targetEnv);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting, true);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Reflection, true);
                        modified.AddRange(new[] { "background", "lighting", "reflection" });
                        break;
                    default:
                        return ToolHelpers.ErrorResponse($"Invalid usage type: {usage}. Use 'background', 'lighting', 'reflection', or 'all'");
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    environment = targetEnv.Name,
                    environmentId = targetEnv.Id.ToString(),
                    modified
                });
            });
        }

        [McpServerTool, Description("Create a basic solid-color render environment.")]
        public string RhinoCreateEnvironment(
            [Description("Environment name (must be unique)")] string name,
            [Description("Background color as hex '#RRGGBB' or RGB '255,128,0'")] string color)
        {
            _server?.RecordCommand("rhino_create_environment");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Environment name is required");

                if (string.IsNullOrEmpty(color))
                    return ToolHelpers.ErrorResponse("Color is required");

                // Parse color
                if (!ToolHelpers.TryParseColor(color, out var bgColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}. Use hex '#RRGGBB' or RGB '255,128,0'");

                // Check if environment already exists
                var existing = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                    e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = false,
                        alreadyExists = true,
                        id = existing.Id.ToString(),
                        name = existing.Name
                    });
                }

                // Create a simulated environment
                var simEnv = new SimulatedEnvironment();
                simEnv.BackgroundColor = bgColor;

                // Create render environment from simulation
                var renderEnv = RenderEnvironment.NewBasicEnvironment(simEnv, rhinoDoc);
                if (renderEnv == null)
                    return ToolHelpers.ErrorResponse("Failed to create environment");

                // Set the name
                renderEnv.Name = name;

                // Add to document - CRITICAL: must add before setting as current
                rhinoDoc.RenderEnvironments.Add(renderEnv);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    created = true,
                    id = renderEnv.Id.ToString(),
                    name = renderEnv.Name,
                    color = ToolHelpers.ColorToHex(bgColor)
                });
            });
        }

        [McpServerTool, Description("Delete a render environment from the document.")]
        public string RhinoDeleteEnvironment(
            [Description("Environment name")] string name)
        {
            _server?.RecordCommand("rhino_delete_environment");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Environment name is required");

                // Find environment by name
                var env = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                    e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (env == null)
                    return ToolHelpers.ErrorResponse($"Environment '{name}' not found");

                var envId = env.Id.ToString();
                var deleted = rhinoDoc.RenderEnvironments.Remove(env);

                return JsonConvert.SerializeObject(new
                {
                    success = deleted,
                    name,
                    id = envId,
                    deleted
                });
            });
        }

    }
}
