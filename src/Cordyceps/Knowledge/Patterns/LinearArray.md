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

## Implementation with Cordyceps

```
# 1. Disable solver
gh_document(action='solver', enabled=false)

# 2. Add components
gh_canvas(action='add', type='Number Slider', x=50, y=50, nickname='Count')
gh_canvas(action='add', type='Number Slider', x=50, y=120, nickname='Spacing')
gh_canvas(action='add', type='Series', x=250, y=80)
gh_canvas(action='add', type='Unit X', x=400, y=80)
gh_canvas(action='add', type='Move', x=550, y=80)

# 3. Configure sliders
gh_canvas(action='config', id='<count-id>', min=2, max=50, value=10)
gh_canvas(action='config', id='<spacing-id>', min=1, max=100, value=10)

# 4. Wire connections
gh_wire(action='connect', connections='[...]')

# 5. Enable solver and verify
gh_document(action='solver', enabled=true)
gh_inspect(action='status')
```
