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
    /// Unified canvas tool - component operations, value adjustments, and group management
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
                ["zoom"] = new ActionInfo
                {
                    Name = "zoom",
                    Description = "Zoom canvas to fit all components or a specific component",
                    Optional = new[] { "id", "padding" },
                    Example = "action='zoom' OR action='zoom', id='abc-123', padding=100",
                    Tips = new[] { "No id = zoom to fit all components", "padding adds margin around content (default 50)" }
                },
                ["view"] = new ActionInfo
                {
                    Name = "view",
                    Description = "Get or set canvas view (center point and zoom level)",
                    Optional = new[] { "centerX", "centerY", "zoom" },
                    Example = "action='view' OR action='view', centerX=300, centerY=200, zoom=1.0",
                    Tips = new[] { "No params = get current view", "zoom: 1.0=100%, 0.5=50%, 2.0=200%" }
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                },
                // Value adjustment actions (from gh_adjust)
                ["get"] = new ActionInfo
                {
                    Name = "get",
                    Description = "Read current value(s) from a component",
                    Required = new[] { "id" },
                    Example = "action='get', id='abc-123'"
                },
                ["set"] = new ActionInfo
                {
                    Name = "set",
                    Description = "Set value on a component (slider, panel, toggle)",
                    Required = new[] { "id", "value" },
                    Example = "action='set', id='abc-123', value='42'",
                    Tips = new[] { "For sliders, value must be within range", "For toggles, use 'true' or 'false'" }
                },
                ["config"] = new ActionInfo
                {
                    Name = "config",
                    Description = "Configure input component constraints",
                    Required = new[] { "id" },
                    Optional = new[] { "min", "max", "value", "decimals", "items" },
                    Example = "action='config', id='abc', min=0, max=100, value=50",
                    Tips = new[] { "For value lists: items='[{\"name\":\"A\",\"value\":\"0\"}]'" }
                },
                ["preview"] = new ActionInfo
                {
                    Name = "preview",
                    Description = "Set component preview visibility",
                    Required = new[] { "enabled" },
                    Optional = new[] { "id", "ids" },
                    Example = "action='preview', id='abc', enabled=false"
                },
                ["enable"] = new ActionInfo
                {
                    Name = "enable",
                    Description = "Enable or disable component computation",
                    Required = new[] { "enabled" },
                    Optional = new[] { "id", "ids" },
                    Example = "action='enable', id='abc', enabled=false"
                },
                // Group actions (from gh_group)
                ["group_create"] = new ActionInfo
                {
                    Name = "group_create",
                    Description = "Create a visual group",
                    Required = new[] { "name" },
                    Optional = new[] { "ids", "color" },
                    Example = "action='group_create', name='Inputs', ids='[\"a\",\"b\"]', color='#FF6B6B'"
                },
                ["group_delete"] = new ActionInfo
                {
                    Name = "group_delete",
                    Description = "Delete a visual group (components remain)",
                    Required = new[] { "id" },
                    Example = "action='group_delete', id='abc-123'"
                },
                ["group_add"] = new ActionInfo
                {
                    Name = "group_add",
                    Description = "Add components to a group",
                    Required = new[] { "ids" },
                    Optional = new[] { "id", "name", "color" },
                    Example = "action='group_add', ids='[\"a\",\"b\"]', name='MyGroup'"
                },
                ["group_remove"] = new ActionInfo
                {
                    Name = "group_remove",
                    Description = "Remove components from a group",
                    Required = new[] { "id", "ids" },
                    Example = "action='group_remove', id='group-id', ids='[\"comp-a\"]'"
                },
                ["group_list"] = new ActionInfo
                {
                    Name = "group_list",
                    Description = "List all groups on canvas",
                    Example = "action='group_list'"
                },
                ["group_rename"] = new ActionInfo
                {
                    Name = "group_rename",
                    Description = "Rename a group",
                    Required = new[] { "id", "name" },
                    Example = "action='group_rename', id='abc-123', name='NewName'"
                },
                ["group_color"] = new ActionInfo
                {
                    Name = "group_color",
                    Description = "Set group color",
                    Required = new[] { "id", "color" },
                    Example = "action='group_color', id='abc', color='#4ECDC4'"
                },
                ["group_move"] = new ActionInfo
                {
                    Name = "group_move",
                    Description = "Move all components in a group by offset",
                    Required = new[] { "id", "dx", "dy" },
                    Example = "action='group_move', id='abc', dx=100, dy=50"
                }
            },
            Notes = new[]
            {
                "Disable solver before bulk operations: gh_document(action='solver', enabled=false)",
                "Recommended spacing: 150px horizontal, 70px vertical between components",
                "Use action='zoom' before capturing to ensure all components are visible",
                "Groups are visual containers - deleting a group doesn't delete its components"
            }
        };

        public GhCanvasTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Component operations. Actions: add|delete|move|rename|find|search|list|info|bounds|validate|constant|bake|zoom|view|get|set|config|preview|enable|group_create|group_delete|group_add|group_remove|group_list|group_rename|group_color|group_move|help")]
        public string GhCanvas(
            [Description("Action to perform")] string action,
            [Description("Component type for 'add', or search query for 'search'")] string type = null,
            [Description("X position")] double x = double.NaN,
            [Description("Y position")] double y = double.NaN,
            [Description("Component GUID")] string id = null,
            [Description("JSON array of IDs")] string ids = null,
            [Description("JSON array of moves: [{id,x,y},...]")] string moves = null,
            [Description("Nickname for rename/find/add")] string nickname = null,
            [Description("Search query")] string query = null,
            [Description("Category filter")] string category = null,
            [Description("Type filter for list")] string typeFilter = null,
            [Description("Group filter for list")] string group = null,
            [Description("Result limit for search")] int limit = 50,
            [Description("Exact match for find")] bool exact = false,
            [Description("Value to set")] string value = null,
            [Description("Layer name for bake")] string layer = null,
            [Description("Object name for bake/group")] string name = null,
            [Description("Padding for zoom (default 50)")] int padding = 50,
            [Description("Center X for view")] double centerX = double.NaN,
            [Description("Center Y for view")] double centerY = double.NaN,
            [Description("Zoom level for view (1.0=100%)")] double zoom = double.NaN,
            // Value adjustment params
            [Description("Minimum value for slider")] double min = double.NaN,
            [Description("Maximum value for slider")] double max = double.NaN,
            [Description("Decimal places for slider")] int decimals = -1,
            [Description("Items for value list (JSON)")] string items = null,
            [Description("Preview/enable state")] string enabled = null,
            // Group params
            [Description("Color (hex or name)")] string color = null,
            [Description("X offset for group move")] double dx = double.NaN,
            [Description("Y offset for group move")] double dy = double.NaN)
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
                ("name", name),
                ("padding", padding != 50 ? (object)padding : null),
                ("centerX", double.IsNaN(centerX) ? null : (object)centerX),
                ("centerY", double.IsNaN(centerY) ? null : (object)centerY),
                ("zoom", double.IsNaN(zoom) ? null : (object)zoom),
                // Value adjustment params
                ("min", double.IsNaN(min) ? null : (object)min),
                ("max", double.IsNaN(max) ? null : (object)max),
                ("decimals", decimals < 0 ? null : (object)decimals),
                ("items", items),
                ("enabled", enabled),
                // Group params
                ("color", color),
                ("dx", double.IsNaN(dx) ? null : (object)dx),
                ("dy", double.IsNaN(dy) ? null : (object)dy)
            );

            // Validate action and required params
            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse enabled - default to true if not specified (for preview/enable actions)
            bool enabledBool = string.IsNullOrEmpty(enabled) || ToolHelpers.ParseBool(enabled, true);

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
                "zoom" => ActionZoom(id, padding),
                "view" => ActionView(centerX, centerY, zoom),
                // Value adjustment actions
                "get" => ActionGet(id),
                "set" => ActionSet(id, value),
                "config" => ActionConfig(id, min, max, value, decimals, items),
                "preview" => ActionPreview(id, ids, enabledBool),
                "enable" => ActionEnable(id, ids, enabledBool),
                // Group actions
                "group_create" => ActionGroupCreate(name, ids, color),
                "group_delete" => ActionGroupDelete(id),
                "group_add" => ActionGroupAdd(id, ids, name, color),
                "group_remove" => ActionGroupRemove(id, ids),
                "group_list" => ActionGroupList(),
                "group_rename" => ActionGroupRename(id, name),
                "group_color" => ActionGroupColor(id, color),
                "group_move" => ActionGroupMove(id, dx, dy),
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
                            message = $"Multiple components match '{type}'. Use GUID or 'Category/Name' format. Prefer non-deprecated components.",
                            matchCount = matches.Count,
                            matches = matches.Select(m => new
                            {
                                name = m.Name,
                                guid = m.Guid,
                                category = m.Category,
                                subcategory = m.SubCategory,
                                role = m.Role,
                                deprecated = m.Deprecated,
                                upgradeTo = m.UpgradeTo,
                                upgradeToGuid = m.UpgradeToGuid
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
                    guid = r.Guid,
                    deprecated = r.Deprecated,
                    upgradeTo = r.UpgradeTo,
                    upgradeToGuid = r.UpgradeToGuid
                }).ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = enhanced.Count,
                    totalMatches = results.Count,
                    note = "Results are sorted with non-deprecated components first. Prefer components where deprecated=false.",
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

        private string ActionZoom(string id, int padding)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var canvas = Instances.ActiveCanvas;
                if (canvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

                var doc = canvas.Document;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper document");

                RectangleF bounds;

                if (!string.IsNullOrEmpty(id))
                {
                    // Zoom to specific component
                    if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    bounds = component.Attributes.Bounds;
                    bounds.Inflate(padding, padding);
                }
                else
                {
                    // Zoom to fit all components
                    var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;
                    bool hasContent = false;

                    foreach (var obj in doc.Objects)
                    {
                        if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds)) continue;
                        hasContent = true;
                        var b = obj.Attributes.Bounds;
                        minX = Math.Min(minX, b.Left);
                        minY = Math.Min(minY, b.Top);
                        maxX = Math.Max(maxX, b.Right);
                        maxY = Math.Max(maxY, b.Bottom);
                    }

                    if (!hasContent)
                        return ToolHelpers.ErrorResponse("No components on canvas to zoom to");

                    bounds = new RectangleF(minX - padding, minY - padding,
                                           (maxX - minX) + padding * 2,
                                           (maxY - minY) + padding * 2);
                }

                // Calculate center and zoom to fit bounds in viewport
                var center = new PointF(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                var viewportSize = canvas.Viewport.VisibleRegion;
                float zoomX = viewportSize.Width / bounds.Width;
                float zoomY = viewportSize.Height / bounds.Height;
                float newZoom = Math.Min(zoomX, zoomY) * 0.9f; // 90% to leave margin

                // Clamp zoom to reasonable range
                newZoom = Math.Max(0.1f, Math.Min(5f, newZoom));

                canvas.Viewport.MidPoint = center;
                canvas.Viewport.Zoom = newZoom;
                canvas.Refresh();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    centerX = center.X,
                    centerY = center.Y,
                    zoom = newZoom,
                    bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
                });
            });
        }

        private string ActionView(double centerX, double centerY, double zoom)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var canvas = Instances.ActiveCanvas;
                if (canvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

                // If no parameters provided, return current view state
                if (double.IsNaN(centerX) && double.IsNaN(centerY) && double.IsNaN(zoom))
                {
                    var currentMid = canvas.Viewport.MidPoint;
                    var currentZoom = canvas.Viewport.Zoom;
                    var visible = canvas.Viewport.VisibleRegion;

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        centerX = currentMid.X,
                        centerY = currentMid.Y,
                        zoom = currentZoom,
                        visibleRegion = new { x = visible.X, y = visible.Y, width = visible.Width, height = visible.Height }
                    });
                }

                // Set view parameters
                var newCenter = canvas.Viewport.MidPoint;
                if (!double.IsNaN(centerX)) newCenter.X = (float)centerX;
                if (!double.IsNaN(centerY)) newCenter.Y = (float)centerY;
                canvas.Viewport.MidPoint = newCenter;

                if (!double.IsNaN(zoom))
                {
                    // Clamp zoom to reasonable range
                    var newZoom = Math.Max(0.1, Math.Min(5.0, zoom));
                    canvas.Viewport.Zoom = (float)newZoom;
                }

                canvas.Refresh();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    centerX = canvas.Viewport.MidPoint.X,
                    centerY = canvas.Viewport.MidPoint.Y,
                    zoom = canvas.Viewport.Zoom
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

        #region Value Adjustment Actions (from gh_adjust)

        private string ActionGet(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                object currentValue = null;
                string valueType = "unknown";

                if (component is GH_NumberSlider slider)
                {
                    currentValue = slider.CurrentValue;
                    valueType = "slider";
                }
                else if (component is GH_Panel panel)
                {
                    currentValue = panel.UserText;
                    valueType = "panel";
                }
                else if (component is GH_BooleanToggle toggle)
                {
                    currentValue = toggle.Value;
                    valueType = "toggle";
                }
                else if (component is GH_ValueList valueList)
                {
                    var selected = valueList.SelectedItems.FirstOrDefault();
                    currentValue = selected?.Name ?? "(none)";
                    valueType = "value_list";
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id,
                    name = component.Name,
                    nickname = component.NickName,
                    valueType,
                    value = currentValue
                });
            });
        }

        private string ActionSet(string id, string value)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (component is GH_Panel panel)
                {
                    panel.UserText = value;
                }
                else if (component is GH_NumberSlider slider)
                {
                    if (!double.TryParse(value, out double numValue))
                        return ToolHelpers.ErrorResponse($"Invalid number: {value}");

                    if (numValue < (double)slider.Slider.Minimum || numValue > (double)slider.Slider.Maximum)
                        return ToolHelpers.ErrorResponse($"Value {numValue} out of range [{slider.Slider.Minimum}, {slider.Slider.Maximum}]");

                    slider.SetSliderValue((decimal)numValue);
                }
                else if (component is GH_BooleanToggle toggle)
                {
                    toggle.Value = value.ToLower() == "true" || value == "1";
                }
                else if (component is GH_ValueList valueList)
                {
                    var item = valueList.ListItems.FirstOrDefault(i =>
                        i.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                        i.Expression.Equals(value, StringComparison.OrdinalIgnoreCase));

                    if (item == null)
                        return ToolHelpers.ErrorResponse($"Value '{value}' not found in list. Available: {string.Join(", ", valueList.ListItems.Select(i => i.Name))}");

                    valueList.SelectItem(valueList.ListItems.IndexOf(item));
                }
                else
                {
                    return ToolHelpers.ErrorResponse($"Cannot set value on {component.GetType().Name}");
                }

                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id,
                    type = component.Name,
                    value
                });
            });
        }

        private string ActionConfig(string id, double min, double max, string value, int decimals, string items)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (component is GH_NumberSlider slider)
                {
                    if (!double.IsNaN(min)) slider.Slider.Minimum = (decimal)min;
                    if (!double.IsNaN(max)) slider.Slider.Maximum = (decimal)max;
                    if (decimals >= 0) slider.Slider.DecimalPlaces = decimals;
                    if (!string.IsNullOrEmpty(value) && double.TryParse(value, out double val))
                        slider.SetSliderValue((decimal)val);

                    doc.NewSolution(true);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id,
                        type = "slider",
                        min = (double)slider.Slider.Minimum,
                        max = (double)slider.Slider.Maximum,
                        value = (double)slider.CurrentValue,
                        decimals = slider.Slider.DecimalPlaces
                    });
                }
                else if (component is GH_ValueList valueList && !string.IsNullOrEmpty(items))
                {
                    try
                    {
                        var itemList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(items);
                        valueList.ListItems.Clear();

                        foreach (var item in itemList)
                        {
                            var itemName = item.ContainsKey("name") ? item["name"] : "Item";
                            var expr = item.ContainsKey("value") ? item["value"] : "0";
                            valueList.ListItems.Add(new GH_ValueListItem(itemName, expr));
                        }

                        if (valueList.ListItems.Count > 0)
                            valueList.SelectItem(0);

                        doc.NewSolution(true);

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            id,
                            type = "value_list",
                            itemCount = valueList.ListItems.Count,
                            items = valueList.ListItems.Select(i => new { name = i.Name, value = i.Expression }).ToList()
                        });
                    }
                    catch (Exception ex)
                    {
                        return ToolHelpers.ErrorResponse($"Failed to parse items: {ex.Message}");
                    }
                }
                else
                {
                    return ToolHelpers.ErrorResponse($"Cannot configure {component.GetType().Name}. Use config for sliders and value lists.");
                }
            });
        }

        private string ActionPreview(string id, string ids, bool enabled)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<string> idList = BuildIdList(id, ids, out error);
                if (idList == null) return ToolHelpers.ErrorResponse(error);

                int changed = 0;
                foreach (var compId in idList)
                {
                    if (ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var comp, out _))
                    {
                        if (comp is IGH_PreviewObject preview)
                        {
                            preview.Hidden = !enabled;
                            changed++;
                        }
                    }
                }

                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    previewEnabled = enabled,
                    changedCount = changed
                });
            });
        }

        private string ActionEnable(string id, string ids, bool enabled)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<string> idList = BuildIdList(id, ids, out error);
                if (idList == null) return ToolHelpers.ErrorResponse(error);

                int changed = 0;
                foreach (var compId in idList)
                {
                    if (ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var comp, out _))
                    {
                        if (comp is IGH_ActiveObject active)
                        {
                            active.Locked = !enabled;
                            changed++;
                        }
                    }
                }

                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled,
                    changedCount = changed
                });
            });
        }

        private List<string> BuildIdList(string id, string ids, out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(ids))
            {
                try { return JsonConvert.DeserializeObject<List<string>>(ids); }
                catch { error = "Invalid ids JSON array"; return null; }
            }
            if (!string.IsNullOrEmpty(id))
                return new List<string> { id };

            error = "Either 'id' or 'ids' is required";
            return null;
        }

        #endregion

        #region Group Actions (from gh_group)

        private string ActionGroupCreate(string name, string componentIds, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var group = new GH_Group();
                group.NickName = name;
                group.Name = name;

                if (ToolHelpers.TryParseColor(color, out var groupColor))
                    group.Colour = groupColor;

                doc.AddObject(group, false);

                var addedIds = new List<string>();
                if (!string.IsNullOrEmpty(componentIds))
                {
                    if (ToolHelpers.TryParseGuidArray(componentIds, out var guids, out _))
                    {
                        foreach (var guid in guids)
                        {
                            var obj = doc.FindObject(guid, true);
                            if (obj != null)
                            {
                                group.AddObject(obj.InstanceGuid);
                                addedIds.Add(guid.ToString());
                            }
                        }
                        group.ExpireCaches();
                    }
                }

                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = group.InstanceGuid.ToString(),
                    name = group.NickName,
                    color = ColorTranslator.ToHtml(group.Colour),
                    memberCount = addedIds.Count,
                    memberIds = addedIds
                });
            });
        }

        private string ActionGroupDelete(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(obj is GH_Group group))
                    return ToolHelpers.ErrorResponse($"Object is not a group: {id}");

                doc.RemoveObject(group, true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    deleted = id
                });
            });
        }

        private string ActionGroupAdd(string groupId, string componentIds, string groupName, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!ToolHelpers.TryParseGuidArray(componentIds, out var guids, out error))
                    return ToolHelpers.ErrorResponse(error);

                var objects = new List<IGH_DocumentObject>();
                foreach (var guid in guids)
                {
                    var obj = doc.FindObject(guid, true);
                    if (obj != null)
                        objects.Add(obj);
                }

                if (objects.Count == 0)
                    return ToolHelpers.ErrorResponse("No valid components found");

                GH_Group group = null;
                if (!string.IsNullOrEmpty(groupId) && Guid.TryParse(groupId, out Guid gGuid))
                    group = doc.FindObject(gGuid, true) as GH_Group;

                if (group == null)
                {
                    group = new GH_Group();
                    group.NickName = groupName ?? "Group";
                    group.Name = groupName ?? "Group";
                    doc.AddObject(group, false);
                }

                if (ToolHelpers.TryParseColor(color, out var parsedColor))
                    group.Colour = parsedColor;

                foreach (var obj in objects)
                    group.AddObject(obj.InstanceGuid);

                group.ExpireCaches();
                Instances.ActiveCanvas?.Invalidate();

                var bounds = group.Attributes?.Bounds ?? RectangleF.Empty;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    groupId = group.InstanceGuid.ToString(),
                    groupName = group.NickName,
                    addedCount = objects.Count,
                    addedIds = objects.Select(o => o.InstanceGuid.ToString()).ToList(),
                    bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
                });
            });
        }

        private string ActionGroupRemove(string groupId, string componentIds)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetComponent(_context, groupId, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(obj is GH_Group group))
                    return ToolHelpers.ErrorResponse($"Object is not a group: {groupId}");

                if (!ToolHelpers.TryParseGuidArray(componentIds, out var guids, out error))
                    return ToolHelpers.ErrorResponse(error);

                int removedCount = 0;
                var removedIds = new List<string>();

                foreach (var guid in guids)
                {
                    if (group.ObjectIDs != null && group.ObjectIDs.Contains(guid))
                    {
                        group.RemoveObject(guid);
                        removedCount++;
                        removedIds.Add(guid.ToString());
                    }
                }

                group.ExpireCaches();
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    groupId = group.InstanceGuid.ToString(),
                    removedCount,
                    removedIds
                });
            });
        }

        private string ActionGroupList()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var groups = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (obj is GH_Group group && !infraIds.Contains(group.InstanceGuid))
                    {
                        var memberIds = group.ObjectIDs?
                            .Where(g => !infraIds.Contains(g))
                            .Select(g => g.ToString())
                            .ToList() ?? new List<string>();

                        var bounds = group.Attributes?.Bounds ?? RectangleF.Empty;

                        groups.Add(new
                        {
                            id = group.InstanceGuid.ToString(),
                            name = group.NickName,
                            color = ColorTranslator.ToHtml(group.Colour),
                            memberCount = memberIds.Count,
                            memberIds,
                            bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
                        });
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = groups.Count,
                    groups
                });
            });
        }

        private string ActionGroupRename(string id, string newName)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(obj is GH_Group group))
                    return ToolHelpers.ErrorResponse($"Object is not a group: {id}");

                string oldName = group.NickName;
                group.NickName = newName;
                group.Name = newName;
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = group.InstanceGuid.ToString(),
                    oldName,
                    name = group.NickName
                });
            });
        }

        private string ActionGroupColor(string id, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(obj is GH_Group group))
                    return ToolHelpers.ErrorResponse($"Object is not a group: {id}");

                if (!ToolHelpers.TryParseColor(color, out var groupColor))
                    return ToolHelpers.ErrorResponse($"Invalid color: {color}");

                group.Colour = groupColor;
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id,
                    color = ColorTranslator.ToHtml(group.Colour)
                });
            });
        }

        private string ActionGroupMove(string groupSpec, double dx, double dy)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (string.IsNullOrEmpty(groupSpec))
                    return ToolHelpers.ErrorResponse("Group id is required");

                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                GH_Group targetGroup = null;
                if (Guid.TryParse(groupSpec, out Guid groupGuid))
                {
                    if (infraIds.Contains(groupGuid))
                        return ToolHelpers.ErrorResponse($"Protected: required for MCP server. Cannot modify: {groupSpec}");
                    targetGroup = doc.FindObject(groupGuid, true) as GH_Group;
                }

                if (targetGroup == null)
                {
                    foreach (var obj in doc.Objects)
                    {
                        if (obj is GH_Group g && !infraIds.Contains(g.InstanceGuid) &&
                            (g.NickName.Equals(groupSpec, StringComparison.OrdinalIgnoreCase) ||
                             g.Name.Equals(groupSpec, StringComparison.OrdinalIgnoreCase)))
                        {
                            targetGroup = g;
                            break;
                        }
                    }
                }

                if (targetGroup == null)
                    return ToolHelpers.ErrorResponse($"Group not found: {groupSpec}");

                var memberIds = targetGroup.ObjectIDs?.ToList() ?? new List<Guid>();
                if (memberIds.Count == 0)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        groupId = targetGroup.InstanceGuid.ToString(),
                        movedCount = 0,
                        message = "Group has no members"
                    });
                }

                int movedCount = 0;
                foreach (var memberId in memberIds)
                {
                    var member = doc.FindObject(memberId, true);
                    if (member?.Attributes != null)
                    {
                        var pivot = member.Attributes.Pivot;
                        member.Attributes.Pivot = new PointF(pivot.X + (float)dx, pivot.Y + (float)dy);
                        member.Attributes.ExpireLayout();
                        movedCount++;
                    }
                }

                targetGroup.ExpireCaches();
                Instances.ActiveCanvas?.Invalidate();

                var bounds = targetGroup.Attributes?.Bounds ?? RectangleF.Empty;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    groupId = targetGroup.InstanceGuid.ToString(),
                    movedCount,
                    offset = new { dx, dy },
                    bounds = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
                });
            });
        }

        #endregion
    }
}
