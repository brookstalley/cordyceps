# Optimizing Grasshopper Canvas Performance

## Step 1: Get Current Status
```
gh_canvas(action='outline')
gh_inspect(action='status')
```
The outline's `summary` counts the objects; its group lines show item counts on the wires
between groups, which point at the areas carrying large data. `status` lists every error, warning and disconnected input, on components and on parameter objects such as sliders and panels.

## Step 2: Identify Expensive Operations

Common performance bottlenecks:
- Boolean operations (Solid Union, Difference)
- Mesh operations on dense meshes
- Many geometry intersections
- Large data trees with mismatched structures

Look for:
- Components with large output counts
- Deep nesting of transformations
- Repeated expensive operations

## Step 3: Analyze Data Flow
For suspected slow components:
```
gh_canvas(action='outline', id=[component_id])
gh_inspect(action='tree', id=[component_id])
```

Check if data structures are unnecessarily complex: `tree` reports branch and item counts and
notes such as redundant leading path indices, without pulling the data.

## Step 4: Optimization Strategies

### Reduce Data Before Expensive Operations
- Cull unnecessary items early in the pipeline
- Simplify curves before boolean operations
- Reduce mesh density when precision isn't needed

### Use Solver Management
```
gh_document(action='solver', enabled='false')
// Make multiple changes
gh_document(action='solver', enabled='true')
```

### Consider Data Caching
Add Data Dam components after expensive operations to cache results.

### Simplify Tree Structures
- Flatten data when branch structure isn't needed
- Use Simplify to remove redundant path levels

## Step 5: Verify Improvements
After optimizations:
```
gh_document(action='recompute')
gh_inspect(action='status')
```

Compare component status before and after changes.