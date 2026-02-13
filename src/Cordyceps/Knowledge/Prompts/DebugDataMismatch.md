# Debugging Data Tree Mismatch

Data tree mismatches are the most common source of Grasshopper errors. Follow this diagnostic workflow:

## Step 1: Identify Problem Components
```
gh_inspect(action='status')
```
Look for components with ERROR or WARNING status.

## Step 2: Check Component Outputs
For each problem component, examine its output structure:
```
gh_inspect(action='outputs', id=[component_id])
```
Note the `branchCount` and `dataCount` for each output.

## Step 3: Trace Upstream
Find what's feeding the problem component:
```
gh_inspect(action='trace', id=[component_id], direction='upstream')
```

## Step 4: Compare Tree Structures
For each upstream component:
```
gh_inspect(action='outputs', id=[upstream_id])
```
Compare branch counts. Mismatched branch counts cause cross-reference behavior.

## Step 5: Common Fixes

### If one input has more branches than another:
- **Graft** the simpler input to match structure
- Or **Flatten** both to ignore structure (destructive)

### If getting unexpected combinations:
- Check if you need to **Graft** an input
- Consider using **Path Mapper** for complex restructuring

### If data is in wrong order:
- Use **Flip Matrix** to swap rows/columns
- Or **Shift Path** to adjust depth

## Step 6: Verify Fix
After making changes:
```
gh_inspect(action='status')
gh_inspect(action='outputs', id=[component_id])
```