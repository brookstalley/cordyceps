# Linear Array Pattern

Creates N copies of geometry arranged in a straight line along a direction vector.

## Use Cases
- Fence posts along a path
- Steps in a staircase
- Repeated structural elements
- Evenly spaced objects along an axis

## Parameters
| Parameter | Type | Typical Range | Description |
|-----------|------|---------------|-------------|
| Count | Integer | 2-100 | Number of copies |
| Spacing | Number | 1-1000 | Distance between copies |
| Direction | Vector | X, Y, or Z | Direction of array |
| Start | Point | Any | Starting position |

## Components Required

### Inputs (2-3 sliders)
1. **Count Slider** - Integer, e.g., 1<10<50
2. **Spacing Slider** - Distance between copies, e.g., 0<100<500
3. **(Optional) Start Point** - Or use origin (0,0,0)

### Math (2-3 components)
1. **Series** - Generate Count values: Start=0, Step=Spacing, Count=Count
2. **Unit X/Y/Z** - Direction vector (or custom vector)
3. **(Optional) Multiplication** - Scale direction by series values

### Geometry (2-3 components)
1. **Move** - Translate geometry by offset vectors
2. **[Your Geometry]** - The object to array

## Simple Approach: Series + Move

The simplest linear array uses Series to generate distances, then Move:

```
[Count]────────────────────► [Series: Count]
[Spacing]──────────────────► [Series: Step]
[Panel: 0]─────────────────► [Series: Start]

[Series]───────────────────► [Unit X: Factor]
[Unit X]───────────────────► [Move: Motion]
[Geometry]─────────────────► [Move: Geometry]
```

## Connection Diagram

```
                                              ┌──────────────────┐
[Count Slider]──────────────────────────────► │ Series           │
                                              │   Count ◄────────┤
[Spacing Slider]────────────────────────────► │   Step  ◄────────┤
                                              │   Start ◄──[0]   │
[Panel: 0]──────────────────────────────────► │                  │
                                              │   Series ────────┼──► [Unit X: Factor]
                                              └──────────────────┘         │
                                                                           ▼
┌──────────────┐                              ┌──────────────────┐   ┌──────────┐
│ Base         │                              │ Move             │   │ Unit X   │
│ Geometry     │─────────────────────────────►│   Geometry       │◄──┤  Vector  │
│ (Circle,     │                              │   Motion    ◄────┼───┤          │
│  Box, etc.)  │                              │                  │   └──────────┘
└──────────────┘                              │   Geometry ──────┼──► [Output]
                                              └──────────────────┘
```

## Recommended Layout

```
x=50          x=200        x=350         x=500         x=650
┌───────────────────────────────────────────────────────────────┐
│ INPUTS      │ SERIES      │ VECTOR      │ TRANSFORM   │ OUTPUT
│             │             │             │             │
│ [Count]─────┼─► [Series]──┼─► [Unit X]──┼─► [Move]────┼─► [Result]
│ y=50        │   y=50      │   y=50      │   y=50      │   y=50
│             │             │             │   ▲         │
│ [Spacing]───┼─►           │             │   │         │
│ y=120       │             │             │   │         │
│             │             │             │   │         │
│ [Panel: 0]──┼─►           │             │   │         │
│ y=190       │             │             │   │         │
│             │             │ [Geometry]──┼───┘         │
│             │             │ y=120       │             │
└───────────────────────────────────────────────────────────────┘
```

## Variations

### Bidirectional Array
Array in positive and negative directions:
- Use Range instead of Series with domain (-Count/2, Count/2)
- Center element is at origin

### Along a Curve
Replace linear direction with curve tangents:
- Divide Curve → Points and Tangents
- Use tangent vectors for orientation
- Move geometry to each point

### With Rotation
Add rotation at each step:
- Series for angles (0 to total rotation)
- Rotate each copy by its corresponding angle

### Variable Spacing
Non-uniform spacing:
- Replace Series with custom list
- Or use Graph Mapper to modulate spacing

## Common Mistakes

1. **Confusing Step and Count**: Series Step is the increment, Count is how many values. For 5 objects 100 apart: Step=100, Count=5 (gives 0, 100, 200, 300, 400).

2. **Wrong vector scaling**: Unit vectors have length 1. Multiply by distance to get actual offset.

3. **Starting at wrong position**: Series starts at 0 by default. First copy is at origin. If you want first copy at distance Spacing, set Start=Spacing.

4. **Forgetting data matching**: If geometry is single item and Series produces list, Move creates multiple copies automatically (good!). But if both are lists of different lengths, unexpected cross-matching occurs.

## Example: Fence Posts

Create fence posts along X-axis:
```
Inputs:
- Post Count: 10
- Post Spacing: 150
- Post Diameter: 20
- Post Height: 200

Components:
[Count: 10]──► [Series]──► [Unit X]──► [Move]──► Output
[Spacing: 150]──►              │          ▲
[0]────────────►               │          │
                               │     [Cylinder]
                               │     Base: XY Plane
                               │     Radius: 10 (Diameter/2)
                               │     Length: 200
                               └─────────►│
```

## Performance Note

For large counts (100+), consider:
- Using Mesh instead of Brep for geometry
- Baking intermediate results
- Using Data Dam to cache heavy computations
