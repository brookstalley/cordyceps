# Grid Array

2D or 3D grid of geometry.

## Parameters

| Param | Type | Range |
|-------|------|-------|
| X/Y Count | Integer | 1-50 |
| X/Y Spacing | Number | 1-1000 |

## Option A: Built-in (Simplest)

```
XY Plane → Rectangular Grid (Plane)
X Count → Rectangular Grid (X)
Y Count → Rectangular Grid (Y)
Cell Size → Rectangular Grid (Size)
→ Points, Cells output
```

## Option B: Cross Reference (More Control)

```
X Count/Spacing → Series X
Y Count/Spacing → Series Y
Series X → Cross Reference (A) → Construct Point (X)
Series Y → Cross Reference (B) → Construct Point (Y)
0 → Construct Point (Z)
```

**Important**: Set Cross Reference to **Holistic** mode. Default "Longest" gives diagonal, not grid.

## Key Points

- 5×5 points = 4×4 cells (point count ≠ cell count)
- For 3D: chain second Cross Reference with Series Z

## Variations

| Variation | Change |
|-----------|--------|
| 3D grid | Add Series Z + second Cross Reference |
| Hexagonal | Hexagonal Grid component, or offset alternate rows |
| Centered | Range: -Width/2 to Width/2 |

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Diagonal line, not grid | Cross Reference → Holistic mode |
| Off-by-one errors | 5×5 points = 4×4 cells |
| Slow (100×100) | Use mesh vertices instead |
