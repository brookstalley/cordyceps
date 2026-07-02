using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Render;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoRenderTool
    {
        // Map user-friendly slot names to internal PBR child slot names
        private static readonly Dictionary<string, string> PbrSlotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["base-color"] = "pbr-base-color",
            ["roughness"] = "pbr-roughness",
            ["metallic"] = "pbr-metallic",
            ["bump"] = "pbr-bump",
            ["opacity"] = "pbr-opacity",
            ["emission"] = "pbr-emission",
            ["displacement"] = "pbr-displacement",
            ["ambient-occlusion"] = "pbr-ambient-occlusion",
            ["clearcoat"] = "pbr-clearcoat",
            ["clearcoat-roughness"] = "pbr-clearcoat-roughness"
        };

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

                    // Report which PBR slots have textures assigned
                    var textureSlots = new List<string>();
                    foreach (var kvp in PbrSlotMap)
                    {
                        var child = renderMaterial.FindChild(kvp.Value);
                        if (child != null)
                            textureSlots.Add(kvp.Key);
                    }
                    if (textureSlots.Count > 0)
                        materialInfo["textures"] = textureSlots;

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

        private string ActionMaterialTexture(string name, string slot, string path, string repeat, double amount)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("name is required");

                if (string.IsNullOrEmpty(slot))
                    return ToolHelpers.ErrorResponse("slot is required. Valid slots: " + string.Join(", ", PbrSlotMap.Keys));

                if (!PbrSlotMap.TryGetValue(slot, out var internalSlot))
                {
                    return ToolHelpers.ErrorResponse($"Unknown slot: '{slot}'. Valid slots: {string.Join(", ", PbrSlotMap.Keys)}");
                }

                var renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (renderMaterial == null)
                    return ToolHelpers.ErrorResponse($"Material '{name}' not found. Use material_list to see available materials.");

                bool isRemove = string.IsNullOrEmpty(path);

                if (!isRemove && !File.Exists(path))
                    return ToolHelpers.ErrorResponse($"Texture file not found: {path}");

                // Parse UV repeat
                double repeatU = 1.0, repeatV = 1.0;
                if (!string.IsNullOrEmpty(repeat))
                {
                    var parts = repeat.Split(',');
                    if (parts.Length == 2 &&
                        double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ru) &&
                        double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rv))
                    {
                        repeatU = ru;
                        repeatV = rv;
                    }
                    else
                    {
                        return ToolHelpers.ErrorResponse($"Invalid repeat format: '{repeat}'. Expected 'u,v' (e.g., '4,4')");
                    }
                }

                // Clamp amount to 0-100
                amount = Math.Max(0, Math.Min(100, amount));

                try
                {
                    renderMaterial.BeginChange(RenderContent.ChangeContexts.Program);

                    if (isRemove)
                    {
                        // Remove the texture from this slot
                        renderMaterial.SetChild(null, internalSlot);
                        renderMaterial.SetChildSlotOn(internalSlot, false, RenderContent.ChangeContexts.Program);
                        renderMaterial.EndChange();

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            material = renderMaterial.Name,
                            slot,
                            removed = true
                        });
                    }

                    // Create a bitmap texture
                    var bmtex = RenderContentType.NewContentFromTypeId(ContentUuids.BitmapTextureType) as RenderTexture;
                    if (bmtex == null)
                    {
                        renderMaterial.EndChange();
                        return ToolHelpers.ErrorResponse("Failed to create bitmap texture");
                    }

                    bmtex.BeginChange(RenderContent.ChangeContexts.Program);
                    bmtex.SetParameter("filename", path);

                    // Apply UV repeat
                    if (repeatU != 1.0 || repeatV != 1.0)
                    {
                        bmtex.SetRepeat(new Vector3d(repeatU, repeatV, 1.0), RenderContent.ChangeContexts.Program);
                    }

                    bmtex.EndChange();

                    // Assign the texture to the material slot
                    renderMaterial.SetChild(bmtex, internalSlot);
                    renderMaterial.SetChildSlotOn(internalSlot, true, RenderContent.ChangeContexts.Program);
                    renderMaterial.SetChildSlotAmount(internalSlot, amount, RenderContent.ChangeContexts.Program);
                    renderMaterial.EndChange();

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        material = renderMaterial.Name,
                        slot,
                        path,
                        repeat = $"{repeatU},{repeatV}",
                        amount
                    });
                }
                catch (Exception ex)
                {
                    try { renderMaterial.EndChange(); }
                    catch (Exception endEx) { DebugLog.Debug($"EndChange during texture-error cleanup failed: {endEx.Message}"); }
                    return ToolHelpers.ErrorResponse($"Failed to set texture: {ex.Message}");
                }
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
                            catch (Exception ex)
                            {
                                DebugLog.Debug($"SetParameter('{paramName}') failed: {ex.Message}");
                            }
                        }

                        if (!colorSet)
                        {
                            try
                            {
                                var sim = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
                                if (sim != null)
                                    sim.DiffuseColor = baseColor;
                            }
                            catch (Exception ex)
                            {
                                DebugLog.Debug($"Material simulation fallback failed: {ex.Message}");
                            }
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

                // Set the PBR parameters on the Rhino.DocObjects.Material BEFORE creating the
                // document RenderMaterial. The previous approach called
                // RenderMaterial.ConvertToPhysicallyBased(), which returns a DETACHED converted
                // copy — setting PBR values on it never reached the material added to the doc,
                // so roughness/metallic/emission/opacity were silently discarded.
                var notApplied = new List<string>();
                // Which PBR params did the caller actually provide? (Detected as differs-from-
                // default: roughness=0.5, metallic=0, transparency=0 -> opacity, ior=1.0,
                // emission non-empty.) Fallback paths report only these in notApplied instead of
                // unconditionally listing every PBR param the caller may never have passed.
                var providedPbrParams = new List<string>();
                if (roughness != 0.5) providedPbrParams.Add("roughness");
                if (metallic != 0) providedPbrParams.Add("metallic");
                if (!string.IsNullOrEmpty(emission)) providedPbrParams.Add("emission");
                if (transparency != 0 || ior != 1.0) providedPbrParams.Add("opacity");
                RenderMaterial renderMaterial;
                try
                {
                    basicMaterial.ToPhysicallyBased(); // in-place conversion (RhinoCommon 7.0+)
                    var pbr = basicMaterial.PhysicallyBased;
                    if (pbr != null)
                    {
                        pbr.BaseColor = Color4f.FromArgb(1, baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f);
                        pbr.Roughness = roughness;
                        pbr.Metallic = metallic;
                        pbr.Opacity = 1.0 - transparency;
                        pbr.OpacityIOR = ior;

                        if (!string.IsNullOrEmpty(emission))
                        {
                            if (ToolHelpers.TryParseColor(emission, out var emissionColor))
                                pbr.Emission = Color4f.FromArgb(1, emissionColor.R / 255f, emissionColor.G / 255f, emissionColor.B / 255f);
                            else
                                notApplied.Add($"emission (invalid color: '{emission}')");
                        }

                        pbr.SynchronizeLegacyMaterial();
                    }
                    else
                    {
                        notApplied.AddRange(providedPbrParams);
                        DebugLog.Warn("material_create: PhysicallyBased accessor unavailable after ToPhysicallyBased(); PBR params not applied");
                    }

                    // FromMaterial creates a Physically Based render material when
                    // material.IsPhysicallyBased is true, carrying the PBR values into the doc.
                    renderMaterial = RenderMaterial.FromMaterial(basicMaterial, rhinoDoc);
                }
                catch (Exception ex)
                {
                    DebugLog.Warn($"material_create: PBR conversion failed, falling back to basic material: {ex.Message}");
                    notApplied.Clear(); // the emission entry may already be there; rebuild from provided params only
                    notApplied.AddRange(providedPbrParams);
                    renderMaterial = null;
                }

                if (renderMaterial == null)
                    renderMaterial = RenderMaterial.CreateBasicMaterial(basicMaterial, rhinoDoc);
                if (renderMaterial == null)
                    return ToolHelpers.ErrorResponse($"Failed to create render material '{name}'");

                renderMaterial.Name = name;
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
                    ior,
                    notApplied = notApplied.Count > 0 ? notApplied : null,
                    note = notApplied.Count > 0
                        ? "Some PBR parameters could not be applied through the SDK; the listed values were dropped."
                        : null
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
                    // Legacy materials: resolve by name first (same lookup material_delete
                    // uses), then fall back to a numeric-index string.
                    materialIndex = rhinoDoc.Materials.Find(material, true);
                    if (materialIndex < 0 && int.TryParse(material, out var idx) && idx >= 0 && idx < rhinoDoc.Materials.Count)
                        materialIndex = idx;
                    if (materialIndex < 0)
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
                    if (!deleted)
                        return ToolHelpers.ErrorResponse($"Failed to delete material '{name}' — it may be in use by objects or layers; unassign it first");
                    return JsonConvert.SerializeObject(new { success = true, name, deleted = true });
                }

                var materialIndex = rhinoDoc.Materials.Find(name, true);
                if (materialIndex >= 0)
                {
                    var deleted = rhinoDoc.Materials.DeleteAt(materialIndex);
                    if (!deleted)
                        return ToolHelpers.ErrorResponse($"Failed to delete legacy material '{name}' — it may be in use by objects or layers; unassign it first");
                    return JsonConvert.SerializeObject(new { success = true, name, deleted = true });
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
                if (!deleted)
                    return ToolHelpers.ErrorResponse($"Failed to delete environment '{name}' — it may be in use (e.g. assigned as background, skylight, or reflection environment)");

                return JsonConvert.SerializeObject(new { success = true, name, id = envId, deleted = true });
            });
        }

        #endregion
    }
}
