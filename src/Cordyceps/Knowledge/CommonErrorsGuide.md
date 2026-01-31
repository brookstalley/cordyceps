# Common Errors and Solutions

Quick reference for resolving the most frequent issues when working with Cordyceps and Grasshopper.

## Component Errors

### "Component not found"

**Cause:** The component ID (GUID) doesn't exist in the current document.

**Solutions:**
1. List current components: `gh_canvas(action='list')`
2. Search by name: `gh_canvas(action='find', nickname='...')`
3. The component may have been deleted or the ID is from a different session

### "Unknown component type"

**Cause:** The component name doesn't match any installed component.

**Solutions:**
1. Search for correct name: `gh_canvas(action='search', query='...')`
2. Use GUID for guaranteed accuracy
3. Use `Category/Name` format for disambiguation (e.g., `Curve/Circle`)

### "Ambiguous component name"

**Cause:** Multiple components share the same name.

**Solutions:**
1. Use the full path: `Category/Subcategory/Name`
2. Use the component GUID from search results
3. Prefer non-deprecated components (check `deprecated` field)

## Connection Errors

### "Source output not found" / "Target input not found"

**Cause:** The parameter name or index doesn't exist on the component.

**Solutions:**
1. Check available parameters: `gh_canvas(action='info', id='...')`
2. Use parameter index (0-based) instead of name
3. Parameter names are case-insensitive but must match exactly

### "Connection failed - type mismatch"

**Cause:** The output type cannot convert to the input type.

**Solutions:**
1. Validate before connecting: `gh_wire(action='validate', sourceId='...', targetId='...')`
2. Check type compatibility in the validation response
3. Consider adding type conversion components (e.g., `Curve` to convert geometry to curve)

## Data Tree Errors

### "Data tree mismatch" / Unexpected output count

**Cause:** Inputs have different branch structures, causing cross-reference behavior.

**Solutions:**
1. Check structures: `gh_inspect(action='outputs', id='...')`
2. Trace the data flow: `gh_inspect(action='trace', id='...', direction='upstream')`
3. Add `Graft` to match branch counts, or `Flatten` to simplify

See `gh://docs/data-trees` for comprehensive data tree guidance.

## Solver Errors

### Solver not responding / Infinite loop

**Cause:** Cyclic dependencies or extremely heavy computation.

**Solutions:**
1. Disable solver: `gh_document(action='solver', enabled='false')`
2. Check for cycles: `gh_inspect(action='trace', id='...', direction='downstream')`
3. Look for components feeding into their own inputs

### "Solver is disabled"

**Cause:** Solver was disabled and not re-enabled.

**Solution:** `gh_document(action='solver', enabled='true')`

## Protected Component Errors

### "Protected: required for MCP server"

**Cause:** Attempting to modify or delete the Cordyceps infrastructure component.

**Solution:** The Cordyceps component is required for MCP communication and cannot be modified. Work with other components on the canvas.

## Canvas Errors

### "No active Grasshopper document"

**Cause:** No Grasshopper file is open or the canvas is not active.

**Solutions:**
1. Ensure Grasshopper is running with a document open
2. Check: `gh_document(action='info')`

### "No active Grasshopper canvas"

**Cause:** The Grasshopper window may be minimized or not focused.

**Solution:** Ensure the Grasshopper canvas window is open and visible.

## Script Component Errors

### "Script compilation failed"

**Cause:** Syntax errors in C# or Python code.

**Solutions:**
1. Check reports: `gh_inspect(action='reports')`
2. Get current code: `gh_script(action='get', id='...')`
3. Review error messages for line numbers and details

### "Type not found" in script

**Cause:** Missing using/import statements or incorrect type names.

**Common fixes:**
- C#: Add `using Rhino.Geometry;`
- Python: Add `import Rhino.Geometry as rg`

## Baking Errors

### "Component has no bakeable outputs"

**Cause:** The component doesn't produce geometry that can be baked.

**Solutions:**
1. Check component outputs: `gh_canvas(action='info', id='...')`
2. Only geometry types (curves, surfaces, meshes, etc.) can be baked
3. Panels, sliders, and data components cannot be baked

### "No active Rhino document"

**Cause:** Rhino doesn't have an active document.

**Solution:** Open or create a Rhino document before baking.

## Rendering Errors

### "Render timed out"

**Cause:** Render took longer than the specified timeout.

**Solutions:**
1. Increase timeout: `rhino_render(action='render', timeout='60')`
2. Reduce scene complexity
3. Use simpler materials or lower resolution

## Best Practices to Avoid Errors

1. **Disable solver during bulk operations:**
   ```
   gh_document(action='solver', enabled='false')
   // ... bulk operations ...
   gh_document(action='solver', enabled='true')
   ```

2. **Validate before connecting:**
   ```
   gh_wire(action='validate', sourceId='...', targetId='...')
   ```

3. **Check status after changes:**
   ```
   gh_inspect(action='status')
   ```

4. **Search before adding:**
   ```
   gh_canvas(action='search', query='...')
   ```

5. **Use snapshots before risky operations:**
   ```
   gh_document(action='snapshot', name='before-changes')
   // ... make changes ...
   // If needed: gh_document(action='revert', name='before-changes')
   ```
