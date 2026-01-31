# Grasshopper via Cordyceps

Grasshopper is a visual dataflow graph for parametric 3D design. Components on a canvas connect via wires. Data flows left-to-right.

## Unified Tools (7 tools)

Use `action='help'` on any tool to see all available actions and parameters.

**Grasshopper Tools:**
- `gh_canvas` - Components, values, groups: add, delete, move, find, search, list, bake, zoom/view, get/set values, group management, zoomable parameter management
- `gh_wire` - Connect, disconnect, list, validate wires
- `gh_document` - Info, save, clear, solver control, snapshots, capture canvas/viewport
- `gh_script` - Get/set script code, configure parameters
- `gh_inspect` - Status, outputs, trace data flow, find disconnected

**Rhino Tools:**
- `rhino_scene` - Objects, selection, layers (CRUD), hide/show, delete
- `rhino_render` - Display modes, camera, render status, materials, environments

## Quick Reference

**Workflow:**
1. `gh_document(action='solver', enabled=false)` — prevent partial recalculation
2. Add components: `gh_canvas(action='add', type='...', x=..., y=...)`
3. Wire: `gh_wire(action='connect', connections='[...]')`
4. `gh_document(action='solver', enabled=true)` — run definition
5. `gh_inspect(action='status')` — check for errors

**Values:**
- `gh_canvas(action='set', id='...', value='...')` — set slider/panel/toggle
- `gh_canvas(action='config', id='...', min=..., max=...)` — configure slider range

**Groups:**
- `gh_canvas(action='group_create', name='...', ids='[...]', color='#FF6B6B')`
- `gh_canvas(action='group_list')` — list all groups

**Variable Parameters (ZUI):**
- `gh_canvas(action='zoomable', id='...', operation='list')` — list available zoomable params
- `gh_canvas(action='zoomable', id='...', operation='add', param='...')` — add input/output

**Capture:**
- `gh_document(action='capture_canvas')` — Grasshopper canvas
- `gh_document(action='capture_viewport')` — Rhino 3D viewport

**Materials:**
- `rhino_render(action='material_library')` — list built-in types
- `rhino_render(action='material_instantiate', type='Metal', name='Gold', color='#FFD700')`
- `rhino_render(action='material_apply', ids='[...]', material='...')`

**Environments:**
- `rhino_render(action='env_list')` — list environments
- `rhino_render(action='env_set', environment='...', usage='all')`

## Object Types

Three roles (check `role` field):
- **component**: Processing node with inputs/outputs (usually what you want)
- **parameter**: Data container (Params category)
- **input**: Interactive control (Params/Input)

**Ambiguous names**: "Circle" matches both Circle component (Curve/Primitive) and Circle parameter (Params/Geometry). On ambiguity, you get `ambiguous_name` error with matches. Resolve with GUID or `Category/Name` format.

**Deprecated**: Always prefer `deprecated=false`. Results are sorted with non-deprecated first.

## Layout

**Goal: Avoid backwards wires. Stack vertically.**

**Spacing:**
- Horizontal: 60-80px between columns
- Vertical: 70px between stacked components
- Inputs: Stack sliders at x≈50, y=50/120/190...

Use `gh_canvas(action='validate')` to check overlaps. See `gh://docs/canvas-layout` for details.

## Resources

- `gh://docs/data-trees` — essential for list/tree operations
- `gh://docs/canvas-layout` — spacing details
- `gh://docs/geometry-orientation` — plane axes for oriented geometry
- `gh://docs/common-errors` — error→fix quick reference
- `gh://docs/rendering` — bake → materials → viewport → capture
- `gh://docs/mcp-testing` — test instructions
- `gh://component/{name}` — component inputs/outputs
- `gh://patterns/*` — linear-array, grid-array
