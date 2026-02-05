# Grasshopper via Cordyceps

Visual dataflow graph for parametric 3D. Components on canvas connect via wires. Data flows left-to-right.

## Tools (7 total)

Use `action='help'` on any tool for parameters.

**Grasshopper:**
- `gh_canvas` — components, values, groups, bake, zoomable params
- `gh_wire` — connect, disconnect, list, validate
- `gh_document` — save, clear, solver, snapshots, capture
- `gh_script` — get/set script code, configure params
- `gh_inspect` — status, outputs, trace, disconnected

**Rhino:**
- `rhino_scene` — objects, layers, selection, visibility
- `rhino_render` — display, camera, materials, environments, render

## Workflow

```
gh_document(action='solver', enabled=false)
gh_canvas(action='add', type='...', x=..., y=..., nickname='...')
gh_wire(action='connect', connections='[{"source":"id1:0","target":"id2:R"}]')
gh_document(action='solver', enabled=true)
gh_inspect(action='status')
```

## Values

- `gh_canvas(action='set', id='...', value='...')` — slider/panel/toggle
- `gh_canvas(action='config', id='...', min=..., max=...)` — slider range

## Groups

- `gh_canvas(action='group_create', name='...', ids='[...]', color='#FF6B6B')`
- `gh_canvas(action='group_list')`

## Variable Parameters (ZUI)

- `gh_canvas(action='zoomable', id='...', operation='list')`
- `gh_canvas(action='zoomable', id='...', operation='add', param='...')`

## Capture

- `gh_document(action='capture_canvas')` — GH canvas
- `gh_document(action='capture_viewport')` — Rhino 3D view

## Materials

- `rhino_render(action='material_library')` — list types
- `rhino_render(action='material_instantiate', type='Metal', name='Gold', color='#FFD700')`
- `rhino_render(action='material_apply', ids='[...]', material='...')`

## Object Types

| Role | Description |
|------|-------------|
| component | Processing node with I/O (usually what you want) |
| parameter | Data container (Params category) |
| input | Interactive control (Params/Input) |

**Ambiguous names**: "Circle" matches both component and parameter. Use `Category/Name` or GUID.

## Layout

Avoid backwards wires. Stack inputs vertically at x=50. Processing columns at x=300, 380, 460...
Use `gh_canvas(action='validate')` for overlaps. See `gh://docs/canvas-layout`.

## Resources

- `gh://docs/data-trees` — essential for list/tree ops
- `gh://docs/common-errors` — error→fix reference
- `gh://docs/rendering` — bake→materials→viewport→capture
- `gh://component/{name}` — component I/O docs
- `gh://patterns/*` — linear-array, grid-array
