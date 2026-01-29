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

namespace Cordyceps.Tools
{
    /// <summary>
    /// Rhino document material operations (create PBR materials, apply to objects, query)
    /// </summary>
    [McpServerToolType]
    public class MaterialTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public MaterialTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("List all materials in the Rhino document with their properties.")]
        public string RhinoGetMaterials()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var materials = new List<object>();

                // Get render materials
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

                // Also list basic materials (legacy)
                for (int i = 0; i < rhinoDoc.Materials.Count; i++)
                {
                    var mat = rhinoDoc.Materials[i];
                    if (mat.IsDeleted) continue;

                    materials.Add(new
                    {
                        index = i,
                        name = mat.Name,
                        type = "BasicMaterial",
                        diffuseColor = ToolHelpers.ColorToHex(mat.DiffuseColor),
                        transparency = mat.Transparency,
                        reflectivity = mat.Reflectivity
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = materials.Count,
                    materials
                });
            });
        }

        [McpServerTool, Description("Create a PBR (Physically Based Rendering) material in the Rhino document.")]
        public string RhinoCreateMaterial(
            [Description("Material name (must be unique)")] string name,
            [Description("Base color as hex '#RRGGBB' or RGB '255,128,0'")] string color,
            [Description("Surface roughness 0-1 (0=mirror, 1=matte). Default: 0.5")] double roughness = 0.5,
            [Description("Metalness 0-1 (0=dielectric, 1=metal). Default: 0")] double metallic = 0,
            [Description("Transparency 0-1 (0=opaque, 1=fully transparent). Default: 0")] double transparency = 0,
            [Description("Emission color as hex or RGB (optional, for glowing materials)")] string emission = null,
            [Description("Index of refraction (glass ~1.5, water ~1.33). Default: 1.0")] double ior = 1.0)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Material name is required");

                if (string.IsNullOrEmpty(color))
                    return ToolHelpers.ErrorResponse("Color is required");

                // Parse base color
                if (!ToolHelpers.TryParseColor(color, out var baseColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}. Use hex '#RRGGBB' or RGB '255,128,0'");

                // Check if material already exists
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

                // Clamp values to valid ranges
                roughness = Math.Max(0, Math.Min(1, roughness));
                metallic = Math.Max(0, Math.Min(1, metallic));
                transparency = Math.Max(0, Math.Min(1, transparency));
                ior = Math.Max(1, Math.Min(3, ior));

                // Create a basic material first
                var basicMaterial = new Material
                {
                    Name = name,
                    DiffuseColor = baseColor,
                    Transparency = transparency,
                    Reflectivity = metallic * 0.5, // Approximate
                    IndexOfRefraction = ior
                };

                // Convert to render material with PBR properties
                var renderMaterial = RenderMaterial.CreateBasicMaterial(basicMaterial, rhinoDoc);

                // Try to set PBR properties if available
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

                        // Set emission if provided
                        if (!string.IsNullOrEmpty(emission) && ToolHelpers.TryParseColor(emission, out var emissionColor))
                        {
                            pbr.Emission = Color4f.FromArgb(1, emissionColor.R / 255f, emissionColor.G / 255f, emissionColor.B / 255f);
                        }
                    }
                }
                catch
                {
                    // PBR conversion may not be available, continue with basic material
                }

                // Add to document
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

        [McpServerTool, Description("Apply a material to Rhino objects by material name or index.")]
        public string RhinoApplyMaterial(
            [Description("JSON array of object GUIDs")] string objectIds,
            [Description("Material name or index")] string material)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(material))
                    return ToolHelpers.ErrorResponse("Material name or index is required");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                // Find the material
                RenderMaterial renderMaterial = null;
                int materialIndex = -1;

                // Try by name first
                renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(material, StringComparison.OrdinalIgnoreCase));

                if (renderMaterial == null)
                {
                    // Try by index
                    if (int.TryParse(material, out var idx) && idx >= 0 && idx < rhinoDoc.Materials.Count)
                    {
                        materialIndex = idx;
                    }
                    else
                    {
                        return ToolHelpers.ErrorResponse($"Material '{material}' not found");
                    }
                }

                int succeeded = 0;
                int failed = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        failed++;
                        continue;
                    }

                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null)
                    {
                        results.Add(new { id = idStr, success = false, error = "Object not found" });
                        failed++;
                        continue;
                    }

                    var attrs = obj.Attributes.Duplicate();
                    attrs.MaterialSource = ObjectMaterialSource.MaterialFromObject;

                    if (renderMaterial != null)
                    {
                        // Use render material
                        attrs.RenderMaterial = renderMaterial;
                    }
                    else
                    {
                        // Use basic material index
                        attrs.MaterialIndex = materialIndex;
                    }

                    if (rhinoDoc.Objects.ModifyAttributes(obj, attrs, true))
                    {
                        succeeded++;
                        results.Add(new { id = idStr, success = true });
                    }
                    else
                    {
                        failed++;
                        results.Add(new { id = idStr, success = false, error = "Failed to apply material" });
                    }
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = ids.Count,
                    succeeded,
                    failed,
                    material = renderMaterial?.Name ?? material,
                    results
                });
            });
        }

        [McpServerTool, Description("Delete a material from the Rhino document.")]
        public string RhinoDeleteMaterial(
            [Description("Material name")] string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Material name is required");

                // Find render material
                var renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (renderMaterial != null)
                {
                    var deleted = rhinoDoc.RenderMaterials.Remove(renderMaterial);
                    return JsonConvert.SerializeObject(new
                    {
                        success = deleted,
                        name,
                        type = "RenderMaterial",
                        deleted
                    });
                }

                // Try basic material
                var materialIndex = rhinoDoc.Materials.Find(name, true);
                if (materialIndex >= 0)
                {
                    var deleted = rhinoDoc.Materials.DeleteAt(materialIndex);
                    return JsonConvert.SerializeObject(new
                    {
                        success = deleted,
                        name,
                        type = "BasicMaterial",
                        index = materialIndex,
                        deleted
                    });
                }

                return ToolHelpers.ErrorResponse($"Material '{name}' not found");
            });
        }

    }
}
