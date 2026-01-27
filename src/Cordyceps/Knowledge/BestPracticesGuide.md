# Grasshopper Best Practices

## Essential Practices

### 1. Disable Solver During Bulk Operations

```
set_solver_enabled(false)
// Add components, configure, connect
set_solver_enabled(true)  // Single recompute
```

Each operation triggers full recompute. Disabling prevents dozens of unnecessary solves.

### 2. Name Your Components

```
add_component(type: "Number Slider", x: 100, y: 200, nickname: "InputRadius")
```

Benefits: readable `get_all_components`, understandable errors, `get_component_by_nickname` lookup.

### 3. Validate Before Connecting

```
validate_connection(sourceId, sourceParam, targetId, targetParam)
// If valid, then connect
```

Failed connections don't error—they create disconnected inputs causing confusing downstream failures.

### 4. Check Status After Changes

```
get_canvas_status()
```

Shows errors (red), warnings (orange), disconnected inputs, runtime messages.

### 5. Match Data Structures Before Connecting

Check both inputs' tree structures. Add Graft/Flatten/Path Mapper if needed. See `gh://docs/data-trees`.

## Anti-Patterns

| Don't | Do Instead |
|-------|------------|
| Guess component names | `search_components(query)` first |
| Ignore orange warnings | Check `get_canvas_status()` |
| Chain without checking results | Verify `result.success` |
| Flatten everything | Understand structure, use Graft/Shift Path |
| Create cycles (A→B→C→A) | Design as DAG (directed acyclic graph) |

## Naming Conventions

| Component Type | Name After |
|----------------|------------|
| Sliders | What they control: "Radius", "Height" |
| Panels | Data source: "Points from File" |
| Scripts | Function: "Calculate Area" |
| Groups | Purpose: "Geometry Generation" |

## Expensive Operations

Cache these with Data Dam:
- Boolean operations (Solid Union/Difference)
- Mesh operations on dense meshes
- Kangaroo simulations
- Curve/surface intersections

## Debugging Strategy

1. **Isolate**: Disconnect suspected components
2. **Visualize**: Add Panels at each stage
3. **Check structure**: `get_component_outputs` for branch/item counts
4. **Trace upstream**: `trace_data_flow(id, "upstream")`
5. **Read messages**: `get_canvas_status()` runtime messages

## Script Components

1. Set input types explicitly via `configure_script_component`
2. Set access modes: Item for singles, List for collections
3. Handle null inputs
4. Use Report output for debugging

## Error Recovery

| Error | Fix |
|-------|-----|
| "Component not found" | Check spelling, use `search_components`, use GUID |
| "Connection failed" | Verify IDs exist, check params with `get_component_info` |
| Solver error/loop | Check for cycles, disable solver, fix, re-enable |
| Canvas unresponsive | `set_solver_enabled(false)`, fix problem, re-enable |
