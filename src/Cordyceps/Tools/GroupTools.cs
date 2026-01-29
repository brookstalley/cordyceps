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

namespace Cordyceps.Tools
{
    /// <summary>
    /// Visual group operations
    /// </summary>
    [McpServerToolType]
    public class GroupTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public GroupTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Create a visual group on the Grasshopper canvas")]
        public string CreateGroup(
            [Description("Name/label for the group")] string name,
            [Description("Color as hex (#FF0000) or name (Red)")] string color = null)
        {
            _server?.RecordCommand("create_group");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var group = new GH_Group();
                group.NickName = name;
                group.Name = name;

                // Set color if specified
                if (ToolHelpers.TryParseColor(color, out var groupColor))
                    group.Colour = groupColor;

                doc.AddObject(group, false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = group.InstanceGuid.ToString(),
                    name = group.NickName,
                    color = ColorTranslator.ToHtml(group.Colour)
                });
            });
        }

        [McpServerTool, Description("Add components to a visual group")]
        public string AddToGroup(
            [Description("JSON array of component GUIDs to add")] string componentIds,
            [Description("Existing group GUID (optional)")] string groupId = null,
            [Description("Name for new group if groupId not provided")] string groupName = null,
            [Description("Color for the group (hex like '#FF0000' or name like 'Red')")] string color = null)
        {
            _server?.RecordCommand("add_to_group");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Parse component IDs
                if (!ToolHelpers.TryParseGuidArray(componentIds, out var guids, out error))
                    return ToolHelpers.ErrorResponse(error);

                // Find the objects
                var objects = new List<IGH_DocumentObject>();
                foreach (var guid in guids)
                {
                    var obj = doc.FindObject(guid, true);
                    if (obj != null)
                    {
                        objects.Add(obj);
                    }
                }

                if (objects.Count == 0)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No valid components found" });
                }

                // Find or create group
                GH_Group group = null;

                if (!string.IsNullOrEmpty(groupId))
                {
                    if (Guid.TryParse(groupId, out Guid gGuid))
                    {
                        group = doc.FindObject(gGuid, true) as GH_Group;
                    }
                }

                if (group == null)
                {
                    // Create new group
                    group = new GH_Group();
                    group.NickName = groupName ?? "Group";
                    group.Name = groupName ?? "Group";
                    doc.AddObject(group, false);
                }

                // Set color if provided
                if (ToolHelpers.TryParseColor(color, out var parsedColor))
                    group.Colour = parsedColor;

                // Add objects to group
                foreach (var obj in objects)
                {
                    group.AddObject(obj.InstanceGuid);
                }

                group.ExpireCaches();
                Instances.ActiveCanvas?.Invalidate();

                // Get group bounds and check for layout warnings
                var groupBounds = group.Attributes?.Bounds ?? RectangleF.Empty;
                var warnings = new List<string>();

                // Check if this group overlaps with other groups
                foreach (var otherObj in doc.Objects)
                {
                    if (otherObj is GH_Group otherGroup && otherGroup.InstanceGuid != group.InstanceGuid)
                    {
                        var otherBounds = otherGroup.Attributes?.Bounds ?? RectangleF.Empty;
                        if (!groupBounds.IsEmpty && !otherBounds.IsEmpty && groupBounds.IntersectsWith(otherBounds))
                        {
                            warnings.Add($"Group '{ToolHelpers.GetDisplayName(group)}' overlaps with group '{ToolHelpers.GetDisplayName(otherGroup)}' - consider moving components");
                        }
                    }
                }

                // Check if group is very wide (might indicate horizontal spacing issues)
                if (groupBounds.Width > 1000)
                {
                    warnings.Add($"Group is very wide ({groupBounds.Width:F0}px) - consider splitting into multiple groups or spacing components closer");
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    groupId = group.InstanceGuid.ToString(),
                    groupName = group.NickName,
                    addedCount = objects.Count,
                    addedIds = objects.Select(o => o.InstanceGuid.ToString()).ToList(),
                    bounds = new
                    {
                        x = groupBounds.X,
                        y = groupBounds.Y,
                        width = groupBounds.Width,
                        height = groupBounds.Height,
                        right = groupBounds.Right,
                        bottom = groupBounds.Bottom
                    },
                    warnings = warnings.Count > 0 ? warnings : null
                });
            });
        }

        [McpServerTool, Description("Delete a visual group from the canvas")]
        public string DeleteGroup(
            [Description("Group GUID")] string id)
        {
            _server?.RecordCommand("delete_group");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure groups appear as "not found"
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

        [McpServerTool, Description("Set the color of a visual group")]
        public string SetGroupColor(
            [Description("Group GUID")] string id,
            [Description("Color as hex (#FF0000) or name (Red)")] string color)
        {
            _server?.RecordCommand("set_group_color");
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
                    id = id,
                    color = ColorTranslator.ToHtml(group.Colour)
                });
            });
        }

[McpServerTool, Description("Remove components from a visual group")]
        public string RemoveFromGroup(
            [Description("Group GUID")] string groupId,
            [Description("JSON array of component GUIDs to remove")] string componentIds)
        {
            _server?.RecordCommand("remove_from_group");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetComponent(_context, groupId, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(obj is GH_Group group))
                    return ToolHelpers.ErrorResponse($"Object is not a group: {groupId}");

                // Parse component IDs
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

                // Get updated group bounds after removal
                var bounds = group.Attributes?.Bounds ?? RectangleF.Empty;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    groupId = group.InstanceGuid.ToString(),
                    groupName = group.NickName,
                    removedCount,
                    removedIds,
                    bounds = new
                    {
                        x = bounds.X,
                        y = bounds.Y,
                        width = bounds.Width,
                        height = bounds.Height,
                        right = bounds.Right,
                        bottom = bounds.Bottom
                    }
                });
            });
        }

        [McpServerTool, Description("Rename a visual group")]
        public string RenameGroup(
            [Description("Group GUID")] string id,
            [Description("New name for the group")] string newName)
        {
            _server?.RecordCommand("rename_group");
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
                    newName = group.NickName
                });
            });
        }

        [McpServerTool, Description("Move all components in a group by an offset")]
        public string MoveGroup(
            [Description("Group GUID or name")] string group,
            [Description("X offset to move")] double dx,
            [Description("Y offset to move")] double dy)
        {
            _server?.RecordCommand("move_group");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure IDs to protect
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                // Find group by GUID or name
                GH_Group targetGroup = null;
                if (Guid.TryParse(group, out Guid groupGuid))
                {
                    // Check if protected - report as "not found"
                    if (infraIds.Contains(groupGuid))
                        return ToolHelpers.ErrorResponse($"Group not found: {group}");

                    targetGroup = doc.FindObject(groupGuid, true) as GH_Group;
                }

                if (targetGroup == null)
                {
                    // Try to find by name
                    foreach (var obj in doc.Objects)
                    {
                        if (obj is GH_Group g &&
                            (g.NickName.Equals(group, StringComparison.OrdinalIgnoreCase) ||
                             g.Name.Equals(group, StringComparison.OrdinalIgnoreCase)))
                        {
                            // Check if protected - report as "not found"
                            if (infraIds.Contains(g.InstanceGuid))
                                continue;

                            targetGroup = g;
                            break;
                        }
                    }
                }

                if (targetGroup == null)
                    return ToolHelpers.ErrorResponse($"Group not found: {group}");

                var memberIds = targetGroup.ObjectIDs?.ToList() ?? new List<Guid>();
                if (memberIds.Count == 0)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        groupId = targetGroup.InstanceGuid.ToString(),
                        groupName = targetGroup.NickName,
                        movedCount = 0,
                        message = "Group has no members to move"
                    });
                }

                int movedCount = 0;
                foreach (var memberId in memberIds)
                {
                    var member = doc.FindObject(memberId, true);
                    if (member?.Attributes != null)
                    {
                        var currentPivot = member.Attributes.Pivot;
                        member.Attributes.Pivot = new PointF(
                            currentPivot.X + (float)dx,
                            currentPivot.Y + (float)dy);
                        member.Attributes.ExpireLayout();
                        movedCount++;
                    }
                }

                targetGroup.ExpireCaches();
                Instances.ActiveCanvas?.Invalidate();

                // Get updated group bounds
                var newBounds = targetGroup.Attributes?.Bounds ?? RectangleF.Empty;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    groupId = targetGroup.InstanceGuid.ToString(),
                    groupName = targetGroup.NickName,
                    movedCount,
                    offset = new { dx, dy },
                    bounds = new
                    {
                        x = newBounds.X,
                        y = newBounds.Y,
                        width = newBounds.Width,
                        height = newBounds.Height,
                        right = newBounds.Right,
                        bottom = newBounds.Bottom
                    }
                });
            });
        }

        [McpServerTool, Description("Get all visual groups on the canvas")]
        public string GetAllGroups()
        {
            _server?.RecordCommand("get_all_groups");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure IDs to filter (includes infrastructure groups)
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var groups = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (obj is GH_Group group)
                    {
                        // Skip infrastructure groups
                        if (infraIds.Contains(group.InstanceGuid))
                            continue;

                        // Filter infrastructure components from member list
                        var memberIds = group.ObjectIDs?
                            .Where(g => !infraIds.Contains(g))
                            .Select(g => g.ToString())
                            .ToList() ?? new List<string>();

                        var groupBounds = group.Attributes?.Bounds ?? RectangleF.Empty;

                        groups.Add(new
                        {
                            id = group.InstanceGuid.ToString(),
                            name = group.NickName,
                            color = ColorTranslator.ToHtml(group.Colour),
                            memberCount = memberIds.Count,
                            memberIds,
                            bounds = new
                            {
                                x = groupBounds.X,
                                y = groupBounds.Y,
                                width = groupBounds.Width,
                                height = groupBounds.Height,
                                right = groupBounds.Right,
                                bottom = groupBounds.Bottom
                            }
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
