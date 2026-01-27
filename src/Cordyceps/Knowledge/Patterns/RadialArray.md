# Radial Array Pattern

Creates N copies of geometry arranged in a circle around a center point.

## Parameters

| Parameter | Type | Typical Range |
|-----------|------|---------------|
| Count | Integer | 2-36 |
| Radius | Number | 0-1000 |
| Center | Point | (0,0,0) |
| Axis | Vector | Z-axis |

## Component Chain

```
[Count] → [Series: Count]
[Panel: 2] → [Pi] → [Division: A] → [Series: Step]
[Count] ────────────→ [Division: B]
[Panel: 0] → [Series: Start]

[Radius] → [Base Point: X]
[Panel: 0] → [Base Point: Y, Z]
[Panel: 0] → [Origin: X, Y, Z]
[Panel: 1] → [Unit Z: Factor]

[Base Point] → [Rotate 3D: Geometry]
[Series] → [Rotate 3D: Angle]
[Origin] → [Rotate 3D: Center]
[Unit Z] → [Rotate 3D: Axis]

[Rotate 3D] → [XY Plane: Origin] → [Cylinder/Circle: Base]
```

## Key Points

- Full rotation = 2π radians. Use `Panel: 2 → Pi` to get 2π.
- Series gives exactly N values (not N+1 like Range)
- Rotation uses radians, not degrees
- For oriented geometry (cylinders), use XY Plane at rotated points

## Variations

| Variation | Change |
|-----------|--------|
| Partial arc | Replace `Panel: 2` with slider (0.5 = quarter, 1 = half) |
| Helical | Add Z offset per rotation level via Series |
| Variable radius | Use Graph Mapper or Series for radius values |

## Common Mistakes

| Mistake | Result | Fix |
|---------|--------|-----|
| Using Range instead of Series | N+1 values, overlap at start/end | Use Series |
| Missing rotation center | Rotates around world origin | Connect Origin point to Rotate 3D |
| Degrees instead of radians | Wrong angles | Pi component outputs radians |
| Float count slider | Unexpected Series results | Use integer slider |
