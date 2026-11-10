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

                // Resolve every member BEFORE creating the group: a member that does not resolve
                // (malformed JSON, unknown, ambiguous, or infrastructure, which reads as not found)
                // is an error and nothing is created. A group half-built from a typo is a
                // mutation the caller did not ask for; grouping the Cordyceps component would
                // also turn the whole user group into hidden infrastructure.
                var lookup = new ObjectLookup(doc);
                var members = new List<IGH_DocumentObject>();
                IReadOnlyList<IdOutcome> outcomes = null;
                if (!string.IsNullOrEmpty(componentIds))
                {
                    if (!ToolHelpers.TryResolveIdArray(lookup, componentIds, "ids", out members, out outcomes, out var resolveError))
                        return ToolHelpers.ErrorResponse(resolveError);
                }

                var group = new GH_Group();
                group.NickName = name;
                group.Name = name;

                if (ToolHelpers.TryParseColor(color, out var groupColor))
                    group.Colour = groupColor;

                doc.AddObject(group, false);

                foreach (var obj in members)
                    group.AddObject(obj.InstanceGuid);
                if (members.Count > 0)
                    group.ExpireCaches();

                Instances.ActiveCanvas?.Invalidate();

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["id"] = group.InstanceGuid.ToString(),
                    ["name"] = group.NickName,
                    ["color"] = ColorTranslator.ToHtml(group.Colour),
                    ["memberCount"] = members.Count,
                    ["memberIds"] = members.Select(o => o.InstanceGuid.ToString()).ToList()
                };
                AddDuplicateNotes(result, outcomes);
                return JsonConvert.SerializeObject(result);
            });
        }

        private string ActionGroupDelete(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedGroup(_context, id, Core.Canvas.ResolveIntent.Destructive, out var doc, out var group, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var deletedId = group.InstanceGuid.ToString();
                doc.RemoveObject(group, true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    deleted = deletedId
                });
            });
        }

        private string ActionGroupAdd(string groupId, string componentIds, string groupName, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Everything resolves before anything changes: the group (when named) and every
                // member. One member that does not resolve fails the call and nothing is added.
                var lookup = new ObjectLookup(doc);

                GH_Group group = null;
                if (!string.IsNullOrEmpty(groupId))
                {
                    // An explicit groupId must resolve — silently creating a new group here
                    // would hide typos and duplicate groups. Omit groupId to create a group.
                    if (!ToolHelpers.TryGetUnprotectedGroup(lookup, groupId, out group, out var groupError))
                        return ToolHelpers.ErrorResponse(
                            $"{groupError}{(groupError.EndsWith(".") || groupError.EndsWith("?") ? " " : ". ")}Omit 'id' (and pass 'name') to create a new group.");
                }

                if (!ToolHelpers.TryResolveIdArray(lookup, componentIds, "ids", out var objects, out var outcomes, out error))
                    return ToolHelpers.ErrorResponse(error);

                if (objects.Count == 0)
                    return ToolHelpers.ErrorResponse("No valid components found");

                if (group != null)
                {
                    var self = outcomes.FirstOrDefault(o => o.Id == group.InstanceGuid);
                    if (self != null)
                        return ToolHelpers.ErrorResponse($"Nothing was changed: ids[{self.Index}] '{self.Text}' is the group itself; a group cannot contain itself");
                }

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

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["groupId"] = group.InstanceGuid.ToString(),
                    ["groupName"] = group.NickName,
                    ["addedCount"] = objects.Count,
                    ["addedIds"] = objects.Select(o => o.InstanceGuid.ToString()).ToList(),
                    ["bounds"] = new { x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }
                };
                AddDuplicateNotes(result, outcomes);
                return JsonConvert.SerializeObject(result);
            });
        }

        private string ActionGroupRemove(string groupId, string componentIds)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // The group and every entry resolve before anything is removed; one entry that
                // does not resolve fails the call and the group is left as it was.
                var lookup = new ObjectLookup(doc);
                if (!ToolHelpers.TryGetUnprotectedGroup(lookup, groupId, out var group, out error))
                    return ToolHelpers.ErrorResponse(error);

                if (!ToolHelpers.TryResolveIdArray(lookup, componentIds, "ids", out var objects, out var outcomes, out error))
                    return ToolHelpers.ErrorResponse(error);

                var memberIds = new HashSet<Guid>(group.ObjectIDs ?? new List<Guid>());
                var removedIds = new List<string>();
                var notMembers = new List<string>();

                foreach (var obj in objects)
                {
                    var guid = obj.InstanceGuid;
                    if (memberIds.Contains(guid))
                    {
                        group.RemoveObject(guid);
                        removedIds.Add(guid.ToString());
                    }
                    else
                    {
                        notMembers.Add(guid.ToString());
                    }
                }

                group.ExpireCaches();
                Instances.ActiveCanvas?.Invalidate();

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["groupId"] = group.InstanceGuid.ToString(),
                    ["removedCount"] = removedIds.Count,
                    ["removedIds"] = removedIds
                };
                if (notMembers.Count > 0) result["notMembers"] = notMembers;
                AddDuplicateNotes(result, outcomes);
                return JsonConvert.SerializeObject(result);
            });
        }

        /// <summary>Adds <c>notes</c> (<c>ids[i]: same object as entry j</c>) when a member array repeated an object.</summary>
        private static void AddDuplicateNotes(Dictionary<string, object> result, IReadOnlyList<IdOutcome> outcomes)
        {
            if (outcomes == null) return;
            var notes = IdBatch.DuplicateNotes(outcomes, "ids");
            if (notes.Count > 0) result["notes"] = notes;
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
                if (!ToolHelpers.TryGetUnprotectedGroup(_context, id, out _, out var group, out var error))
                    return ToolHelpers.ErrorResponse(error);

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
                if (!ToolHelpers.TryGetUnprotectedGroup(_context, id, out _, out var group, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!ToolHelpers.TryParseColor(color, out var groupColor))
                    return ToolHelpers.ErrorResponse($"Invalid color: {color}");

                group.Colour = groupColor;
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = group.InstanceGuid.ToString(),
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

                // Exact name (ordinal), full GUID or 8+ hex GUID prefix; infrastructure is not found.
                if (!ToolHelpers.TryGetUnprotectedGroup(new ObjectLookup(doc), groupSpec, out var targetGroup, out error))
                    return ToolHelpers.ErrorResponse(error);

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
