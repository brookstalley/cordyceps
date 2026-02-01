using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoRenderTool
    {
        #region Material Actions

        private string ActionMaterialList()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var materials = new List<object>();

                foreach (var renderMaterial in rhinoDoc.RenderMaterials)
                {
                    var pbr = renderMaterial.ConvertToPhysicallyBased(RenderTexture.TextureGeneration.Allow);

                    var materialInfo = new Dictionary<string, object>
                    {
                        ["id"] = renderMaterial.Id.ToString(),
                        ["name"] = renderMaterial.Name,
                        ["type"] = "RenderMaterial"
                    };

                    if (pbr != null)
                    {
                        materialInfo["baseColor"] = ToolHelpers.ColorToHex(pbr.BaseColor);
                        materialInfo["roughness"] = pbr.Roughness;
                        materialInfo["metallic"] = pbr.Metallic;
                        materialInfo["opacity"] = pbr.Opacity;
                        materialInfo["ior"] = pbr.OpacityIOR;
                    }

                    materials.Add(materialInfo);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = materials.Count,
                    materials
                });
            });
        }

        private string ActionMaterialLibrary()
        {
            var types = BuiltInMaterialTypes.Select(kvp => new
            {
                name = kvp.Key,
                guid = kvp.Value.ToString(),
                description = GetMaterialTypeDescription(kvp.Key)
            }).ToList();

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = types.Count,
                types,
                usage = "Use action='material_instantiate', type='<type>' to create"
            });
        }

        private static string GetMaterialTypeDescription(string typeName)
        {
            return typeName switch
            {
                "Metal" => "Metallic materials (gold, copper, silver, aluminum)",
                "Glass" => "Transparent with refraction (ior=1.5)",
                "Plastic" => "Non-metallic with varying glossiness",
                "Paint" => "Painted surface with color and sheen",
                "Gem" => "Gemstone materials with dispersion",
                "Plaster" => "Matte diffuse (concrete, stone)",
                "Picture" => "Image-based materials",
                "PhysicallyBased" => "Full PBR material",
                "Blend" => "Blend between two materials",
                "DoubleSided" => "Different materials front/back",
                "Emission" => "Light-emitting materials",
                _ => "Material type"
            };
        }

        private string ActionMaterialInstantiate(string type, string name, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(type))
                    return ToolHelpers.ErrorResponse("type is required. Use action='material_library' to see types.");

                if (!BuiltInMaterialTypes.TryGetValue(type, out var typeGuid))
                {
                    var availableTypes = string.Join(", ", BuiltInMaterialTypes.Keys);
                    return ToolHelpers.ErrorResponse($"Unknown type: '{type}'. Available: {availableTypes}");
                }

                var materialName = !string.IsNullOrEmpty(name) ? name : $"{type} Material";

                var existing = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = false,
                        alreadyExists = true,
                        id = existing.Id.ToString(),
                        name = existing.Name,
                        type
                    });
                }

                try
                {
                    var renderMaterial = RenderContentType.NewContentFromTypeId(typeGuid) as RenderMaterial;
                    if (renderMaterial == null)
                        return ToolHelpers.ErrorResponse($"Failed to create material of type '{type}'");

                    renderMaterial.BeginChange(RenderContent.ChangeContexts.Program);
                    renderMaterial.Name = materialName;

                    if (!string.IsNullOrEmpty(color) && ToolHelpers.TryParseColor(color, out var baseColor))
                    {
                        var color4f = Color4f.FromArgb(1, baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f);
                        var colorParamNames = new[] { "color", "diffuse", "diffuse-color", "base-color", "Color" };
                        bool colorSet = false;

                        foreach (var paramName in colorParamNames)
                        {
                            try
                            {
                                if (renderMaterial.SetParameter(paramName, color4f))
                                {
                                    colorSet = true;
                                    break;
                                }
                            }
                            catch { }
                        }

                        if (!colorSet)
                        {
                            try
                            {
                                var sim = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
                                if (sim != null)
                                    sim.DiffuseColor = baseColor;
                            }
                            catch { }
                        }
                    }

                    renderMaterial.EndChange();
                    rhinoDoc.RenderMaterials.Add(renderMaterial);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = true,
                        id = renderMaterial.Id.ToString(),
                        name = renderMaterial.Name,
                        type,
                        typeGuid = typeGuid.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Failed to instantiate material: {ex.Message}");
                }
            });
        }

        private string ActionMaterialCreate(string name, string color, double roughness, double metallic, double transparency, string emission, double ior)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseColor(color, out var baseColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}");

                var existing = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
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

                roughness = Math.Max(0, Math.Min(1, roughness));
                metallic = Math.Max(0, Math.Min(1, metallic));
                transparency = Math.Max(0, Math.Min(1, transparency));
                ior = Math.Max(1, Math.Min(3, ior));

                var basicMaterial = new Material
                {
                    Name = name,
                    DiffuseColor = baseColor,
                    Transparency = transparency,
                    Reflectivity = metallic * 0.5,
                    IndexOfRefraction = ior
                };

                var renderMaterial = RenderMaterial.CreateBasicMaterial(basicMaterial, rhinoDoc);

                try
                {
                    var pbr = renderMaterial.ConvertToPhysicallyBased(RenderTexture.TextureGeneration.Allow);
                    if (pbr != null)
                    {
                        pbr.BaseColor = Color4f.FromArgb(1, baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f);
                        pbr.Roughness = roughness;
                        pbr.Metallic = metallic;
                        pbr.Opacity = 1.0 - transparency;
                        pbr.OpacityIOR = ior;

                        if (!string.IsNullOrEmpty(emission) && ToolHelpers.TryParseColor(emission, out var emissionColor))
                            pbr.Emission = Color4f.FromArgb(1, emissionColor.R / 255f, emissionColor.G / 255f, emissionColor.B / 255f);
                    }
                }
                catch { }

                rhinoDoc.RenderMaterials.Add(renderMaterial);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    created = true,
                    id = renderMaterial.Id.ToString(),
                    name = renderMaterial.Name,
                    color = ToolHelpers.ColorToHex(baseColor),
                    roughness,
                    metallic,
                    transparency,
                    ior
                });
            });
        }

        private string ActionMaterialApply(string objectIds, string material)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseGuidArray(objectIds, out var guids, out var parseError))
                    return ToolHelpers.ErrorResponse(parseError);

                RenderMaterial renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(material, StringComparison.OrdinalIgnoreCase));

                int materialIndex = -1;
                if (renderMaterial == null)
                {
                    if (int.TryParse(material, out var idx) && idx >= 0 && idx < rhinoDoc.Materials.Count)
                        materialIndex = idx;
                    else
                        return ToolHelpers.ErrorResponse($"Material '{material}' not found");
                }

                int succeeded = 0, failed = 0;

                foreach (var guid in guids)
                {
                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null) { failed++; continue; }

                    var attrs = obj.Attributes.Duplicate();
                    attrs.MaterialSource = ObjectMaterialSource.MaterialFromObject;

                    if (renderMaterial != null)
                        attrs.RenderMaterial = renderMaterial;
                    else
                        attrs.MaterialIndex = materialIndex;

                    if (rhinoDoc.Objects.ModifyAttributes(obj, attrs, true))
                        succeeded++;
                    else
                        failed++;
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = guids.Count,
                    succeeded,
                    failed,
                    material = renderMaterial?.Name ?? material
                });
            });
        }

        private string ActionMaterialDelete(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (renderMaterial != null)
                {
                    var deleted = rhinoDoc.RenderMaterials.Remove(renderMaterial);
                    return JsonConvert.SerializeObject(new { success = deleted, name, deleted });
                }

                var materialIndex = rhinoDoc.Materials.Find(name, true);
                if (materialIndex >= 0)
                {
                    var deleted = rhinoDoc.Materials.DeleteAt(materialIndex);
                    return JsonConvert.SerializeObject(new { success = deleted, name, deleted });
                }

                return ToolHelpers.ErrorResponse($"Material '{name}' not found");
            });
        }

        #endregion

        #region Environment Actions

        private string ActionEnvList()
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

        private string ActionEnvCurrent()
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

        private string ActionEnvSet(string environment, string usage)
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

        private string ActionEnvCreate(string name, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseColor(color, out var bgColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}");

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

        private string ActionEnvDelete(string name)
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

        #endregion
    }
}
