# Creating Parametric {geometry_type}

Follow these steps to create parametric geometry with adjustable inputs:

## Step 1: Disable Solver
```
gh_document(action='solver', enabled='false')
```
This prevents recomputation after each step.

## Step 2: Create Input Parameters
Add Number Sliders for each parameter you need:
```
gh_canvas(action='add', type='Number Slider', x=50, y=50)
gh_canvas(action='config', id=[slider_id], min=0, max=100, value=50)
```
Don't rename the sliders — group them with a label instead once they're placed:
```
gh_canvas(action='group_create', name='Parameters', ids='[slider_ids]')
```

## Step 3: Create Geometry Component
Based on your geometry type, add the appropriate component:
- Circle: `gh_canvas(action='add', type='Circle', x=300, y=50)`
- Box: `gh_canvas(action='add', type='Box', x=300, y=50)`
- Curve: `gh_canvas(action='add', type='Interpolate', x=300, y=50)`

## Step 4: Create Connections
Connect sliders to geometry inputs:
```
gh_wire(action='connect', sourceId=[slider_id], sourceParam='0', targetId=[geometry_id], targetParam='Radius')
```

## Step 5: Enable Solver and Verify
```
gh_document(action='solver', enabled='true')
gh_inspect(action='status')
```

Check for any errors or warnings in the status output.