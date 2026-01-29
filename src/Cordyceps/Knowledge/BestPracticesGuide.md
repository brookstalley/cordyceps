# Grasshopper Best Practices

## Essential Practices

### 1. Disable Solver During Bulk Operations

```
gh_document(action='solver', enabled=false)
// Add components, configure, connect
gh_document(action='solver', enabled=true)  // Single recompute
```

Each operation triggers full recompute. Disabling prevents dozens of unnecessary solves.

### 2. Name Your Components

```
gh_canvas(action='add', type='Number Slider', x=100, y=200, nickname='InputRadius')
```

Benefits: readable `gh_canvas(action='list')`, understandable errors, `gh_canvas(action='find', nickname='...')` lookup.

### 3. Validate Before Connecting

```
gh_wire(action='validate', sourceId='...', sourceParam='...', targetId='...', targetParam='...')
// If valid, then connect
```

Failed connections don't error—they create disconnected inputs causing confusing downstream failures.

### 4. Check Status After Changes

```
gh_inspect(action='status')
```

Shows errors (red), warnings (orange), disconnected inputs, runtime messages.

### 5. Match Data Structures Before Connecting

Check both inputs' tree structures. Add Graft/Flatten/Path Mapper if needed. See `gh://docs/data-trees`.

## Anti-Patterns

| Don't | Do Instead |
|-------|------------|
| Guess component names | `gh_canvas(action='search', query='...')` first |
| Ignore orange warnings | Check `gh_inspect(action='status')` |
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
3. **Check structure**: `gh_inspect(action='outputs', id='...')` for branch/item counts
4. **Trace upstream**: `gh_inspect(action='trace', id='...', direction='upstream')`
5. **Read messages**: `gh_inspect(action='status')` runtime messages

## Script Components

1. Set input types explicitly via `gh_script(action='configure', ...)`
2. Set access modes: Item for singles, List for collections
3. Handle null inputs
4. Use Report output for debugging
5. Use `gh_script(action='info', id='...')` to inspect existing scripts (source code, parameters, type hints)
6. Use `gh_script(action='get', id='...')` for quick source code retrieval

## Error Recovery

| Error | Fix |
|-------|-----|
| "Component not found" | Check spelling, use `gh_canvas(action='search')`, use GUID |
| "Connection failed" | Verify IDs exist, check params with `gh_canvas(action='info')` |
| Solver error/loop | Check for cycles, disable solver, fix, re-enable |
| Canvas unresponsive | `gh_document(action='solver', enabled=false)`, fix problem, re-enable |
