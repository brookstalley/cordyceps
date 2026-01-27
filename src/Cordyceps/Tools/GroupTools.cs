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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                var group = new GH_Group();
                group.NickName = name;
                group.Name = name;

                // Set color if specified
                if (!string.IsNullOrEmpty(color))
                {
                    try
                    {
                        Color groupColor;
                        if (color.StartsWith("#"))
                        {
                            groupColor = ColorTranslator.FromHtml(color);
                        }
                        else
                        {
                            groupColor = Color.FromName(color);
                        }
                        group.Colour = groupColor;
                    }
                    catch { }
                }

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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                // Parse component IDs
                List<Guid> guids;
                try
                {
                    var idsArray = JsonConvert.DeserializeObject<string[]>(componentIds);
                    guids = idsArray.Select(s => Guid.Parse(s)).ToList();
                }
                catch
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid component IDs format" });
                }

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
                if (!string.IsNullOrEmpty(color))
                {
                    try
                    {
                        Color parsedColor;
                        if (color.StartsWith("#"))
                        {
                            parsedColor = ColorTranslator.FromHtml(color);
                        }
                        else
                        {
                            parsedColor = Color.FromName(color);
                        }
                        group.Colour = parsedColor;
                    }
                    catch
                    {
                        // Ignore invalid color
                    }
                }

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
                            warnings.Add($"Group '{group.NickName}' overlaps with group '{otherGroup.NickName}' - consider moving components");
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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(id, out Guid guid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid group ID" });
                }

                var group = doc.FindObject(guid, true) as GH_Group;
                if (group == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Group not found: {id}" });
                }

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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(id, out Guid guid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid group ID" });
                }

                var group = doc.FindObject(guid, true) as GH_Group;
                if (group == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Group not found: {id}" });
                }

                try
                {
                    Color groupColor;
                    if (color.StartsWith("#"))
                    {
                        groupColor = ColorTranslator.FromHtml(color);
                    }
                    else
                    {
                        groupColor = Color.FromName(color);
                    }
                    group.Colour = groupColor;
                    Instances.ActiveCanvas?.Invalidate();
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Invalid color: {ex.Message}" });
                }

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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(groupId, out Guid gGuid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid group ID" });
                }

                var group = doc.FindObject(gGuid, true) as GH_Group;
                if (group == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Group not found: {groupId}" });
                }

                // Parse component IDs
                List<Guid> guids;
                try
                {
                    var idsArray = JsonConvert.DeserializeObject<string[]>(componentIds);
                    guids = idsArray.Select(s => Guid.Parse(s)).ToList();
                }
                catch
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid component IDs format" });
                }

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
                    groupName = group.NickName,
                    removedCount,
                    removedIds
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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(id, out Guid guid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid group ID" });
                }

                var group = doc.FindObject(guid, true) as GH_Group;
                if (group == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Group not found: {id}" });
                }

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

        [McpServerTool, Description("Get all visual groups on the canvas")]
        public string GetAllGroups()
        {
            _server?.RecordCommand("get_all_groups");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                var groups = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (obj is GH_Group group)
                    {
                        var memberIds = group.ObjectIDs?.Select(g => g.ToString()).ToList() ?? new List<string>();

                        groups.Add(new
                        {
                            id = group.InstanceGuid.ToString(),
                            name = group.NickName,
                            color = ColorTranslator.ToHtml(group.Colour),
                            memberCount = memberIds.Count,
                            memberIds
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
