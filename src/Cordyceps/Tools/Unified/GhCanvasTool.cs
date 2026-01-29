using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Newtonsoft.Json;
using Rhino;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified canvas tool - component operations (add, delete, move, rename, find, search, list)
    /// </summary>
    [McpServerToolType]
    public class GhCanvasTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_canvas",
            Description = "Component operations on the Grasshopper canvas",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["add"] = new ActionInfo
                {
                    Name = "add",
                    Description = "Add a component to the canvas",
                    Required = new[] { "type", "x", "y" },
                    Optional = new[] { "nickname" },
                    Example = "action='add', type='Circle', x=200, y=100",
                    Tips = new[] { "Use 'Category/Name' format for ambiguous names (e.g., 'Curve/Circle')", "Use GUID for guaranteed accuracy" }
                },
                ["delete"] = new ActionInfo
                {
                    Name = "delete",
                    Description = "Remove component(s) from the canvas",
                    Required = new string[0],
                    Optional = new[] { "id", "ids" },
                    Example = "action='delete', id='abc-123' OR action='delete', ids='[\"a\",\"b\"]'",
                    Tips = new[] { "Use 'id' for single, 'ids' (JSON array) for bulk" }
                },
                ["move"] = new ActionInfo
                {
                    Name = "move",
                    Description = "Move component(s) to new position(s)",
                    Required = new string[0],
                    Optional = new[] { "id", "x", "y", "moves" },
                    Example = "action='move', id='abc', x=100, y=200",
                    Tips = new[] { "Use id/x/y for single, 'moves' JSON array for bulk" }
                },
                ["rename"] = new ActionInfo
                {
                    Name = "rename",
                    Description = "Change a component's nickname (display name)",
                    Required = new[] { "id", "nickname" },
                    Example = "action='rename', id='abc-123', nickname='MyCircle'"
                },
                ["find"] = new ActionInfo
                {
                    Name = "find",
                    Description = "Find component(s) by nickname",
                    Required = new[] { "nickname" },
                    Optional = new[] { "exact" },
                    Example = "action='find', nickname='MyCircle'"
                },
                ["search"] = new ActionInfo
                {
                    Name = "search",
                    Description = "Search available component types (not on canvas)",
                    Required = new[] { "query" },
                    Optional = new[] { "category", "limit" },
                    Example = "action='search', query='circle', category='Curve'"
                },
                ["list"] = new ActionInfo
                {
                    Name = "list",
                    Description = "List components currently on the canvas",
                    Optional = new[] { "category", "type", "group" },
                    Example = "action='list' OR action='list', category='Curve'"
                },
                ["info"] = new ActionInfo
                {
                    Name = "info",
                    Description = "Get detailed info about a component",
                    Required = new[] { "id" },
                    Example = "action='info', id='abc-123'"
                },
                ["bounds"] = new ActionInfo
                {
                    Name = "bounds",
                    Description = "Get component bounding box and dimensions",
                    Required = new[] { "id" },
                    Example = "action='bounds', id='abc-123'"
                },
                ["validate"] = new ActionInfo
                {
                    Name = "validate",
                    Description = "Check canvas for overlaps and spacing issues",
                    Example = "action='validate'"
                },
                ["constant"] = new ActionInfo
                {
                    Name = "constant",
                    Description = "Add a pre-configured constant panel",
                    Required = new[] { "value", "x", "y" },
                    Optional = new[] { "nickname" },
                    Example = "action='constant', value='3.14159', x=50, y=100"
                },
                ["bake"] = new ActionInfo
                {
                    Name = "bake",
                    Description = "Bake component geometry to Rhino document",
                    Required = new[] { "id" },
                    Optional = new[] { "layer", "name" },
                    Example = "action='bake', id='abc-123', layer='Baked'",
                    Tips = new[] { "Creates permanent Rhino objects from component output", "Specify layer to organize baked geometry" }
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Disable solver before bulk operations: gh_document(action='solver', enabled=false)",
                "Recommended spacing: 150px horizontal, 70px vertical between components"
            }
        };

        public GhCanvasTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Component operations. Actions: add|delete|move|rename|find|search|list|info|bounds|validate|constant|bake|help")]
        public string GhCanvas(
            [Description("Action to perform")] string action,
            [Description("Component type for 'add', or search query for 'search'")] string type = null,
            [Description("X position")] double x = double.NaN,
            [Description("Y position")] double y = double.NaN,
            [Description("Component GUID")] string id = null,
            [Description("JSON array of IDs for bulk delete")] string ids = null,
            [Description("JSON array of moves: [{id,x,y},...]")] string moves = null,
            [Description("Nickname for rename/find/add")] string nickname = null,
            [Description("Search query")] string query = null,
            [Description("Category filter")] string category = null,
            [Description("Type filter for list")] string typeFilter = null,
            [Description("Group filter for list")] string group = null,
            [Description("Result limit for search")] int limit = 50,
            [Description("Exact match for find")] bool exact = false,
            [Description("Value for constant panel")] string value = null,
            [Description("Layer name for bake")] string layer = null,
            [Description("Object name for bake")] string name = null)
        {
            // Handle help action
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
            {
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);
            }

            // Build params dictionary for validation
            var providedParams = UnifiedToolHelpers.BuildParams(
                ("type", type),
                ("x", double.IsNaN(x) ? null : (object)x),
                ("y", double.IsNaN(y) ? null : (object)y),
                ("id", id),
                ("ids", ids),
                ("moves", moves),
                ("nickname", nickname),
                ("query", query),
                ("category", category),
                ("typeFilter", typeFilter),
                ("group", group),
                ("limit", limit),
                ("exact", exact),
                ("value", value),
                ("layer", layer),
                ("name", name)
            );

            // Validate action and required params
            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Dispatch to action handler
            return action.ToLowerInvariant() switch
            {
                "add" => ActionAdd(type, x, y, nickname),
                "delete" => ActionDelete(id, ids),
                "move" => ActionMove(id, x, y, moves),
                "rename" => ActionRename(id, nickname),
                "find" => ActionFind(nickname, exact),
                "search" => ActionSearch(query ?? type, category, limit),
                "list" => ActionList(category, typeFilter, group),
                "info" => ActionInfo(id),
                "bounds" => ActionBounds(id),
                "validate" => ActionValidate(),
                "constant" => ActionConstant(value, x, y, nickname),
                "bake" => ActionBake(id, layer, name),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionAdd(string type, double x, double y, string nickname)
        {
            Core.DebugLog.Info($"gh_canvas add: type='{type}', x={x}, y={y}, nickname='{nickname}'");

            return _context.ExecuteOnUiThread(() =>
            {
                try
                {
                    if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    var (component, matches) = ComponentRegistry.TryCreateComponent(type);

                    // Handle disambiguation
                    if (matches != null && matches.Count > 1)
                    {
                        return JsonConvert.SerializeObject(new
                        {
                            success = false,
                            error = "ambiguous_name",
                            message = $"Multiple components match '{type}'. Use GUID or 'Category/Name' format.",
                            matchCount = matches.Count,
                            matches = matches.Select(m => new
                            {
                                name = m.Name,
                                guid = m.Guid,
                                category = m.Category,
                                subcategory = m.SubCategory,
                                role = m.Role
                            })
                        });
                    }

                    if (component == null)
                        return ToolHelpers.ErrorResponse($"Unknown component type: {type}");

                    if (component.Attributes == null)
                        component.CreateAttributes();

                    if (!string.IsNullOrEmpty(nickname))
                        component.NickName = nickname;

                    component.Attributes.Pivot = new PointF((float)x, (float)y);
                    doc.AddObject(component, false);
                    doc.NewSolution(true);

                    var bounds = component.Attributes.Bounds;
                    int inputCount = 0, outputCount = 0;
                    string categoryStr = null, subcategory = null;

                    IGH_ObjectProxy proxy = null;
                    if (component is IGH_ActiveObject activeObj)
                        proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == activeObj.ComponentGuid);
                    proxy ??= Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == component.Name);

                    if (proxy != null)
                    {
                        categoryStr = proxy.Desc.Category;
                        subcategory = proxy.Desc.SubCategory;
                    }

                    if (component is IGH_Component ghComp)
                    {
                        inputCount = ghComp.Params.Input.Count;
                        outputCount = ghComp.Params.Output.Count;
                    }
                    else if (component is IGH_Param)
                    {
                        outputCount = 1;
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        type = component.Name,
                        nickname = component.NickName,
                        category = categoryStr,
                        subcategory,
                        role = ComponentRegistry.GetRole(categoryStr, subcategory),
                        x = component.Attributes.Pivot.X,
                        y = component.Attributes.Pivot.Y,
                        width = bounds.Width,
                        height = bounds.Height,
                        inputCount,
                        outputCount
                    });
                }
                catch (Exception ex)
                {
                    Core.DebugLog.Error($"gh_canvas add failed: {ex.Message}");
                    return ToolHelpers.ErrorResponse(ex.Message);
                }
            });
        }

        private string ActionDelete(string id, string ids)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<string> idList;
                if (!string.IsNullOrEmpty(ids))
                {
                    try { idList = JsonConvert.DeserializeObject<List<string>>(ids); }
                    catch (Exception ex) { return ToolHelpers.ErrorResponse($"Invalid ids format: {ex.Message}"); }
                }
                else if (!string.IsNullOrEmpty(id))
                {
                    idList = new List<string> { id };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Either 'id' or 'ids' is required");
                }

                var results = new List<object>();
                var deletedIds = new List<string>();
                int succeeded = 0, failed = 0;

                foreach (var compId in idList)
                {
                    if (!ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var component, out var compError))
                    {
                        results.Add(new { id = compId, success = false, error = compError });
                        failed++;
                        continue;
                    }

                    try
                    {
                        doc.RemoveObject(component, true);
                        deletedIds.Add(compId);
                        results.Add(new { id = compId, success = true });
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { id = compId, success = false, error = ex.Message });
                        failed++;
                    }
                }

                if (succeeded > 0) doc.NewSolution(true);

                if (idList.Count == 1)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = succeeded == 1,
                        deleted = deletedIds.FirstOrDefault(),
                        error = failed > 0 ? ((dynamic)results[0]).error : null
                    });
                }

                return JsonConvert.SerializeObject(new { success = failed == 0, total = idList.Count, succeeded, failed, deletedIds, results });
            });
        }

        private string ActionMove(string id, double x, double y, string moves)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<dynamic> moveList;
                if (!string.IsNullOrEmpty(moves))
                {
                    if (!ToolHelpers.TryDeserializeList<dynamic>(moves, out moveList, out error))
                        return ToolHelpers.ErrorResponse(error);
                }
                else if (!string.IsNullOrEmpty(id) && !double.IsNaN(x) && !double.IsNaN(y))
                {
                    moveList = new List<dynamic> { new { id, x, y } };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Provide 'id'+'x'+'y' for single move, or 'moves' array for bulk");
                }

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var results = new List<object>();
                int successCount = 0, failCount = 0;

                foreach (var move in moveList)
                {
                    try
                    {
                        string compId = move.id?.ToString();
                        if (string.IsNullOrEmpty(compId) || !Guid.TryParse(compId, out Guid guid))
                        {
                            results.Add(new { success = false, id = compId, error = "Invalid or missing id" });
                            failCount++;
                            continue;
                        }

                        if (infraIds.Contains(guid))
                        {
                            results.Add(new { success = false, id = compId, error = "Protected: required for MCP server" });
                            failCount++;
                            continue;
                        }

                        var component = doc.FindObject(guid, true);
                        if (component == null)
                        {
                            results.Add(new { success = false, id = compId, error = "Component not found" });
                            failCount++;
                            continue;
                        }

                        double moveX = (double)move.x;
                        double moveY = (double)move.y;
                        component.Attributes.Pivot = new PointF((float)moveX, (float)moveY);
                        component.Attributes.ExpireLayout();

                        results.Add(new { success = true, id = compId, x = moveX, y = moveY });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { success = false, id = move.id?.ToString(), error = ex.Message });
                        failCount++;
                    }
                }

                Instances.ActiveCanvas?.Invalidate();

                if (moveList.Count == 1 && successCount == 1)
                {
                    var r = (dynamic)results[0];
                    return JsonConvert.SerializeObject(new { success = true, id = r.id, x = r.x, y = r.y });
                }

                return JsonConvert.SerializeObject(new { success = failCount == 0, total = moveList.Count, succeeded = successCount, failed = failCount, results });
            });
        }

        private string ActionRename(string id, string nickname)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                component.NickName = nickname;
                component.Attributes?.ExpireLayout();
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new { success = true, id, nickname });
            });
        }

        private string ActionFind(string nickname, bool exact)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var matches = new List<Dictionary<string, object>>();

                foreach (var obj in doc.Objects)
                {
                    if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds)) continue;

                    bool isMatch = exact
                        ? obj.NickName == nickname
                        : obj.NickName?.IndexOf(nickname, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isMatch)
                        matches.Add(ToolHelpers.BuildListComponentInfo(obj));
                }

                if (matches.Count == 0)
                    return JsonConvert.SerializeObject(new { success = true, found = false, message = $"No component found with nickname '{nickname}'" });

                if (matches.Count == 1)
                {
                    matches[0]["success"] = true;
                    matches[0]["found"] = true;
                    return JsonConvert.SerializeObject(matches[0]);
                }

                return JsonConvert.SerializeObject(new { success = true, found = true, count = matches.Count, components = matches });
            });
        }

        private string ActionSearch(string query, string category, int limit)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var results = ComponentRegistry.SearchComponents(query);

                if (!string.IsNullOrEmpty(category))
                    results = results.Where(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

                var enhanced = results.Take(limit > 0 ? limit : 50).Select(r => new
                {
                    name = r.Name,
                    description = r.Description,
                    category = r.Category,
                    subcategory = r.SubCategory,
                    role = ComponentRegistry.GetRole(r.Category, r.SubCategory),
                    guid = r.Guid
                }).ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = enhanced.Count,
                    totalMatches = results.Count,
                    components = enhanced
                });
            });
        }

        private string ActionList(string category, string type, string group)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                HashSet<Guid> groupMemberIds = null;
                if (!string.IsNullOrEmpty(group))
                {
                    var targetGroup = FindGroup(doc, group, infraIds);
                    if (targetGroup == null)
                        return ToolHelpers.ErrorResponse($"Group not found: {group}");
                    groupMemberIds = new HashSet<Guid>(targetGroup.ObjectIDs ?? new List<Guid>());
                }

                var components = new List<object>();
                foreach (var obj in doc.Objects)
                {
                    if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds)) continue;
                    if (groupMemberIds != null && !groupMemberIds.Contains(obj.InstanceGuid)) continue;

                    string objCategory = null, objName = null;
                    if (obj is IGH_Component comp)
                    {
                        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == comp.ComponentGuid);
                        objCategory = proxy?.Desc.Category ?? comp.Category;
                        objName = comp.Name;
                    }
                    else if (obj is IGH_Param param)
                    {
                        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == param.ComponentGuid);
                        objCategory = proxy?.Desc.Category ?? "Params";
                        objName = param.Name;
                    }

                    if (!string.IsNullOrEmpty(category) && !string.Equals(objCategory, category, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrEmpty(type))
                    {
                        var typeLower = type.ToLowerInvariant();
                        var nameLower = objName?.ToLowerInvariant() ?? "";
                        if (!nameLower.Contains(typeLower)) continue;
                    }

                    components.Add(ToolHelpers.BuildListComponentInfo(obj));
                }

                return JsonConvert.SerializeObject(new { success = true, count = components.Count, components });
            });
        }

        private string ActionInfo(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var info = ToolHelpers.BuildFullComponentInfo(obj, includeSuccess: true);
                return JsonConvert.SerializeObject(info);
            });
        }

        private string ActionBounds(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var bounds = component.Attributes.Bounds;
                var pivot = component.Attributes.Pivot;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id,
                    name = component.Name,
                    nickname = component.NickName,
                    bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height },
                    pivot = new { x = pivot.X, y = pivot.Y }
                });
            });
        }

        private string ActionValidate()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var overlaps = new List<object>();
                var suggestions = new List<string>();
                var components = doc.Objects.Where(c => !(c is GH_Group)).ToList();

                for (int i = 0; i < components.Count; i++)
                {
                    for (int j = i + 1; j < components.Count; j++)
                    {
                        var bounds1 = components[i].Attributes.Bounds;
                        var bounds2 = components[j].Attributes.Bounds;

                        if (bounds1.IntersectsWith(bounds2))
                        {
                            overlaps.Add(new
                            {
                                component1 = new { id = components[i].InstanceGuid.ToString(), name = components[i].NickName ?? components[i].Name },
                                component2 = new { id = components[j].InstanceGuid.ToString(), name = components[j].NickName ?? components[j].Name }
                            });
                            suggestions.Add($"'{components[i].NickName ?? components[i].Name}' overlaps with '{components[j].NickName ?? components[j].Name}'");
                        }
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    componentCount = components.Count,
                    overlapCount = overlaps.Count,
                    overlaps,
                    suggestions,
                    isClean = overlaps.Count == 0
                });
            });
        }

        private string ActionConstant(string value, double x, double y, string nickname)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var panel = new GH_Panel();
                panel.CreateAttributes();
                panel.Attributes.Pivot = new PointF((float)x, (float)y);
                panel.SetUserText(value);
                panel.NickName = nickname ?? "";

                doc.AddObject(panel, false);
                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = panel.InstanceGuid.ToString(),
                    type = "Panel",
                    value,
                    nickname = panel.NickName,
                    x = panel.Attributes.Pivot.X,
                    y = panel.Attributes.Pivot.Y
                });
            });
        }

        private string ActionBake(string id, string layer, string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is IGH_Component ghComp) || ghComp.Params.Output.Count == 0)
                    return ToolHelpers.ErrorResponse("Component has no bakeable outputs");

                var rhinoDoc = Rhino.RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                // Find or create layer
                int layerIndex = 0;
                if (!string.IsNullOrEmpty(layer))
                {
                    layerIndex = rhinoDoc.Layers.FindByFullPath(layer, -1);
                    if (layerIndex < 0)
                    {
                        layerIndex = rhinoDoc.Layers.Add(layer, System.Drawing.Color.Black);
                    }
                }

                var bakedIds = new List<string>();
                var attr = new Rhino.DocObjects.ObjectAttributes { LayerIndex = layerIndex };
                if (!string.IsNullOrEmpty(name)) attr.Name = name;

                foreach (var output in ghComp.Params.Output)
                {
                    foreach (var item in output.VolatileData.AllData(true))
                    {
                        Guid objId = Guid.Empty;

                        // Handle specific geometry types that need conversion
                        if (item is Grasshopper.Kernel.Types.GH_Circle ghCircle && ghCircle.Value.IsValid)
                        {
                            objId = rhinoDoc.Objects.AddCircle(ghCircle.Value, attr);
                        }
                        else if (item is Grasshopper.Kernel.Types.GH_Arc ghArc && ghArc.Value.IsValid)
                        {
                            objId = rhinoDoc.Objects.AddArc(ghArc.Value, attr);
                        }
                        else if (item is Grasshopper.Kernel.Types.GH_Line ghLine && ghLine.Value.IsValid)
                        {
                            objId = rhinoDoc.Objects.AddLine(ghLine.Value, attr);
                        }
                        else if (item is Grasshopper.Kernel.Types.GH_Point ghPoint && ghPoint.Value.IsValid)
                        {
                            objId = rhinoDoc.Objects.AddPoint(ghPoint.Value, attr);
                        }
                        else if (item is Grasshopper.Kernel.Types.GH_Rectangle ghRect && ghRect.Value.IsValid)
                        {
                            objId = rhinoDoc.Objects.AddPolyline(ghRect.Value.ToPolyline(), attr);
                        }
                        else if (item is Grasshopper.Kernel.Types.GH_Box ghBox && ghBox.Value.IsValid)
                        {
                            var brep = ghBox.Value.ToBrep();
                            if (brep != null)
                                objId = rhinoDoc.Objects.AddBrep(brep, attr);
                        }
                        else if (item is Grasshopper.Kernel.Types.IGH_GeometricGoo geoGoo)
                        {
                            // Try to cast to GeometryBase for other types
                            if (geoGoo.CastTo(out Rhino.Geometry.GeometryBase castGeo) && castGeo != null)
                            {
                                objId = rhinoDoc.Objects.Add(castGeo, attr);
                            }
                        }

                        if (objId != Guid.Empty)
                            bakedIds.Add(objId.ToString());
                    }
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    bakedCount = bakedIds.Count,
                    layer = layer ?? "Default",
                    objectIds = bakedIds
                });
            });
        }

        private static GH_Group FindGroup(GH_Document doc, string group, HashSet<Guid> infraIds)
        {
            if (Guid.TryParse(group, out Guid groupGuid))
            {
                if (infraIds.Contains(groupGuid)) return null;
                return doc.FindObject(groupGuid, true) as GH_Group;
            }

            var searchLower = group.ToLowerInvariant();
            foreach (var obj in doc.Objects)
            {
                if (obj is GH_Group g && !infraIds.Contains(g.InstanceGuid))
                {
                    var groupName = g.NickName?.ToLowerInvariant() ?? g.Name?.ToLowerInvariant() ?? "";
                    if (groupName == searchLower || groupName.Contains(searchLower))
                        return g;
                }
            }
            return null;
        }
    }
}
