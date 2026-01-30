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

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified group tool - visual group operations (create, delete, add, remove, rename, color, move, list)
    /// </summary>
    [McpServerToolType]
    public class GhGroupTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_group",
            Description = "Visual group operations for organizing components on the canvas",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["create"] = new ActionInfo
                {
                    Name = "create",
                    Description = "Create a new visual group",
                    Required = new[] { "name" },
                    Optional = new[] { "ids", "color" },
                    Example = "action='create', name='Inputs', ids='[\"a\",\"b\"]', color='#FF6B6B'",
                    Tips = new[] { "Use 'ids' to add components when creating the group" }
                },
                ["delete"] = new ActionInfo
                {
                    Name = "delete",
                    Description = "Delete a visual group (components remain)",
                    Required = new[] { "id" },
                    Example = "action='delete', id='abc-123'"
                },
                ["add"] = new ActionInfo
                {
                    Name = "add",
                    Description = "Add components to a group",
                    Required = new[] { "ids" },
                    Optional = new[] { "id", "name", "color" },
                    Example = "action='add', ids='[\"a\",\"b\"]', name='MyGroup'",
                    Tips = new[] { "Provide 'id' for existing group, or 'name' to create new" }
                },
                ["remove"] = new ActionInfo
                {
                    Name = "remove",
                    Description = "Remove components from a group",
                    Required = new[] { "id", "ids" },
                    Example = "action='remove', id='group-id', ids='[\"comp-a\"]'"
                },
                ["rename"] = new ActionInfo
                {
                    Name = "rename",
                    Description = "Rename a group",
                    Required = new[] { "id", "name" },
                    Example = "action='rename', id='abc-123', name='NewName'"
                },
                ["color"] = new ActionInfo
                {
                    Name = "color",
                    Description = "Set group color",
                    Required = new[] { "id", "color" },
                    Example = "action='color', id='abc', color='#4ECDC4'",
                    Tips = new[] { "Use hex (#RRGGBB) or named colors (Red, Blue, etc.)" }
                },
                ["move"] = new ActionInfo
                {
                    Name = "move",
                    Description = "Move all components in a group by offset",
                    Required = new[] { "id", "dx", "dy" },
                    Example = "action='move', id='abc', dx=100, dy=50"
                },
                ["list"] = new ActionInfo
                {
                    Name = "list",
                    Description = "List all groups on canvas",
                    Example = "action='list'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Groups are visual containers - deleting a group doesn't delete its components",
                "Color accepts hex (#FF0000) or named colors (Red, LightBlue, etc.)"
            }
        };

        public GhGroupTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Group operations. Actions: create|delete|add|remove|rename|color|move|list|help")]
        public string GhGroup(
            [Description("Action to perform")] string action,
            [Description("Group GUID")] string id = null,
            [Description("JSON array of component IDs")] string ids = null,
            [Description("Group name")] string name = null,
            [Description("Group color (hex or name)")] string color = null,
            [Description("X offset for move")] double dx = 0,
            [Description("Y offset for move")] double dy = 0)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            // For move action, we need to track if dx/dy were explicitly provided
            // Since they have defaults, we check if action is 'move' and include them
            bool isMoveAction = string.Equals(action, "move", StringComparison.OrdinalIgnoreCase);
            var providedParams = UnifiedToolHelpers.BuildParams(
                ("id", id),
                ("ids", ids),
                ("name", name),
                ("color", color),
                ("dx", isMoveAction ? (object)dx : null),
                ("dy", isMoveAction ? (object)dy : null)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "create" => ActionCreate(name, ids, color),
                "delete" => ActionDelete(id),
                "add" => ActionAdd(id, ids, name, color),
                "remove" => ActionRemove(id, ids),
                "rename" => ActionRename(id, name),
                "color" => ActionColor(id, color),
                "move" => ActionMove(id, dx, dy),
                "list" => ActionList(),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionCreate(string name, string componentIds, string color)
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

                // Add members if ids provided
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

        private string ActionDelete(string id)
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

        private string ActionAdd(string groupId, string componentIds, string groupName, string color)
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

        private string ActionRemove(string groupId, string componentIds)
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

        private string ActionRename(string id, string newName)
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

        private string ActionColor(string id, string color)
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

        private string ActionMove(string groupSpec, double dx, double dy)
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

        private string ActionList()
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
    }
}
