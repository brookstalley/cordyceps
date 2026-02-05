# Linear Array

N copies along a straight line.

## Parameters

| Param | Type | Range |
|-------|------|-------|
| Count | Integer | 2-100 |
| Spacing | Number | 1-1000 |
| Direction | Vector | X, Y, or Z |

## Pattern

```
[Count] → Series (Count)
[Spacing] → Series (Step)
[0] → Series (Start)
Series → Unit X/Y/Z (Factor) → Move (Motion)
[Geometry] → Move → Output
```

## Key Points

- Series outputs: 0, spacing, 2×spacing, 3×spacing...
- Unit vectors have length 1; Series values scale them
- Single geometry + list of vectors → Move creates copies
- First copy at origin (offset 0). For offset start: Series Start = Spacing

## Variations

| Variation | Change |
|-----------|--------|
| Bidirectional | Range with domain (-count/2, count/2) |
| Along curve | Divide Curve → use points/tangents |
| With rotation | Chain Rotate after Move |
| Variable spacing | Replace Series with custom list |

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Wrong count/spacing | Step = increment, Count = quantity |
| Objects at 0,1,2 not 0,100,200 | Series values multiply unit vector |
| Cross-product combinations | Keep geometry as single item, not list |
