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
