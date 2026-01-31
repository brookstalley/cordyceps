# Grid Array Pattern

Creates a 2D or 3D grid of geometry with controllable spacing.

## Parameters

| Parameter | Type | Typical Range |
|-----------|------|---------------|
| X Count | Integer | 1-50 |
| Y Count | Integer | 1-50 |
| X Spacing | Number | 1-1000 |
| Y Spacing | Number | 1-1000 |

## Option A: Built-in Component (Simplest)

```
[XY Plane] → [Rectangular Grid: Plane]
[X Count] → [Rectangular Grid: X Count]
[Y Count] → [Rectangular Grid: Y Count]
[X Spacing] → [Rectangular Grid: X Size]
[Y Spacing] → [Rectangular Grid: Y Size]
→ Points, Cells output
```

## Option B: Cross Reference (More Control)

```
[X Count] → [Series X: Count]
[X Spacing] → [Series X: Step]
[Panel: 0] → [Series X: Start]

[Y Count] → [Series Y: Count]
[Y Spacing] → [Series Y: Step]
[Panel: 0] → [Series Y: Start]

[Series X] → [Cross Reference: A] → [Construct Point: X]
[Series Y] → [Cross Reference: B] → [Construct Point: Y]
[Panel: 0] → [Construct Point: Z]
→ Grid Points
```

**Important**: Set Cross Reference to "Holistic" mode for full grid. Default "Longest" mode gives diagonal, not grid.

## Key Points

- 5×5 points = 4×4 cells (point count ≠ cell count)
- Cross Reference default mode pairs by index (wrong). Use Holistic for grid.
- For 3D grid, chain two Cross Reference components

## Variations

| Variation | Change |
|-----------|--------|
| 3D grid | Add Series Z, chain second Cross Reference |
| Hexagonal | Use Hexagonal Grid component, or offset alternate rows |
| Centered | X range: -Width/2 to Width/2 |
| Variable density | Replace Series with custom lists |

## Common Mistakes

| Mistake | Result | Fix |
|---------|--------|-----|
| Wrong Cross Reference mode | Diagonal line, not grid | Set to "Holistic" |
| Confusing points vs cells | Off-by-one errors | 5×5 points = 4×4 cells |
| Large grids (100×100) | Slow performance | Use mesh vertices instead |

## Implementation with Cordyceps

```
# 1. Disable solver
gh_document(action='solver', enabled=false)

# 2. Add components using Option A (Rectangular Grid)
gh_canvas(action='add', type='XY Plane', x=50, y=100)
gh_canvas(action='add', type='Number Slider', x=50, y=170, nickname='X Count')
gh_canvas(action='add', type='Number Slider', x=50, y=240, nickname='Y Count')
gh_canvas(action='add', type='Number Slider', x=50, y=310, nickname='Cell Size')
gh_canvas(action='add', type='Rectangular', x=350, y=200)

# 3. Configure sliders
gh_canvas(action='config', id='<x-count>', min=1, max=20, value=5)
gh_canvas(action='config', id='<y-count>', min=1, max=20, value=5)
gh_canvas(action='config', id='<cell-size>', min=1, max=50, value=10)

# 4. Wire connections
gh_wire(action='connect', connections='[...]')

# 5. Enable solver and verify
gh_document(action='solver', enabled=true)
gh_inspect(action='status')
gh_canvas(action='validate')
```
