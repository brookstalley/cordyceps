# Common Errors

## Component Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Component not found" | ID doesn't exist | `gh_canvas(action='list')` (optionally with `typeFilter`/`group`), or `gh_canvas(action='find', nickname='...')` — find matches default nicknames like 'Circle'; no need to rename components |
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
| Connections lost after `gh_script set` | Param renamed or removed | Check `lostConnections` in response, re-wire with `gh_wire(action='connect')` |
| "Can not determine input code language" | Directive-less body set on a bare unified **Script** component, which has no language until one is given | `gh_script(set)` preserves an existing directive automatically; for a bare Script component with no language yet it now returns a `languageWarning` in the response. Start your `code` with `#! python 3` (or `// #! csharp`) as line 1 and call `set` again — this also recovers a component already in this state. (The dedicated `C# Script` / `Python 3 Script` components carry a concrete language and don't need a directive.) |
| "Cannot set source on 'X': the component's code input parameter is visible" | The component exposes its code as a wired input parameter, so its source is driven by that wire and cannot be assigned directly | Disconnect and remove the component's code input parameter, then retry `gh_script(action='set')` — or feed the code through that parameter with `gh_wire` instead. Nothing was written; the component is unchanged. |
| "Cannot set source on 'X': no writable source member was found" | The component has neither a `SetSource(string)` method nor a writable `Code` property — it is probably not a script component, or its script API is one Cordyceps cannot write to | Confirm the target with `gh_script(action='info')`; the response's `componentType` names what was actually resolved. Reading source may still work even when writing does not. |
| `configure` returns `success:false` with `codeSet:false` and a `sourceError` | The parameter changes were applied but the source write failed | The component's params reflect the new configuration and its code is unchanged — a detectable partial apply. Fix the cause named in `sourceError`, then call `gh_script(action='set')` with just the code. |

## Type Marshaling

Numeric parameters (e.g., `x`, `y`, `lens`, `wait`, `timeout`, `azimuth`) accept both JSON numbers (`300`) and string-encoded numbers (`"300"`). The server coerces automatically. If you see "Cannot convert String to int/double", the value is not a valid number.

## Other Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Protected: required for MCP" | Modifying Cordyceps component | Cannot modify; work with other components |
| "No active document" | GH/Rhino not ready | Ensure document open, `gh_document(action='info')` |
| "No bakeable outputs" | Non-geometry component | Only geometry types can bake |
| "Render timed out" | Slow render | Increase timeout or reduce complexity |
| "Document is busy: another operation held the ... document lock for more than N seconds" | A prior request is running a long operation, or the Rhino UI thread is wedged (e.g. an infinite-loop script component) | Wait and retry; if it persists, a script component is likely stuck — fix or remove it, and restart Rhino if needed. Operations are serialized on the single Rhino UI thread, so only one runs at a time. |
| "MCP server is shutting down; the request was not processed" | The Cordyceps component was removed (or its port changed) while this request was in flight | Transient — the server stopped. Re-place the component (or restore its port) and reconnect, then retry. |
