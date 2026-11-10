# Grasshopper via Cordyceps

Visual dataflow graph for parametric 3D. Components on canvas connect via wires. Data flows left-to-right.

## Tools (7 total)

Use `action='help'` on any tool for parameters.

**Grasshopper:**
- `gh_canvas` — components, values, groups, outline, bake, zoomable params, data modifiers
- `gh_wire` — connect, disconnect, list, clear, validate
- `gh_document` — save, clear, solver, snapshots, capture
- `gh_script` — get/set script code, configure params
- `gh_inspect` — status, outputs, tree, trace, disconnected

**Rhino:**
- `rhino_scene` — objects, layers, selection, visibility
- `rhino_render` — display, camera, materials, environments, render

## Orient First

On a definition you did not build, start with one call:

```
gh_canvas(action='outline')
```

It returns the groups and how data flows between them, the controls and their values, item
counts, and problems (by the `status` rule, capped at 10). Every line starts with a name you can
pass back as an id. Then `outline` with `group='NAME'` or `id='NAME'` for detail. See
`gh://docs/canvas-outline`. Outline is for orientation; `gh_inspect(action='status')` gives the
full problem list.

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

## Data Modifiers (Flatten / Graft / Simplify / Reverse)

The per-port right-click options, on any component parameter or free-floating param:

- `gh_canvas(action='modifier', id='...', side='input', param='B', mapping='graft')` — graft one input
- `gh_canvas(action='modifier', id='...', side='output', param='0', simplify=true)` — simplify an output
- `gh_canvas(action='modifier', id='...', side='input', param='B')` — READ the current state
- `gh_canvas(action='modifier', id='...', side='input', param='B', mapping='none', simplify=false, reverse=false)` — clear

`mapping`: `none` | `flatten` | `graft`. `simplify`/`reverse`: true/false. Partial update — whatever you
omit stays as it is, and passing none of the three reads instead of writing. `param` takes an exact name
or nickname, or a 0-based index, and is unnecessary for a free-floating param. `gh_canvas(action='info')` reports
`modifiers` for every param, so state round-trips.

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

## Names Are Exact

Names that select one thing are never guessed. A near miss is an error that tells you how to fix
the call, and nothing is created or wired.

**Canvas objects** (`id`, `ids`, `sourceId`, `targetId`, `moves`, `connections`, on every tool):

- The full GUID, as returned by `add`
- An 8+ hex-digit GUID prefix: `id='3f2a91c0'` (the short ids `outline` prints)
- The object's nickname, exact and case-sensitive, when exactly one object carries it. Default
  nicknames such as `Circle` count too, so they work while only one Circle is on the canvas

A nickname two objects share acts on nothing: the error lists both ids
(`'Pt' is the nickname of 2 objects: 1a2b3c4d Point, 5e6f7a8b Point; name one by its id`). A near miss gets `Did you mean`.
On `group_*` actions and `group=`, `id` names a **group** by its exact name, GUID or prefix; a group's
name does not work as an object id elsewhere (the error says so and gives the group's id).

**Component types** (`gh_canvas(action='add', type=...)`, `gh_inspect(action='docs')`,
`gh://component/{name}`):

- The exact name, case ignored: `type='Number Slider'`, `type='merge'`
- Narrowed by category: `type='Curve/Circle'`, `type='Curve/Primitive/Circle'`, or `type='Circle (Curve)'`
- A GUID, always exact
- A few exact whole-name shorthands: `python`, `c#`, `slider`, `plane` (XY Plane), `filter` (Stream Filter), plus the parameter shorthands `pt`, `crv`, `ln`, `circ`, `rect`, `num`, `int`, `bool`, `str`, … (`ln` is `Params/Line`)

Many names are both a component and a parameter: plain `Circle` or `Line` is **ambiguous**. The
error (`code: "ambiguous_name"`) lists every match with its category, role, plugin and GUID, plus a
`use` spelling for each — pass that back. An unknown or partial name (`code: "unknown_type"`)
returns `suggestions`, each with a `use` spelling; `Cull` does not create Cull Pattern. A deprecated
type is used only when it is the only one with that name, and the response says so. To browse,
use `gh_canvas(action='search', query='...')`, which does match partially.

**Ports** (`sourceParam`, `targetParam`, `param`): a 0-based index, or the port's exact name or
nickname (`targetParam='R'` or `'Radius'`). Case is ignored only when that leaves one match.
On a Circle, `'Rad'` is refused with "Did you mean 'R' (1 R (Radius))?" and the full port list for
that side.
`sourceParam` is always an output and `targetParam` always an input.

## Layout

Avoid backwards wires. Stack inputs vertically at x=50 (70px vertical gaps). Processing columns at x=300, 450, 600... (150px horizontal gaps).
Use `gh_canvas(action='validate')` for overlaps. See `gh://docs/canvas-layout`.

## Busy vs. dead

Every tool response carries a compact `status` block — `{document, ui, solving}` — telling you which
`.gh` file the call acted on and whether the host is healthy. If a call is slow or silent, call
`gh_inspect(action='connection')`: it reads cached state and never touches the document, so it answers
even when everything else is stuck. `ui: "blocked"` with `solving: true` means **wait**;
`modal_inferred: true` means a dialog is open in Rhino and **only a human can clear it**.

`gh_document(action='recompute')` refuses (with `solving: true`) while a solution is already
running, rather than queueing silently. See `gh://docs/common-errors`.

## Clusters

When the cluster editor is open, `gh_document(action='recompute')` and `gh_document(action='solver', enabled=true)` are safe to use — they preserve cluster input data. Grasshopper's native recompute (F5) is NOT safe inside the cluster editor — it will null out all cluster inputs.

## Resources

- `gh://docs/canvas-outline` — reading `gh_canvas(action='outline')`
- `gh://docs/data-trees` — essential for list/tree ops; `gh_inspect(action='tree')`
- `gh://docs/common-errors` — error→fix reference
- `gh://docs/rendering` — bake→materials→viewport→capture
- `gh://component/{name}` — component I/O docs (`{name}` is written as for `add`, e.g. `gh://component/Curve/Circle`)
- `gh://patterns/*` — linear-array, grid-array
