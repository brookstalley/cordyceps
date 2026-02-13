# Optimizing Grasshopper Canvas Performance

## Step 1: Get Current Status
```
gh_inspect(action='status')
gh_canvas(action='list')
```
Count the total components and identify complex areas.

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
gh_inspect(action='trace', id=[component_id], direction='upstream')
gh_inspect(action='outputs', id=[component_id])
```

Check if data structures are unnecessarily complex.

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