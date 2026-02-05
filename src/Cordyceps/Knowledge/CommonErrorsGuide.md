# Common Errors

## Component Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Component not found" | ID doesn't exist | `gh_canvas(action='list')` or `gh_canvas(action='find', nickname='...')` |
| "Unknown component type" | Name doesn't match | `gh_canvas(action='search', query='...')` or use GUID |
| "Ambiguous component name" | Multiple matches | Use `Category/Name` format or GUID |

## Connection Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Source/Target not found" | Bad param name/index | `gh_canvas(action='info', id='...')` to see params |
| "Type mismatch" | Incompatible types | `gh_wire(action='validate', ...)` first; add conversion component |

## Data Tree Errors

| Symptom | Cause | Fix |
|---------|-------|-----|
| N×M expected, got max(N,M) | Both inputs flat | Graft one input |
| Unexpected combinations | Mismatched structures | Align with Graft/Flatten |
| Wrong item count | Access mode mismatch | Check Item vs List mode |

See `gh://docs/data-trees` for details.

## Solver Errors

| Error | Cause | Fix |
|-------|-------|-----|
| Not responding / loop | Cyclic dependency | `gh_document(action='solver', enabled=false)`, fix cycle |
| "Solver is disabled" | Not re-enabled | `gh_document(action='solver', enabled=true)` |

## Cluster Editor Errors

| Error | Cause | Fix |
|-------|-------|-----|
| All cluster inputs null after recompute | Native F5/recompute destroys input hooks | Use `gh_document(action='recompute')` instead — it is cluster-safe |
| Cluster inputs lost after re-enabling solver | `NewSolution(true)` expires input hooks | Use `gh_document(action='solver', enabled=true)` — it is cluster-safe |

**CRITICAL:** When working inside a cluster editor, NEVER use Grasshopper's native F5/recompute button — it will destroy all cluster input data (all inputs become null). This is a Grasshopper architectural limitation. Always use `gh_document(action='recompute')` which is cluster-safe. If inputs are lost, close and reopen the cluster editor to recover.

## Script Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Compilation failed" | Syntax error | `gh_inspect(action='reports')` for details |
| "Type not found" | Missing import | C#: `using Rhino.Geometry;` / Python: `import Rhino.Geometry as rg` |

## Other Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Protected: required for MCP" | Modifying Cordyceps component | Cannot modify; work with other components |
| "No active document" | GH/Rhino not ready | Ensure document open, `gh_document(action='info')` |
| "No bakeable outputs" | Non-geometry component | Only geometry types can bake |
| "Render timed out" | Slow render | Increase timeout or reduce complexity |
