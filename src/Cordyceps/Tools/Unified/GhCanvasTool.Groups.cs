using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Newtonsoft.Json;

namespace Cordyceps.Tools.Unified
{
    public partial class GhCanvasTool
    {
        #region Group Actions

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
                            (g.NickName?.Equals(groupSpec, StringComparison.OrdinalIgnoreCase) == true ||
                             g.Name?.Equals(groupSpec, StringComparison.OrdinalIgnoreCase) == true))
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
