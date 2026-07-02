# Grasshopper via Cordyceps

Visual dataflow graph for parametric 3D. Components on canvas connect via wires. Data flows left-to-right.

## Tools (7 total)

Use `action='help'` on any tool for parameters.

**Grasshopper:**
- `gh_canvas` — components, values, groups, bake, zoomable params
- `gh_wire` — connect, disconnect, list, clear, validate
- `gh_document` — save, clear, solver, snapshots, capture
- `gh_script` — get/set script code, configure params
- `gh_inspect` — status, outputs, trace, disconnected

**Rhino:**
- `rhino_scene` — objects, layers, selection, visibility
- `rhino_render` — display, camera, materials, environments, render

## Workflow

```
gh_document(action='solver', enabled=false)
gh_canvas(action='add', type='...', x=..., y=...)
gh_wire(action='connect', connections='[{"sourceId":"id1","sourceParam":"0","targetId":"id2","targetParam":"R"}]')
gh_document(action='solver', enabled=true)
gh_inspect(action='status')
```

## Values

- `gh_canvas(action='set', id='...', value='...')` — slider/panel/toggle
- `gh_canvas(action='config', id='...', min=..., max=...)` — slider range

## Scripts

**Updating script code**: `gh_script(action='set')` preserves connections for params whose names survive unchanged. If params are renamed or removed, the response includes `lostConnections` — an array directly usable with `gh_wire(action='connect')` to restore wiring.

**Configuring params**: `gh_script(action='configure')` preserves wires the same way — params matching by name keep their connections, and removed/renamed params come back in `lostConnections`. It's a partial update: omit a side (don't pass `inputs` or `outputs`) to leave it untouched, or pass `[]` to explicitly clear that side. So configuring only `inputs` no longer wipes your outputs.

## Groups

- `gh_canvas(action='group_create', name='...', ids='[...]', color='#FF6B6B')`
- `gh_canvas(action='group_list')`

## Variable Parameters (ZUI)

- `gh_canvas(action='zoomable', id='...', operation='add', side='input')` — append a param (optional `index` for position)
- `gh_canvas(action='zoomable', id='...', operation='remove', side='input', index=2)` — remove a param
- `gh_canvas(action='zoomable', id='...', operation='set_count', side='input', count=4)` — set total param count

Operations: `add`, `remove`, `set_count`. Params: `side` ('input'/'output', default 'input'), `index`, `count`. Use `gh_canvas(action='info')` to list current params.

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

Avoid backwards wires. Stack inputs vertically at x=50 (70px vertical gaps). Processing columns at x=300, 450, 600... (150px horizontal gaps).
Use `gh_canvas(action='validate')` for overlaps. See `gh://docs/canvas-layout`.

## Clusters

When the cluster editor is open, `gh_document(action='recompute')` and `gh_document(action='solver', enabled=true)` are safe to use — they preserve cluster input data. Grasshopper's native recompute (F5) is NOT safe inside the cluster editor — it will null out all cluster inputs.

## Resources

- `gh://docs/data-trees` — essential for list/tree ops
- `gh://docs/common-errors` — error→fix reference
- `gh://docs/rendering` — bake→materials→viewport→capture
- `gh://component/{name}` — component I/O docs
- `gh://patterns/*` — linear-array, grid-array
