# Radial Array Pattern

Creates N copies of geometry arranged in a circle around a center point.

## Use Cases
- Columns around a circular building
- Bolt holes in a flange
- Petals of a flower
- Spokes of a wheel
- Any circular/rotational symmetry

## Parameters
| Parameter | Type | Typical Range | Description |
|-----------|------|---------------|-------------|
| Count | Integer | 2-36 | Number of copies around the circle |
| Radius | Number | 0-1000 | Distance from center to each copy |
| Center | Point | (0,0,0) | Center of rotation |
| Axis | Vector | Z-axis | Rotation axis |

## Components Required

### Inputs (3 sliders)
1. **Count Slider** - Integer, e.g., 2<8<16
2. **Radius Slider** - Distance from center, e.g., 0<100<500
3. **(Optional) Height Slider** - For 3D geometry like cylinders

### Math & Angles (4 components)
1. **Panel "2"** - Constant for 2π calculation
2. **Pi** - Multiply by 2 to get full rotation (2π radians)
3. **Division** - Calculate angle step: 2π ÷ Count
4. **Series** - Generate Count angles: Start=0, Step=AngleStep, Count=Count

### Geometry (5-6 components)
1. **Construct Point** - Base point at (Radius, 0, 0)
2. **Construct Point** - Origin at (0, 0, 0) for rotation center
3. **Unit Z** - Z-axis vector for rotation
4. **Rotate 3D** - Rotate base point by each angle around Z-axis
5. **XY Plane** - Create horizontal planes at rotated points
6. **[Your Geometry]** - Circle, Cylinder, Box, etc. at each plane

## Connection Diagram

```
[Count]─────────┬─────────────────────────────► [Series: Count]
                │
[Panel: 2]──────┼──► [Pi] ──► [Division: A] ──► [Series: Step]
                │              ▲
                └──────────────┘ (Division: B)

[Panel: 0]─────────────────────────────────────► [Series: Start]
                │
                ├──► [Origin: X, Y, Z]
                │
[Radius]────────┴──► [Base Point: X]
                     [Panel: 0 → Y, Z]

[Panel: 1]─────────────────────────────────────► [Unit Z: Factor]

[Base Point] ──► [Rotate 3D: Geometry]
[Series] ──────► [Rotate 3D: Angle]
[Origin] ──────► [Rotate 3D: Center]
[Unit Z] ──────► [Rotate 3D: Axis]

[Rotate 3D] ───► [XY Plane: Origin] ───► [Cylinder/Circle/etc.: Base]
```

## Recommended Layout

```
x=50          x=250        x=400         x=550         x=750         x=900
┌─────────────────────────────────────────────────────────────────────────┐
│ INPUTS      │ MATH        │ SERIES      │ CONSTRUCT   │ TRANSFORM   │ OUTPUT
│             │             │             │             │             │
│ [Count]─────┼─► [Pi]──────┼─► [Div]─────┼─► [Series]──┼─────────────┼────┐
│ y=50        │   y=50      │   y=50      │   y=50      │             │    │
│             │             │             │             │             │    │
│ [Diameter]──┼─► [Div/2]───┼─────────────┼─────────────┼─────────────┼──┐ │
│ y=120       │   y=120     │             │             │             │  │ │
│             │             │             │             │             │  │ │
│ [Offset]────┼─────────────┼─────────────┼─► [BasePt]──┼─► [Rot3D]───┼─►│ [Cylinder]
│ y=190       │             │             │   y=190     │   y=190     │  │ y=190
│             │             │             │             │   ▲         │  │
│             │             │             │   [Origin]──┼───┤         │  │
│             │             │             │   y=260     │   │         │  │
│ [Panel:2]───┼──►          │             │             │   │         │  │
│ y=280       │             │             │   [UnitZ]───┼───┘         │  │
│ [Panel:0]───┼──►          │             │   y=330     │             │  │
│ y=340       │             │             │             │   [XYPlane]─┼──┘
│ [Panel:1]───┼──►          │             │             │   y=190     │
│ y=400       │             │             │             │             │
└─────────────────────────────────────────────────────────────────────────┘
```

## Variations

### Partial Arc (not full circle)
Replace `Panel: 2` with a slider for the arc fraction:
- Full circle: factor = 2 (gives 2π)
- Half circle: factor = 1 (gives π)
- Quarter: factor = 0.5 (gives π/2)

### Helical Array
Add height variation:
- Connect a Series to Z-coordinate of base point
- Each rotation level is higher than the previous

### Variable Radius
Use Graph Mapper or Series for radius:
- Inner elements smaller, outer larger
- Or oscillating radius for wave pattern

## Common Mistakes

1. **Using Range instead of Series**: Range gives N+1 values (includes both endpoints), causing overlap at start/end. Series gives exactly N values.

2. **Forgetting the origin point**: Rotate 3D needs a center point. If omitted, rotation happens around world origin which may not be what you want.

3. **Wrong angle units**: Pi component outputs radians. Don't multiply by degrees conversion unless you're using a degrees-based rotation.

4. **Count slider as float**: Use integer slider for count, otherwise Series may produce unexpected results.
