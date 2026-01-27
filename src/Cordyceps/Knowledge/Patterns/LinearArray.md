# Linear Array Pattern

Creates N copies of geometry arranged in a straight line.

## Parameters

| Parameter | Type | Typical Range |
|-----------|------|---------------|
| Count | Integer | 2-100 |
| Spacing | Number | 1-1000 |
| Direction | Vector | X, Y, or Z |

## Component Chain

```
[Count] → [Series: Count]
[Spacing] → [Series: Step]
[Panel: 0] → [Series: Start]

[Series] → [Unit X/Y/Z: Factor] → [Move: Motion]
[Geometry] → [Move: Geometry] → [Output]
```

## Key Points

- Series outputs distances: 0, spacing, 2×spacing, 3×spacing...
- Unit vectors have length 1; Series values scale them
- First copy is at origin (offset 0). For offset start, set Series Start = Spacing.
- Single geometry + list of vectors → Move creates multiple copies automatically

## Variations

| Variation | Change |
|-----------|--------|
| Bidirectional | Use Range with domain (-count/2, count/2) |
| Along curve | Divide Curve → use points and tangents |
| With rotation | Add Series for angles, chain Rotate after Move |
| Variable spacing | Replace Series with custom list or Graph Mapper |

## Common Mistakes

| Mistake | Result | Fix |
|---------|--------|-----|
| Confusing Step vs Count | Wrong number or spacing | Step = increment, Count = quantity |
| Forgetting vector scaling | Objects at 0, 1, 2... not 0, 100, 200 | Series values multiply unit vector |
| Both geometry and vectors as lists | Cross-product combinations | Keep geometry as single item |
