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

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified Rhino environment tool - create, list, set, delete render environments
    /// </summary>
    [McpServerToolType]
    public class RhinoEnvironmentTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "rhino_environment",
            Description = "Rhino render environment operations - create, list, set current, delete",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["list"] = new ActionInfo
                {
                    Name = "list",
                    Description = "List all render environments",
                    Example = "action='list'"
                },
                ["current"] = new ActionInfo
                {
                    Name = "current",
                    Description = "Get current environment for each usage type",
                    Example = "action='current'"
                },
                ["set"] = new ActionInfo
                {
                    Name = "set",
                    Description = "Set the current render environment",
                    Required = new[] { "environment" },
                    Optional = new[] { "usage" },
                    Example = "action='set', environment='Studio', usage='all'",
                    Tips = new[] { "usage: 'background', 'lighting', 'reflection', or 'all'" }
                },
                ["create"] = new ActionInfo
                {
                    Name = "create",
                    Description = "Create a solid-color environment",
                    Required = new[] { "name", "color" },
                    Example = "action='create', name='Blue Sky', color='#87CEEB'"
                },
                ["delete"] = new ActionInfo
                {
                    Name = "delete",
                    Description = "Delete an environment",
                    Required = new[] { "name" },
                    Example = "action='delete', name='Blue Sky'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            }
        };

        public RhinoEnvironmentTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Environment operations. Actions: list|current|set|create|delete|help")]
        public string RhinoEnvironment(
            [Description("Action to perform")] string action,
            [Description("Environment name or GUID")] string environment = null,
            [Description("Usage: 'background', 'lighting', 'reflection', 'all'")] string usage = "all",
            [Description("Environment name for create")] string name = null,
            [Description("Background color as hex '#RRGGBB' or RGB")] string color = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("environment", environment),
                ("usage", usage),
                ("name", name),
                ("color", color)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "list" => ActionList(),
                "current" => ActionCurrent(),
                "set" => ActionSet(environment, usage),
                "create" => ActionCreate(name, color),
                "delete" => ActionDelete(name),
                _ => ToolHelpers.ErrorResponse($"Unknown action: {action}")
            };
        }

        private string ActionList()
        {
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
                        envInfo["backgroundColor"] = ToolHelpers.ColorToHex(simEnv.BackgroundColor);

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

        private string ActionCurrent()
        {
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

        private string ActionSet(string environment, string usage)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                RenderEnvironment targetEnv = null;

                if (Guid.TryParse(environment, out var guid))
                    targetEnv = rhinoDoc.RenderEnvironments.FirstOrDefault(e => e.Id == guid);

                if (targetEnv == null)
                    targetEnv = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                        e.Name.Equals(environment, StringComparison.OrdinalIgnoreCase));

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
                        return ToolHelpers.ErrorResponse($"Invalid usage: {usage}. Use 'background', 'lighting', 'reflection', or 'all'");
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

        private string ActionCreate(string name, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseColor(color, out var bgColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}. Use hex '#RRGGBB' or RGB '255,128,0'");

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

                var simEnv = new SimulatedEnvironment();
                simEnv.BackgroundColor = bgColor;

                var renderEnv = RenderEnvironment.NewBasicEnvironment(simEnv, rhinoDoc);
                if (renderEnv == null)
                    return ToolHelpers.ErrorResponse("Failed to create environment");

                renderEnv.Name = name;
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

        private string ActionDelete(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var env = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                    e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (env == null)
                    return ToolHelpers.ErrorResponse($"Environment '{name}' not found");

                var envId = env.Id.ToString();
                var deleted = rhinoDoc.RenderEnvironments.Remove(env);

                return JsonConvert.SerializeObject(new { success = deleted, name, id = envId, deleted });
            });
        }
    }
}
