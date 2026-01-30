using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified Rhino material tool - create, list, apply, delete materials
    /// </summary>
    [McpServerToolType]
    public class RhinoMaterialTool
    {
        private readonly GrasshopperContext _context;

        // Built-in material types mapped to ContentUuids
        private static readonly Dictionary<string, Guid> BuiltInMaterialTypes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["Metal"] = ContentUuids.MetalMaterialType,
            ["Glass"] = ContentUuids.GlassMaterialType,
            ["Plastic"] = ContentUuids.PlasticMaterialType,
            ["Paint"] = ContentUuids.PaintMaterialType,
            ["Gem"] = ContentUuids.GemMaterialType,
            ["Plaster"] = ContentUuids.PlasterMaterialType,
            ["Picture"] = ContentUuids.PictureMaterialType,
            ["PhysicallyBased"] = ContentUuids.PhysicallyBasedMaterialType,
            ["Blend"] = ContentUuids.BlendMaterialType,
            ["DoubleSided"] = ContentUuids.DoubleSidedMaterialType,
            ["Emission"] = ContentUuids.EmissionMaterialType
        };

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "rhino_material",
            Description = "Rhino material operations - create PBR materials, apply to objects, list, delete, use built-in library types",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["list"] = new ActionInfo
                {
                    Name = "list",
                    Description = "List all materials in the document",
                    Example = "action='list'"
                },
                ["library"] = new ActionInfo
                {
                    Name = "library",
                    Description = "List available built-in material types (Metal, Glass, Plastic, etc.)",
                    Example = "action='library'"
                },
                ["instantiate"] = new ActionInfo
                {
                    Name = "instantiate",
                    Description = "Create a material from a built-in type",
                    Required = new[] { "type" },
                    Optional = new[] { "name", "color" },
                    Example = "action='instantiate', type='Metal', name='Copper', color='#B87333'",
                    Tips = new[] { "Types: Metal, Glass, Plastic, Paint, Gem, Plaster, Emission", "name defaults to type name if not provided" }
                },
                ["create"] = new ActionInfo
                {
                    Name = "create",
                    Description = "Create a PBR material from scratch",
                    Required = new[] { "name", "color" },
                    Optional = new[] { "roughness", "metallic", "transparency", "emission", "ior" },
                    Example = "action='create', name='Red Metal', color='#FF0000', metallic=1, roughness=0.3",
                    Tips = new[] { "roughness: 0=mirror, 1=matte", "metallic: 0=dielectric, 1=metal" }
                },
                ["apply"] = new ActionInfo
                {
                    Name = "apply",
                    Description = "Apply material to objects",
                    Required = new[] { "ids", "material" },
                    Example = "action='apply', ids='[\"abc\",\"def\"]', material='Red Metal'"
                },
                ["delete"] = new ActionInfo
                {
                    Name = "delete",
                    Description = "Delete a material",
                    Required = new[] { "name" },
                    Example = "action='delete', name='Red Metal'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            }
        };

        public RhinoMaterialTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Material operations. Actions: list|library|instantiate|create|apply|delete|help")]
        public string RhinoMaterial(
            [Description("Action to perform")] string action,
            [Description("Material name")] string name = null,
            [Description("Built-in material type (Metal, Glass, Plastic, Paint, Gem, Plaster, Emission, etc.)")] string type = null,
            [Description("Base color as hex '#RRGGBB' or RGB '255,128,0'")] string color = null,
            [Description("Roughness 0-1 (polished=0.1, stone=0.7, matte=0.9)")] double roughness = 0.5,
            [Description("Metalness 0-1 (0=dielectric, 1=metal)")] double metallic = 0,
            [Description("Transparency 0-1 (0=opaque, 1=transparent)")] double transparency = 0,
            [Description("Emission color (for glowing materials)")] string emission = null,
            [Description("IOR (glass=1.5, water=1.33, diamond=2.42)")] double ior = 1.0,
            [Description("JSON array of object GUIDs")] string ids = null,
            [Description("Material name to apply")] string material = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("name", name),
                ("type", type),
                ("color", color),
                ("roughness", roughness),
                ("metallic", metallic),
                ("transparency", transparency),
                ("emission", emission),
                ("ior", ior),
                ("ids", ids),
                ("material", material)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "list" => ActionList(),
                "library" => ActionLibrary(),
                "instantiate" => ActionInstantiate(type, name, color),
                "create" => ActionCreate(name, color, roughness, metallic, transparency, emission, ior),
                "apply" => ActionApply(ids, material),
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

        private string ActionLibrary()
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
                usage = "Use action='instantiate', type='<type>' to create a material from these types"
            });
        }

        private static string GetMaterialTypeDescription(string typeName)
        {
            return typeName switch
            {
                "Metal" => "Metallic materials (gold #FFD700, copper #B87333, silver #C0C0C0, aluminum #A9A9A9)",
                "Glass" => "Transparent with refraction (ior=1.5 for standard glass)",
                "Plastic" => "Non-metallic materials with varying glossiness",
                "Paint" => "Painted surface materials with color and sheen",
                "Gem" => "Gemstone materials with dispersion (diamonds, rubies, emeralds)",
                "Plaster" => "Matte diffuse (concrete #999999, stone #808080, sandstone #D2B48C)",
                "Picture" => "Image-based materials for decals and textures",
                "PhysicallyBased" => "Full PBR material with all parameters",
                "Blend" => "Blend between two materials",
                "DoubleSided" => "Different materials on front and back faces",
                "Emission" => "Light-emitting materials (screens, neon, glowing objects)",
                _ => "Material type"
            };
        }

        private string ActionInstantiate(string type, string name, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(type))
                    return ToolHelpers.ErrorResponse("type is required. Use action='library' to see available types.");

                if (!BuiltInMaterialTypes.TryGetValue(type, out var typeGuid))
                {
                    var availableTypes = string.Join(", ", BuiltInMaterialTypes.Keys);
                    return ToolHelpers.ErrorResponse($"Unknown material type: '{type}'. Available types: {availableTypes}");
                }

                // Use the provided name or generate one based on the type
                var materialName = !string.IsNullOrEmpty(name) ? name : $"{type} Material";

                // Check if a material with this name already exists
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
                    // Create temporary content from the built-in type using NewContentFromTypeId
                    var renderMaterial = RenderContentType.NewContentFromTypeId(typeGuid) as RenderMaterial;

                    if (renderMaterial == null)
                        return ToolHelpers.ErrorResponse($"Failed to create material of type '{type}'. The type GUID may not be available.");

                    // Configure the material
                    renderMaterial.BeginChange(RenderContent.ChangeContexts.Program);
                    renderMaterial.Name = materialName;

                    // Apply color if provided
                    if (!string.IsNullOrEmpty(color) && ToolHelpers.TryParseColor(color, out var baseColor))
                    {
                        var color4f = Color4f.FromArgb(1, baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f);

                        // Try multiple parameter names that different material types might use
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

                        // Also try setting via ToMaterial if direct parameter setting didn't work
                        if (!colorSet)
                        {
                            try
                            {
                                var sim = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
                                if (sim != null)
                                {
                                    sim.DiffuseColor = baseColor;
                                }
                            }
                            catch { }
                        }
                    }

                    renderMaterial.EndChange();

                    // Add the material to the document
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

        private string ActionCreate(string name, string color, double roughness, double metallic, double transparency, string emission, double ior)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseColor(color, out var baseColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}. Use hex '#RRGGBB' or RGB '255,128,0'");

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
                        {
                            pbr.Emission = Color4f.FromArgb(1, emissionColor.R / 255f, emissionColor.G / 255f, emissionColor.B / 255f);
                        }
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

        private string ActionApply(string objectIds, string material)
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

        private string ActionDelete(string name)
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
    }
}
