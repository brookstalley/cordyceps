# Best Practices

## Essential Rules

1. **Disable solver during bulk operations**
   ```
   gh_document(action='solver', enabled=false)
   // ... add components, configure, connect ...
   gh_document(action='solver', enabled=true)
   ```
   Turning it back on solves and waits; its reply's `solve.state` says whether the solve
   `completed` before you check status.

2. **Annotate with labeled groups, not renames**: `gh_canvas(action='group_create', name='Radius controls', ids='[...]')` — the Grasshopper convention. Do NOT rename components while building (`nickname=` on add, or action='rename'): renamed components are hard to find on the canvas. Track components by the `id` returned from `add`; panels and scribbles also work for annotation.

3. **Validate before connecting**: `gh_wire(action='validate', ...)` — failed connections don't error, they just disconnect

4. **Check status after changes**: `gh_inspect(action='status')` — shows errors, warnings, disconnected inputs

5. **Match data structures**: Check tree structures before connecting — `gh_inspect(action='tree', id='...', side='input')` shows what each input receives. See `gh://docs/data-trees`.

6. **Orient before editing an existing definition**: `gh_canvas(action='outline')` gives groups, data flow, controls, counts and problems in one call; then `group=` or `id=` for detail. See `gh://docs/canvas-outline`.

## Anti-Patterns

| Don't | Do |
|-------|-----|
| Guess component names | `gh_canvas(action='search', query='...')` |
| Ignore orange warnings | `gh_inspect(action='status')` |
| Chain without checking | Verify `result.success` |
| Flatten everything | Understand structure first |
| Create cycles | Design as DAG |

## Naming Conventions

| Type | Name After |
|------|------------|
| Sliders | What they control: "Radius", "Height" |
| Panels | Data source: "Points from File" |
| Scripts | Function: "Calculate Area" |
| Groups | Purpose: "Geometry Generation" |

## Expensive Operations (Cache with Data Dam)

- Boolean operations (Solid Union/Difference)
- Mesh operations on dense meshes
- Kangaroo simulations
- Curve/surface intersections

## Debugging

1. `gh_inspect(action='status')` — find every error and warning, on components and on parameter objects such as sliders and panels (outline reads the same objects but caps its list at 10)
2. `gh_canvas(action='outline', id='...')` — what feeds the object and what it feeds
3. `gh_inspect(action='tree', id='...')` — branch paths and item counts, without the data
4. `gh_inspect(action='trace', id='...', direction='upstream')` — trace data flow
5. `gh_inspect(action='outputs', id='...')` — the values themselves
6. Add Panel components to visualize intermediate data

## Bake and Cleanup

```
gh_canvas(action='bake', id='...', layer='temp_preview')
// ... inspect, render ...
rhino_scene(action='layer_delete', name='temp_preview', deleteObjects=true)
```

## Error Recovery

| Error | Fix |
|-------|-----|
| "No component type is named …" (adding) | Pass a `use` spelling from `suggestions`, or `gh_canvas(action='search')` |
| "Component not found" / "No object named …" (an id) | Verify the id: `gh_canvas(action='outline')` prints a usable name for every object |
| "… is the nickname of N objects …" | Pass one of the ids the error lists |
| "Connection failed" | `gh_canvas(action='info', id='...')` to check params |
| Solver loop | Disable solver, find cycle, fix, re-enable |
| Canvas frozen | `gh_document(action='solver', enabled=false)`, fix, re-enable |

See `gh://docs/common-errors` for comprehensive reference.
