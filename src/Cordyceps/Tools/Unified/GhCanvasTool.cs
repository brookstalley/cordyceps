using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
using Cordyceps.Core.Naming;
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
    public partial class GhCanvasTool
    {
        private readonly GrasshopperContext _context;

        private const string GroupIdTip = "'id' names the group: its exact name (case-sensitive), its GUID, or an 8+ hex GUID prefix. A name two groups share acts on nothing and lists both ids";
        private const string MembersTip = "Every entry of 'ids' is an object (GUID, 8+ hex GUID prefix, or unique nickname; a group by its id). If any entry does not resolve, the call fails naming it and nothing is changed; a repeated object is acted on once, with a note";

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
                    Optional = new[] { "nickname", "min", "max", "value", "decimals" },
                    Example = "action='add', type='Curve/Circle', x=200, y=100",
                    Tips = new[] {
                        "type must name exactly one installed type: its exact name (case ignored), 'Category/Name', 'Category/Subcategory/Name', 'Name (Category)', or a GUID. Partial names are never matched.",
                        "Many names exist as both a component and a parameter (e.g. 'Circle', 'Line'): plain 'Circle' is ambiguous, so write 'Curve/Circle' (component) or 'Params/Circle' (parameter).",
                        "A deprecated type is used only when it is the only one with that name; the response then carries deprecated=true and a warning naming any replacement.",
                        "Shorthands are exact whole-name aliases: python, c#, slider, plane (XY Plane), filter (Stream Filter), and pt, crv, ln, circ, rect, cube, num, int, bool, str, which create the parameter (ln = 'Params/Line', circ = 'Params/Circle', rect = 'Params/Rectangle'). 'Panel', 'Number Slider', 'C# Script' and 'Stream Filter' always mean the built-in Grasshopper type.",
                        "'Python 3 Script' (and python, 'python script', ghpython) creates Rhino's unified 'Script' component (Maths/Script, GUID c9b2d725-6f87-4b07-af90-bd9aefef68eb), which has no language until its code starts with '#! python 3'. The dedicated Python 3 Script component is 'Maths/Python 3 Script'.",
                        "Nothing is created unless the name resolves. An ambiguous name returns code='ambiguous_name' with every match and a 'use' spelling for each; an unknown name returns code='unknown_type' with 'suggestions' (each with a 'use' spelling). Retry with one of those, or browse with action='search'.",
                        "A GUID is always exact.",
                        "Adding a slider: pass min/max/value/decimals to configure it in one call (e.g. type='slider', min=0, max=100, value=50, decimals=2). Non-slider components ignore these.",
                        "Set min/max before value: value is clamped to the range. Same slider config as action='config'.",
                        "A slider value that doesn't parse as a number is an error — the component is NOT added (parity with action='config').",
                        "Also refused before anything is added: decimals outside 0-8, a min, max or value that is not finite or beyond ±7.92E+28, and min greater than max. A lone min or max is checked against the new slider's default 0-1 range, so pass both to move the range; min equal to max is allowed.",
                        "Leave 'nickname' unset during normal building — renamed components are hard to find on the canvas. Annotate with labeled groups (group_create) instead and track the returned id.",
                        "One undo step: Ctrl+Z removes the object. A failure after the object is placed rolls the call back, so nothing is left on the canvas."
                    }
                },
                ["delete"] = new ActionInfo
                {
                    Name = "delete",
                    Description = "Remove component(s) from the canvas",
                    Required = new string[0],
                    Optional = new[] { "id", "ids" },
                    Example = "action='delete', id='abc-123' OR action='delete', ids='[\"a\",\"b\"]'",
                    Tips = new[] {
                        "Use 'id' for single, 'ids' (JSON array) for bulk",
                        "Each id may be a GUID, an 8+ hex GUID prefix, or a unique nickname you set. Every id is resolved before anything is deleted",
                        "A default nickname (the one every new object of the type gets, like 'Circle') never selects what delete removes, even when only one object has it: the error prints the id to pass, or rename the object first (action='rename') and pass the new name",
                        "A name that fits two objects, a default nickname, or an id that fits nothing, deletes nothing for that entry (an ambiguous name's error lists the candidates by id); the other entries are still deleted (per-id results)",
                        "A repeated object is deleted once; the later entry carries note 'same object as entry i'",
                        "Everything one call deletes is one undo step: Ctrl+Z brings back the objects and every wire, upstream and downstream. One solve is scheduled for the whole call",
                        "Deleting a Relay drops its wires; unlike Grasshopper's own Delete, it does not reconnect the relay's source to its targets"
                    }
                },
                ["move"] = new ActionInfo
                {
                    Name = "move",
                    Description = "Move component(s) to new position(s)",
                    Required = new string[0],
                    Optional = new[] { "id", "x", "y", "moves" },
                    Example = "action='move', id='abc', x=100, y=200",
                    Tips = new[] {
                        "Use id/x/y for single, 'moves' JSON array for bulk",
                        "Each id may be a GUID, an 8+ hex GUID prefix, or a unique nickname; every entry is resolved before anything moves, and failures are per-entry",
                        "The entries that move are one undo step. An object already at its target is reported as moved but records nothing; an object with no canvas attributes fails its entry. A call that moves nothing returns no undo"
                    }
                },
                ["rename"] = new ActionInfo
                {
                    Name = "rename",
                    Description = "Change a component's nickname (display name)",
                    Required = new[] { "id", "nickname" },
                    Example = "action='rename', id='abc-123', nickname='MyCircle'",
                    Tips = new[]
                    {
                        "Avoid renaming components as part of normal building — renamed components are hard to find on the canvas and it is not the Grasshopper convention. Annotate with labeled groups (group_create), panels, or scribbles instead; only rename when the user explicitly asks for it",
                        "Renaming to the current nickname records nothing and returns no undo"
                    }
                },
                ["find"] = new ActionInfo
                {
                    Name = "find",
                    Description = "Find component(s) by nickname",
                    Required = new[] { "nickname" },
                    Optional = new[] { "exact" },
                    Example = "action='find', nickname='Circle'",
                    Tips = new[] { "Matches default nicknames too (e.g. 'Circle', 'Slider') — no need to rename components to make them findable; prefer tracking the id returned from add" }
                },
                ["search"] = new ActionInfo
                {
                    Name = "search",
                    Description = "Search available component types (not on canvas)",
                    Required = new[] { "query" },
                    Optional = new[] { "category", "limit" },
                    Example = "action='search', query='circle', category='Curve'",
                    Tips = new[] { "'type' is accepted as an alias for 'query'" }
                },
                ["list"] = new ActionInfo
                {
                    Name = "list",
                    Description = "List components currently on the canvas",
                    Optional = new[] { "category", "typeFilter", "group" },
                    Example = "action='list' OR action='list', category='Curve' OR action='list', typeFilter='Number Slider'",
                    Tips = new[] { "group= takes the group's exact name (case-sensitive, never a substring), its GUID, or an 8+ hex GUID prefix; a name two groups share is an error listing both ids" }
                },
                // A method call, not a static field: static initialisers in different partial
                // files have no defined order, so a field there could still be null here.
                ["outline"] = OutlineActionInfo(),
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
                    Example = "action='constant', value='3.14159', x=50, y=100",
                    Tips = new[] { "One undo step: Ctrl+Z removes the panel. A failure after the panel is placed rolls the call back, so nothing is left on the canvas" }
                },
                ["bake"] = new ActionInfo
                {
                    Name = "bake",
                    Description = "Bake component geometry to Rhino document",
                    Required = new[] { "id" },
                    Optional = new[] { "layer", "name" },
                    Example = "action='bake', id='abc-123', layer='Baked'",
                    Tips = new[] { "Creates permanent Rhino objects from component output", "Specify layer to organize baked geometry", "All baked objects land in one undo record — a single Ctrl-Z in Rhino removes the whole bake" }
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
                    Tips = new[] {
                        "For sliders, value must be a finite number within the slider's range",
                        "For toggles, value is true/false, 1/0 or yes/no (case-insensitive); anything else is an error, never read as false",
                        "For value lists, value is an item's name or value (case ignored); that item becomes the only one selected",
                        "A value equal to the current one records nothing and returns no undo"
                    }
                },
                ["config"] = new ActionInfo
                {
                    Name = "config",
                    Description = "Configure input component constraints",
                    Required = new[] { "id" },
                    Optional = new[] { "min", "max", "value", "decimals", "items" },
                    Example = "action='config', id='abc', min=0, max=100, value=50",
                    Tips = new[] {
                        "For value lists, items is required: items='[{\"name\":\"A\",\"value\":\"0\"}]'. It must be a non-empty array of objects with only the keys name and value (a missing one defaults to 'Item' / '0'); one bad item refuses the whole list before the list is touched",
                        "A value list keeps its selection by item name where that item still exists; otherwise item 0 is selected",
                        "Refused before anything is applied: a non-numeric 'value' (parity with action='set'), decimals outside 0-8, a min, max or value that is not finite or beyond ±7.92E+28, and min greater than max. A lone min or max is checked against the slider's current other bound, so pass both to move the range; min equal to max is allowed",
                        "Settings equal to the current ones record nothing; a call that changes nothing returns no undo"
                    }
                },
                ["preview"] = new ActionInfo
                {
                    Name = "preview",
                    Description = "Set component preview visibility",
                    Required = new[] { "enabled" },
                    Optional = new[] { "id", "ids" },
                    Example = "action='preview', id='abc', enabled=false",
                    Tips = new[] {
                        "Returns per-id results with changedCount/failedCount; unresolvable ids or components without preview support are per-id failures and overall success is false",
                        "enabled accepts true/false, 1/0, yes/no (case-insensitive); anything else is a validation error",
                        "The ids that change are one undo step. An id already in the requested state counts in changedCount but records nothing"
                    }
                },
                ["enable"] = new ActionInfo
                {
                    Name = "enable",
                    Description = "Enable or disable component computation",
                    Required = new[] { "enabled" },
                    Optional = new[] { "id", "ids" },
                    Example = "action='enable', id='abc', enabled=false",
                    Tips = new[] {
                        "Returns per-id results with changedCount/failedCount; unresolvable ids or components that can't be locked are per-id failures and overall success is false",
                        "enabled accepts true/false, 1/0, yes/no (case-insensitive); anything else is a validation error",
                        "The ids that change are one undo step. An id already in the requested state counts in changedCount but records nothing"
                    }
                },
                // Group actions (from gh_group)
                ["group_create"] = new ActionInfo
                {
                    Name = "group_create",
                    Description = "Create a visual group",
                    Required = new[] { "name" },
                    Optional = new[] { "ids", "color" },
                    Example = "action='group_create', name='Inputs', ids='[\"a\",\"b\"]', color='#FF6B6B'",
                    Tips = new[] { "Malformed 'ids' JSON is rejected with an error — no group is created", MembersTip }
                },
                ["group_delete"] = new ActionInfo
                {
                    Name = "group_delete",
                    Description = "Delete a visual group (components remain)",
                    Required = new[] { "id" },
                    Example = "action='group_delete', id='abc-123'",
                    Tips = new[] { GroupIdTip, "The default group name 'Group' never selects what group_delete removes, even when only one group has it: the error prints the id to pass, or rename the group first (action='group_rename') and pass the new name" }
                },
                ["group_add"] = new ActionInfo
                {
                    Name = "group_add",
                    Description = "Add components to a group",
                    Required = new[] { "ids" },
                    Optional = new[] { "id", "name", "color" },
                    Example = "action='group_add', ids='[\"a\",\"b\"]', name='MyGroup'",
                    Tips = new[] { "If 'id' is provided it must resolve to an existing group (error otherwise); omit 'id' to create a new group", GroupIdTip, MembersTip }
                },
                ["group_remove"] = new ActionInfo
                {
                    Name = "group_remove",
                    Description = "Remove components from a group",
                    Required = new[] { "id", "ids" },
                    Example = "action='group_remove', id='group-id', ids='[\"comp-a\"]'",
                    Tips = new[] { GroupIdTip, MembersTip, "An entry that resolves but is not in the group is listed in 'notMembers'. A call where no entry is a member changes nothing and returns no undo" }
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
                    Example = "action='group_rename', id='abc-123', name='NewName'",
                    Tips = new[] { GroupIdTip }
                },
                ["group_color"] = new ActionInfo
                {
                    Name = "group_color",
                    Description = "Set group color",
                    Required = new[] { "id", "color" },
                    Example = "action='group_color', id='abc', color='#4ECDC4'",
                    Tips = new[] { GroupIdTip }
                },
                ["group_move"] = new ActionInfo
                {
                    Name = "group_move",
                    Description = "Move all components in a group by offset",
                    Required = new[] { "id", "dx", "dy" },
                    Example = "action='group_move', id='abc', dx=100, dy=50",
                    Tips = new[] { GroupIdTip }
                },
                ["zoomable"] = new ActionInfo
                {
                    Name = "zoomable",
                    Description = "Manage zoomable/variable parameters on components",
                    Required = new[] { "id" },
                    Optional = new[] { "side", "operation", "count", "index" },
                    Example = "action='zoomable', id='abc', side='input', operation='add'",
                    Tips = new[] {
                        "side: 'input' or 'output' (default: 'input')",
                        "operation: 'add', 'remove', 'set_count'",
                        "count: target number for 'set_count'",
                        "index: specific position for 'add'/'remove'",
                        "remove, and a set_count below the current count, cut the wires on the ports they remove, so they never select the component by a default nickname (one every new object of the type gets, like 'Merge'), even a unique one: pass the id the error prints. add and a growing set_count accept it",
                        "On a C# or Python script component the program is compiled against its ports, so a reshape rebuilds it before the call returns and the reply carries rebuilt/rebuildSkipped/rebuildFailed and verified/verificationSkipped, meaning exactly what they do for gh_script(action='configure'). Other components (Merge, Entwine, Gate) have no program and get no rebuild fields",
                        "One undo step: Ctrl+Z restores the ports, their wires and, on a script component, its source",
                        "A set_count the component cannot finish (it refuses a port partway) rolls back: success=false, rolledBack=true, and the component is unchanged. A finished set_count replies targetCount, actualCount, added and removed, with errors=null. A set_count already at its count returns no undo"
                    }
                },
                ["modifier"] = new ActionInfo
                {
                    Name = "modifier",
                    Description = "Read or set a parameter's data modifiers (flatten/graft, simplify, reverse)",
                    Required = new[] { "id" },
                    Optional = new[] { "side", "param", "mapping", "simplify", "reverse" },
                    Example = "action='modifier', id='abc', side='input', param='P', mapping='graft'",
                    Tips = new[] {
                        "Omit mapping/simplify/reverse to READ the current state; each one you pass is applied, the rest are left unchanged",
                        "mapping: 'none', 'flatten', or 'graft' (Grasshopper's DataMapping)",
                        "simplify/reverse: true or false",
                        "param: the port's exact name or nickname (case ignored only when that leaves one match), or its 0-based index; not needed for a free-floating parameter. Partial names are refused with suggestions.",
                        "side: 'input' or 'output' (default: 'input')",
                        "Clear everything with mapping='none', simplify=false, reverse=false",
                        "action='info' reports the same modifiers for every param of a component",
                        "A write that changes something is one undo step; a read, or a write whose values are already set, records nothing and returns no undo"
                    }
                }
            },
            Notes = new[]
            {
                "Disable solver before bulk operations: gh_document(action='solver', enabled=false)",
                "Recommended spacing: 150px horizontal, 70px vertical between components",
                "Use action='zoom' before capturing to ensure all components are visible",
                "Groups are visual containers - deleting a group doesn't delete its components. Each group action is one undo step; Ctrl+Z restores membership, name, colour and member positions",
                "Ids: an object is named by its GUID, an 8+ hex GUID prefix, or its unique nickname (exact, case-sensitive); a group action's 'id' takes the group's name or id. Nothing is guessed: a name that fits two things acts on nothing and lists their ids, and a near miss gets a 'Did you mean'",
                "Undo: every call that changes the canvas is one Grasshopper undo step, so a human's Ctrl+Z reverts the whole call. The reply's undo field names the step ('Cordyceps: gh_canvas <action>') and is absent when nothing changed. bake is a Rhino undo record instead; zoom and view change only the view. Agents restore state with gh_document snapshot/revert",
                "A call that fails partway rolls back: success=false, error, rolledBack=true, and the canvas is as it was. rolledBack=false with rollbackFailures means some changes could not be undone: check the canvas, because Ctrl+Z does not cover that call",
                "Per-id actions (delete, move, preview, enable): an id refused at validation is reported in results and never touched, and the rest are one undo step. An unexpected failure while applying rolls the whole call back instead",
                "A success can carry warning: the edit was made, but a step after it (restoring the solver, scheduling the solve) failed. Check gh_document(action='info'), then gh_document(action='recompute'). solve appears only when the solve the edit asked for was refused (solver_disabled while you have the solver off is expected); it never fails the edit. See gh://docs/common-errors"
            }
        };

        public GhCanvasTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Component operations. Actions: add|delete|move|rename|find|search|list|outline|info|bounds|validate|constant|bake|zoom|view|get|set|config|preview|enable|group_create|group_delete|group_add|group_remove|group_list|group_rename|group_color|group_move|zoomable|modifier|help")]
        public string GhCanvas(
            [Description("Action to perform")] string action,
            [Description("Component type for 'add' (exact name, 'Category/Name', 'Category/Subcategory/Name', or GUID; never matched partially), search query for 'search', or type filter for 'list'")] string type = null,
            [Description("X position")] double x = double.NaN,
            [Description("Y position")] double y = double.NaN,
            [Description("Object: GUID, 8+ hex GUID prefix, or unique nickname; on group_* actions: a group name or id. delete, group_delete and a zoomable call that removes a port never take a default nickname or the default group name 'Group'")] string id = null,
            [Description("JSON array of objects, each a GUID, 8+ hex GUID prefix, or unique nickname (for delete, not a default nickname)")] string ids = null,
            [Description("JSON array of moves: [{id,x,y},...]; each id is an object: GUID, 8+ hex GUID prefix, or unique nickname")] string moves = null,
            [Description("Nickname for rename/find/add")] string nickname = null,
            [Description("Search query")] string query = null,
            [Description("Category filter")] string category = null,
            [Description("Type filter for list")] string typeFilter = null,
            [Description("Group for list/outline: the group's exact name, GUID, or 8+ hex GUID prefix (outline also takes a JSON array of them)")] string group = null,
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
            [Description("Y offset for group move")] double dy = double.NaN,
            // Zoomable params
            [Description("Parameter side: 'input' or 'output'")] string side = null,
            [Description("Operation: 'add', 'remove', 'set_count'")] string operation = null,
            [Description("Target count for set_count")] int count = -1,
            [Description("Index for add/remove")] int index = -1,
            // Data modifier params
            [Description("Port for 'modifier': exact name or nickname, or 0-based index (never matched partially)")] string param = null,
            [Description("Data mapping for 'modifier': 'none', 'flatten', or 'graft'")] string mapping = null,
            [Description("Simplify state for 'modifier': true or false")] string simplify = null,
            [Description("Reverse state for 'modifier': true or false")] string reverse = null,
            // Outline params
            [Description("Wires each way for outline id= (1-10, default 2)")] int depth = 0)
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
                ("dy", double.IsNaN(dy) ? null : (object)dy),
                // Zoomable params
                ("side", side),
                ("operation", operation),
                ("count", count >= 0 ? (object)count : null),
                ("index", index >= 0 ? (object)index : null),
                // Data modifier params
                ("param", param),
                ("mapping", mapping),
                ("simplify", simplify),
                ("reverse", reverse),
                // Outline params (0 = not given)
                ("depth", depth != 0 ? (object)depth : null)
            );

            // 'type' doubles as the search query (documented on the param), so let a provided
            // 'type' satisfy the 'search' action's required 'query' — keeping the query??type
            // fallback in the dispatch reachable.
            if (string.Equals(action, "search", StringComparison.OrdinalIgnoreCase)
                && !providedParams.ContainsKey("query") && type != null)
            {
                providedParams["query"] = type;
            }

            // Validate action and required params
            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse enabled strictly for preview/enable ('enabled' is required for both):
            // an unrecognized string must be a validation error, not silently coerced to true.
            bool enabledBool = true;
            if (string.Equals(action, "preview", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "enable", StringComparison.OrdinalIgnoreCase))
            {
                if (!ToolHelpers.TryParseBool(enabled, out enabledBool))
                    return ToolHelpers.ErrorResponse($"Invalid 'enabled' value: '{enabled}'. Use true/false, 1/0, or yes/no.");
            }

            // Dispatch to action handler
            return action.ToLowerInvariant() switch
            {
                "add" => ActionAdd(type, x, y, nickname, min, max, value, decimals),
                "delete" => ActionDelete(id, ids),
                "move" => ActionMove(id, x, y, moves),
                "rename" => ActionRename(id, nickname),
                "find" => ActionFind(nickname, exact),
                "search" => ActionSearch(query ?? type, category, limit),
                "list" => ActionList(category, typeFilter ?? type, group),
                "outline" => ActionOutline(id, group, depth),
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
                "zoomable" => ActionZoomable(id, side, operation, count, index),
                "modifier" => ActionModifier(id, side, param, mapping, simplify, reverse),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionAdd(string type, double x, double y, string nickname,
            double min, double max, string value, int decimals)
        {
            Core.DebugLog.Info($"gh_canvas add: type='{type}', x={x}, y={y}, nickname='{nickname}'");

            // A component's own constructor or attributes can throw while it is built, before the
            // document is touched; that reaches the tool-invocation boundary, which answers it as a
            // failed call. Everything from the add on runs inside the edit, so a throw there rolls
            // the add back instead of leaving a half-configured object on the canvas.
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var (component, resolution, typeError) = ComponentRegistry.TryCreate(type);

                if (component == null)
                    return JsonConvert.SerializeObject(TypeNameRule.ErrorPayload(resolution, typeError));

                if (component.Attributes == null)
                    component.CreateAttributes();

                if (!string.IsNullOrEmpty(nickname))
                    component.NickName = nickname;

                component.Attributes.Pivot = new PointF((float)x, (float)y);

                // Validate slider configuration BEFORE adding to the document, so a setting the
                // slider cannot hold refuses the call and never leaves a half-configured slider on
                // the canvas. A lone min or max is checked against the new slider's own range.
                SliderConfigPlan sliderPlan = null;
                if (component is GH_NumberSlider newSlider)
                {
                    sliderPlan = SliderConfig.Plan(min, max, value, decimals,
                        newSlider.Slider.Minimum, newSlider.Slider.Maximum);
                    if (sliderPlan.Error != null)
                        return ToolHelpers.ErrorResponse(sliderPlan.Error);
                }

                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "add"), tx =>
                {
                    // Tracked, added and recorded by the transaction, which also recomputes it at commit.
                    tx.AddObject(component);

                    // Apply slider configuration when the caller passed min/max/value/decimals on add.
                    // Non-slider components ignore these params. Shares Core.SliderConfig with the
                    // 'config' action so add and config configure a slider identically. The slider
                    // is new in this edit, so its range and value need no undo record of their own.
                    if (component is GH_NumberSlider addedSlider)
                    {
                        var plan = sliderPlan;
                        if (plan.SetMinimum) addedSlider.Slider.Minimum = plan.Minimum;
                        if (plan.SetMaximum) addedSlider.Slider.Maximum = plan.Maximum;
                        if (plan.SetDecimals) addedSlider.Slider.DecimalPlaces = plan.Decimals;
                        if (plan.SetValue) addedSlider.SetSliderValue(plan.Value);
                    }

                    var bounds = component.Attributes.Bounds;
                    int inputCount = 0, outputCount = 0;
                    // A created component always comes with a resolved match describing the type
                    // created (TryCreate's contract), so its category is authoritative.
                    string categoryStr = resolution.Match.Category;
                    string subcategory = resolution.Match.SubCategory;

                    if (component is IGH_Component ghComp)
                    {
                        inputCount = ghComp.Params.Input.Count;
                        outputCount = ghComp.Params.Output.Count;
                    }
                    else if (component is IGH_Param)
                    {
                        outputCount = 1;
                    }

                    var added = new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["id"] = component.InstanceGuid.ToString(),
                        ["type"] = component.Name,
                        ["nickname"] = component.NickName,
                        ["category"] = categoryStr,
                        ["subcategory"] = subcategory,
                        ["role"] = ComponentRegistry.GetRole(categoryStr, subcategory),
                        ["x"] = component.Attributes.Pivot.X,
                        ["y"] = component.Attributes.Pivot.Y,
                        ["width"] = bounds.Width,
                        ["height"] = bounds.Height,
                        ["inputCount"] = inputCount,
                        ["outputCount"] = outputCount
                    };

                    // A deprecated type is only ever chosen when it is the sole match; say so, so
                    // the caller can move to the replacement deliberately.
                    if (resolution.UsedDeprecated)
                    {
                        added["deprecated"] = true;
                        added["warning"] = $"'{resolution.Match.PathText}' is deprecated; it was used because it is the only type with that name." +
                                           (resolution.Match.UpgradeTo != null ? $" Its replacement is '{resolution.Match.UpgradeTo}'." : "");
                        if (resolution.Match.UpgradeTo != null)
                        {
                            added["upgradeTo"] = resolution.Match.UpgradeTo;
                            added["upgradeToGuid"] = resolution.Match.UpgradeToGuid?.ToString();
                        }
                    }

                    return JsonConvert.SerializeObject(added);
                });
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
                    catch (JsonException ex) { return ToolHelpers.ErrorResponse($"Invalid ids format: {ex.Message}"); }
                    if (idList == null) return ToolHelpers.ErrorResponse("Invalid ids format: expected a JSON array");
                }
                else if (!string.IsNullOrEmpty(id))
                {
                    idList = new List<string> { id };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Either 'id' or 'ids' is required");
                }

                // Pass 1: every id resolves against one lookup before the first removal, so a bad or
                // ambiguous id deletes nothing of its own and is reported. Pass 2 removes every id
                // that resolved, as one edit: either all of them go, as one undo step, or an
                // unexpected failure rolls the whole call back.
                var lookup = new ObjectLookup(doc);
                var outcomes = ToolHelpers.ResolveIds(lookup, idList, acceptGroups: true, Core.Canvas.ResolveIntent.Destructive, out var resolved);

                var results = new List<object>();
                var deletedIds = new List<string>();
                var toDelete = new List<IGH_DocumentObject>();
                var deletedGuids = new HashSet<Guid>();
                int succeeded = 0, failed = 0;

                foreach (var outcome in outcomes)
                {
                    if (!outcome.IsResolved)
                    {
                        results.Add(new { id = outcome.Text, success = false, error = outcome.Error });
                        failed++;
                        continue;
                    }

                    var resolvedId = outcome.Id.Value.ToString();
                    if (!outcome.ActOn)
                    {
                        // A repeat of an earlier entry, which acts on the object; report its result.
                        if (deletedGuids.Contains(outcome.Id.Value))
                        {
                            results.Add(new { id = resolvedId, success = true, note = outcome.Note });
                            succeeded++;
                        }
                        else
                        {
                            results.Add(new { id = outcome.Text, success = false, error = $"{outcome.Note}, which was not deleted", note = outcome.Note });
                            failed++;
                        }
                        continue;
                    }

                    toDelete.Add(resolved[outcome.Id.Value]);
                    deletedGuids.Add(outcome.Id.Value);
                    deletedIds.Add(resolvedId);
                    results.Add(new { id = resolvedId, success = true });
                    succeeded++;
                }

                string Reply()
                {
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
                }

                if (toDelete.Count == 0)
                    return Reply();

                // Each removal records the object's wires (its inputs and every input it feeds)
                // and then the object, so undo brings back the objects and every wire, upstream and
                // downstream. The objects they fed are recomputed once, by one solve at commit.
                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "delete"), tx =>
                {
                    foreach (var obj in toDelete)
                        tx.RemoveObject(obj);
                    return Reply();
                });
            });
        }

        private string ActionMove(string id, double x, double y, string moves)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<MoveEntry> entries;
                if (!string.IsNullOrEmpty(moves))
                {
                    // Names stay text (a nickname can look like a date); see Core/BulkEntries.
                    if (!BulkEntries.TryParseMoves(moves, out entries, out error))
                        return ToolHelpers.ErrorResponse(error);
                }
                else if (!string.IsNullOrEmpty(id) && !double.IsNaN(x) && !double.IsNaN(y))
                {
                    entries = new List<MoveEntry> { new MoveEntry(id, x, y, null) };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Provide 'id'+'x'+'y' for single move, or 'moves' array for bulk");
                }

                // Pass 1: resolve every entry's id against one lookup, before anything moves. Each
                // entry carries its own position, so a second entry for one object is a failed
                // entry, never a dropped position (IdBatch.RefuseRepeats). An object with no
                // attributes has no position to set, so it is refused here, not partway through.
                // Pass 2 moves only the entries that passed, as one undo step.
                var lookup = new ObjectLookup(doc);
                var outcomes = IdBatch.RefuseRepeats(
                    ToolHelpers.ResolveIds(lookup, entries.Select(e => e.Id).ToList(), acceptGroups: true, Core.Canvas.ResolveIntent.Ordinary, out var resolved),
                    "give one position per object");

                var results = new List<object>();
                var pending = new List<(IGH_DocumentObject Obj, PointF Pivot)>();
                int successCount = 0, failCount = 0;
                foreach (var outcome in outcomes)
                {
                    var entry = entries[outcome.Index];
                    if (!outcome.ActOn || entry.Error != null)
                    {
                        results.Add(new { success = false, id = outcome.Text, error = outcome.Error ?? entry.Error });
                        failCount++;
                        continue;
                    }

                    var guid = outcome.Id.Value;
                    var component = resolved[guid];
                    if (component.Attributes == null)
                    {
                        results.Add(new { success = false, id = outcome.Text, error = $"'{component.NickName}' ({guid}) has no canvas attributes, so it has no position to set" });
                        failCount++;
                        continue;
                    }

                    pending.Add((component, new PointF((float)entry.X, (float)entry.Y)));
                    results.Add(new { success = true, id = guid.ToString(), x = entry.X, y = entry.Y });
                    successCount++;
                }

                string Reply()
                {
                    if (entries.Count == 1 && successCount == 1)
                    {
                        var r = (dynamic)results[0];
                        return JsonConvert.SerializeObject(new { success = true, id = r.id, x = r.x, y = r.y });
                    }

                    return JsonConvert.SerializeObject(new { success = failCount == 0, total = entries.Count, succeeded = successCount, failed = failCount, results });
                }

                // An object already at its position is reported as moved but records nothing, so a
                // call that moves nothing pushes no undo step.
                var moving = pending.Where(p => p.Obj.Attributes.Pivot != p.Pivot).ToList();
                if (moving.Count == 0)
                    return Reply();

                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "move"), tx =>
                {
                    foreach (var (obj, pivot) in moving)
                    {
                        tx.RecordPivot(obj);
                        obj.Attributes.Pivot = pivot;
                        obj.Attributes.ExpireLayout();
                    }
                    Instances.ActiveCanvas?.Invalidate();
                    return Reply();
                });
            });
        }

        private string ActionRename(string id, string nickname)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);
                if (!ToolHelpers.TryGetUnprotectedObject(new ObjectLookup(doc), id, out var component, out error))
                    return ToolHelpers.ErrorResponse(error);

                string Reply() => JsonConvert.SerializeObject(new { success = true, id = component.InstanceGuid.ToString(), nickname });

                // Renaming to the current name changes nothing, so it records nothing and pushes no undo step.
                if (string.Equals(component.NickName, nickname, StringComparison.Ordinal))
                    return Reply();

                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "rename"), tx =>
                {
                    tx.RecordNickName(component);
                    component.NickName = nickname;
                    component.Attributes?.ExpireLayout();
                    Instances.ActiveCanvas?.Invalidate();
                    return Reply();
                });
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

                // Use GetActiveObjects to filter out phantom/orphaned objects
                foreach (var obj in ToolHelpers.GetActiveObjects(doc, infraIds))
                {
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
                    // The group's exact name (ordinal), full GUID or 8+ hex GUID prefix; never a substring.
                    if (!ToolHelpers.TryGetUnprotectedGroup(new ObjectLookup(doc), group, out var targetGroup, out var groupError))
                        return ToolHelpers.ErrorResponse(groupError);
                    groupMemberIds = new HashSet<Guid>(targetGroup.ObjectIDs ?? new List<Guid>());
                }

                var components = new List<object>();
                // Use GetActiveObjects to filter out phantom/orphaned objects
                foreach (var obj in ToolHelpers.GetActiveObjects(doc, infraIds))
                {
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
                if (!ToolHelpers.TryGetUnprotectedObject(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var info = ToolHelpers.BuildFullComponentInfo(obj, includeSuccess: true);
                return JsonConvert.SerializeObject(info);
            });
        }

        private string ActionBounds(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedObject(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var bounds = component.Attributes.Bounds;
                var pivot = component.Attributes.Pivot;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
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
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                // Use GetActiveObjects to filter out phantom/orphaned objects
                var components = ToolHelpers.GetActiveObjects(doc, infraIds).Where(c => !(c is GH_Group)).ToList();

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
                // UserText, not SetUserText: SetUserText also calls ExpireSolution(true), which
                // would solve outside the edit. The rest of what it does, expiring the layout so
                // the panel draws the new text, is done here; the commit's solve fills its data.
                panel.UserText = value;
                panel.Attributes.ExpireLayout();
                panel.NickName = nickname ?? "";

                // The panel is new, so its text is set before the add and needs no record; the
                // transaction tracks, adds and records it, and recomputes it at commit.
                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "constant"), tx =>
                {
                    tx.AddObject(panel);

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
            });
        }

        private string ActionBake(string id, string layer, string name)
        {
            return _context.ExecuteOnUiThread(() => ToolHelpers.WithUndoRecord(Rhino.RhinoDoc.ActiveDoc, "bake", () =>
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
            }));
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
                    if (!ToolHelpers.TryGetUnprotectedObject(_context, id, out var component, out var error))
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

        // Value Adjustment Actions - see GhCanvasTool.Values.cs
        // Group Actions - see GhCanvasTool.Groups.cs
        // Zoomable Actions - see GhCanvasTool.Zoomable.cs
    }
}
