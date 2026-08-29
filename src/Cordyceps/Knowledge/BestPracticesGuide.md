# Best Practices

## Essential Rules

1. **Disable solver during bulk operations**
   ```
   gh_document(action='solver', enabled=false)
   // ... add components, configure, connect ...
   gh_document(action='solver', enabled=true)
   ```

2. **Annotate with labeled groups, not renames**: `gh_canvas(action='group_create', name='Radius controls', ids='[...]')` — the Grasshopper convention. Do NOT rename components while building (`nickname=` on add, or action='rename'): renamed components are hard to find on the canvas. Track components by the `id` returned from `add`; panels and scribbles also work for annotation.

3. **Validate before connecting**: `gh_wire(action='validate', ...)` — failed connections don't error, they just disconnect

4. **Check status after changes**: `gh_inspect(action='status')` — shows errors, warnings, disconnected inputs

5. **Match data structures**: Check tree structures before connecting. See `gh://docs/data-trees`.

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

1. `gh_inspect(action='status')` — find errors/warnings
2. `gh_inspect(action='outputs', id='...')` — check branch/item counts
3. `gh_inspect(action='trace', id='...', direction='upstream')` — trace data flow
4. Add Panel components to visualize intermediate data

## Bake and Cleanup

```
gh_canvas(action='bake', id='...', layer='temp_preview')
// ... inspect, render ...
rhino_scene(action='layer_delete', name='temp_preview', deleteObjects=true)
```

## Error Recovery

| Error | Fix |
|-------|-----|
| "Component not found" | `gh_canvas(action='search')`, verify ID |
| "Connection failed" | `gh_canvas(action='info', id='...')` to check params |
| Solver loop | Disable solver, find cycle, fix, re-enable |
| Canvas frozen | `gh_document(action='solver', enabled=false)`, fix, re-enable |

See `gh://docs/common-errors` for comprehensive reference.
