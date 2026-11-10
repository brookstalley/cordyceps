# Debugging Data Tree Mismatch

Data tree mismatches are the most common source of Grasshopper errors. Follow this diagnostic workflow:

## Step 1: Identify Problem Components
```
gh_inspect(action='status')
```
Look for components with ERROR or WARNING status.

## Step 2: Check What the Component Receives
For each problem component, read the shape of every input, without pulling the data:
```
gh_inspect(action='tree', id=[component_id], side='input')
```
Each tree lists `branches`, `items`, `depth`, `perBranch` and its `paths`. When two inputs with
several branches each disagree, a note names them: `inputs don't line up: A has 3 branches, B has
12 branches`. A tree with `solved: false` carries a `note` saying why; its counts may be out of date.

## Step 3: Trace Upstream
Find what's feeding the problem component:
```
gh_canvas(action='outline', id=[component_id])
```
or, for the full chain:
```
gh_inspect(action='trace', id=[component_id], direction='upstream')
```

## Step 4: Compare Tree Structures
Read the upstream outputs in one call:
```
gh_inspect(action='tree', ids='["<upstream_id_1>", "<upstream_id_2>"]')
```
Compare branch counts and paths. Mismatched branch counts cause cross-reference behavior. Use
`gh_inspect(action='outputs', id=...)` when you need the values themselves.

## Step 5: Common Fixes

### If one input has more branches than another:
- **Graft** the simpler input to match structure
- Or **Flatten** both to ignore structure (destructive)

### If getting unexpected combinations:
- Check if you need to **Graft** an input
- Consider using **Path Mapper** for complex restructuring

### Applying Flatten / Graft
Set the modifier **on the port itself** — that is how these are written by hand, and it
keeps the canvas clean:

```
gh_canvas(action='modifier', id='<guid>', side='input', param='P', mapping='graft')
```

Read what is already applied with `gh_canvas(action='modifier', id=..., side=..., param=...)`,
or see every port at once in `gh_canvas(action='info')` under each parameter's `modifiers`.
Inserting `Graft Tree` / `Flatten Tree` components still works and is the right choice when
the restructuring needs to be visible as a step in the definition.

### If data is in wrong order:
- Use **Flip Matrix** to swap rows/columns
- Or **Shift Path** to adjust depth

## Step 6: Verify Fix
After making changes:
```
gh_inspect(action='status')
gh_inspect(action='tree', id=[component_id])
```