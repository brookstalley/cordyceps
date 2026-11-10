# Grid Array

2D or 3D grid of geometry.

## Parameters

| Param | Type | Range |
|-------|------|-------|
| X/Y Count | Integer | 1-50 |
| X/Y Spacing | Number | 1-1000 |

## Option A: Built-in (Simplest)

```
XY Plane → Rectangular (Plane)
X Count → Rectangular (Extent X)
Y Count → Rectangular (Extent Y)
Cell Size → Rectangular (Size X) and (Size Y)
→ Cells, Points output
```

The rectangular grid component is named `Rectangular` (Vector/Grid): `type='Rectangular'`.
Port names in parentheses are the exact input names to pass as `targetParam` (nicknames `P`, `Ex`,
`Ey`, `Sx`, `Sy` work too); partial names such as `X` are refused rather than guessed.

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
| Hexagonal | `Hexagonal` component (Vector/Grid), or offset alternate rows |
| Centered | Range: -Width/2 to Width/2 |

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Diagonal line, not grid | Cross Reference → Holistic mode |
| Off-by-one errors | 5×5 points = 4×4 cells |
| Slow (100×100) | Use mesh vertices instead |
