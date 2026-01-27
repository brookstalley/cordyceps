# Grasshopper Best Practices Guide

## Essential Practices

### 1. Disable Solver During Bulk Operations

Grasshopper recomputes the entire document after each change. When adding multiple components:

```
set_solver_enabled(enabled: false)
// Add 10 components, configure them, create connections
set_solver_enabled(enabled: true)  // Single recompute at the end
```

**Why:** Each `add_component` or `connect_components` triggers a full solution. Disabling the solver prevents dozens of unnecessary recomputes.

### 2. Name Your Components

GUIDs are unreadable. Set nicknames when creating components:

```
add_component(type: "Number Slider", x: 100, y: 200, nickname: "InputRadius")
```

Or rename existing components:
```
rename_component(id: "abc-123...", nickname: "InputRadius")
```

**Benefits:**
- `get_all_components` returns readable names
- `get_canvas_status` errors are understandable
- `get_component_by_nickname` allows direct lookup by name
- Connection logs make sense

### 3. Validate Before Connecting

Always check compatibility before creating connections:

```
validate_connection(sourceId, sourceParam, targetId, targetParam)
// If valid, then:
connect_components(...)
```

**Why:** Failed connections don't error - they just create disconnected inputs that cause confusing downstream failures.

### 4. Check Status After Changes

After any sequence of operations:

```
get_canvas_status()
```

This reveals:
- Components with errors (red)
- Components with warnings (orange)
- Disconnected required inputs
- Runtime messages explaining issues

### 5. Understand Data Matching Before Connecting

Before connecting two components with different data structures:

1. Check the output structure (use Panel to visualize)
2. Check what the input expects (Item, List, or Tree)
3. Add Graft/Flatten/Path Mapper if structures don't match

**See:** `gh://docs/data-trees` for complete data tree documentation

## Anti-Patterns to Avoid

### Don't Guess at Component Names

Bad:
```
add_component(type: "circle")  // Might find wrong component
```

Good:
```
search_components(query: "circle")  // See what's available
// Then use exact name or GUID from results
add_component(type: "Circle CNR")
```

### Don't Ignore Warnings

Orange components have warnings that often predict failures:
- "No data" - upstream component produced nothing
- "Invalid data" - type conversion issue
- "Tree mismatch" - data matching problem

Use `get_canvas_status()` to see all warnings.

### Don't Chain Without Checking

Bad:
```
add_component(...)
add_component(...)
connect_components(...)  // What if first add_component failed?
```

Good:
```
result = add_component(...)
if result.success:
    // continue
else:
    // handle error
```

### Don't Flatten Everything

Flattening destroys data relationships. Instead:
- Understand why structures don't match
- Use Graft to add structure
- Use Shift Path to align depths
- Use Path Mapper for complex restructuring

### Don't Create Circular References

Grasshopper doesn't allow cycles. If A→B→C, you cannot connect C→A. Plan your data flow as a directed acyclic graph (DAG).

## Component Organization

### Logical Grouping

Use `create_group` to visually organize:
```
create_group(name: "Input Parameters")
add_to_group(groupId: ..., componentIds: [...slider IDs...])
```

### Left-to-Right Flow

Standard convention: inputs on left, outputs on right. Data flows left→right. This makes definitions readable.

### Naming Conventions

- **Sliders:** Name after what they control ("Radius", "Height")
- **Panels:** Name after data source ("Points from File")
- **Script components:** Name after function ("Calculate Area")
- **Groups:** Name after purpose ("Geometry Generation")

## Performance Optimization

### Expensive Operations

These components are computationally expensive:
- **Boolean operations** (Solid Union, Solid Difference)
- **Mesh operations** (especially on dense meshes)
- **Kangaroo simulations** (iterative solver)
- **Intersections** (curve/surface/brep intersections)

**Tip:** Cache results of expensive operations using Data Dam component.

### Data Reduction

Before expensive operations:
- Simplify curves (Rebuild Curve)
- Reduce mesh density
- Limit point counts with Cull Pattern or subsampling

### Parallel Processing

Some operations benefit from Grasshopper's parallel solver:
- Enable in Grasshopper: Solution → Parallel Computing
- Most math operations auto-parallelize
- Custom scripts need explicit parallel implementation

## Debugging Strategy

### 1. Isolate the Problem

Disconnect suspected problem components. Does the error persist?

### 2. Add Visualization

Insert Panel components to see data at each stage:
- Before the error
- At the error
- After each transformation

### 3. Check Data Structure

Use `get_component_outputs` to see:
- Branch count
- Item count per branch
- Data preview

### 4. Trace Data Flow

Use `trace_data_flow(id, "upstream")` to see all components feeding into the problem.

### 5. Check Component Status

`get_canvas_status()` shows runtime messages for every component. Read the error messages - they often explain exactly what's wrong.

## Common Patterns

### Input Parameter Pattern
```
Number Slider → feeds → Component Input
Panel → displays → Component Output
```

### Geometry Generation Pattern
```
Point → feeds → Circle (or other primitive)
Circle → feeds → Extrude
Extrude → feeds → Cap Holes
```

### Conditional Pattern
```
Boolean Toggle → feeds → Stream Filter (Gate input)
Option A → feeds → Stream Filter input 0
Option B → feeds → Stream Filter input 1
Stream Filter → outputs → selected option
```

### List Manipulation Pattern
```
List → feeds → List Item (to extract one)
List → feeds → Cull Pattern (to filter)
List → feeds → Sort (to reorder)
```

## Script Component Guidelines

When using C# or Python script components:

1. **Set input types explicitly** - Use type hints in `configure_script_component`
2. **Set access modes correctly** - Item for single values, List for collections
3. **Handle null inputs** - Check for null before processing
4. **Output useful errors** - Use Report output for debugging
5. **Keep scripts focused** - One purpose per script component

Example configuration:
```
configure_script_component(
  id: "...",
  inputs: [
    {"name": "Points", "type": "Point3d", "access": "list"},
    {"name": "Radius", "type": "double", "access": "item"}
  ],
  outputs: [
    {"name": "Circles", "type": "Circle"}
  ],
  fullSource: "..."
)
```

## Recovery from Errors

### "Component not found"
- Check spelling and capitalization
- Use `search_components` to find correct name
- Use GUID instead of name for reliability

### "Connection failed"
- Verify both component IDs exist
- Check parameter names with `get_component_info`
- Validate type compatibility first

### "Solver error" or endless loop
- Check for circular references
- Look for components feeding themselves
- Disable solver, fix issues, re-enable

### Canvas becomes unresponsive
- Disable solver: `set_solver_enabled(false)`
- Fix the problem
- Re-enable solver
