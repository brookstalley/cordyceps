# Grid Array Pattern

Creates a 2D or 3D grid of geometry with controllable spacing in each direction.

## Use Cases
- Building facades with repeated windows
- Waffle structures
- Point clouds for analysis
- Panelized surfaces
- Structural grids

## Parameters
| Parameter | Type | Typical Range | Description |
|-----------|------|---------------|-------------|
| X Count | Integer | 1-50 | Number of columns |
| Y Count | Integer | 1-50 | Number of rows |
| X Spacing | Number | 1-1000 | Horizontal spacing |
| Y Spacing | Number | 1-1000 | Vertical spacing |
| Base Plane | Plane | XY Plane | Grid orientation |

## Components Required

### Inputs (4 sliders)
1. **X Count Slider** - Columns, e.g., 1<5<20
2. **Y Count Slider** - Rows, e.g., 1<5<20
3. **X Spacing Slider** - e.g., 0<100<500
4. **Y Spacing Slider** - e.g., 0<100<500

### Option A: Built-in Grid Components
Grasshopper has built-in grid components:
1. **Rectangular Grid** - Creates points and cells directly
2. **Square Grid** - Uniform spacing version

### Option B: Manual Cross-Reference
For more control, use cross-reference:
1. **Series** (X) - Generate X positions
2. **Series** (Y) - Generate Y positions
3. **Cross Reference** - Combine X and Y
4. **Construct Point** - Create grid points

## Option A: Using Rectangular Grid

The simplest approach uses the built-in Rectangular Grid component:

```
[XY Plane]─────────────────► [Rect Grid: Plane]
[X Count]──────────────────► [Rect Grid: X Count]  ──► [Grid Points]
[Y Count]──────────────────► [Rect Grid: Y Count]  ──► [Grid Cells]
[X Spacing]────────────────► [Rect Grid: X Size]
[Y Spacing]────────────────► [Rect Grid: Y Size]
```

## Option B: Manual Cross-Reference

For more control over the grid structure:

```
┌──────────────────────────────────────────────────────────────┐
│ X SERIES                   │ Y SERIES                        │
│                            │                                 │
│ [X Count]──► [Series X]    │ [Y Count]──► [Series Y]         │
│ [X Spacing]──►  Count      │ [Y Spacing]──►  Count           │
│ [0]──────────►  Start      │ [0]──────────►  Start           │
│               Step  ───────┼─────────────────────────────────┤
└──────────────────────────┬─┴─────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ CROSS REFERENCE                                              │
│                                                              │
│ [Series X]──► [Cross Ref: List A] ──► X coordinates (flat)   │
│ [Series Y]──► [Cross Ref: List B] ──► Y coordinates (flat)   │
│                                                              │
│ Note: Set Cross Reference type to "Holistic" for full grid   │
└──────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ CONSTRUCT POINTS                                             │
│                                                              │
│ [Cross Ref A]──► [Construct Point: X]                        │
│ [Cross Ref B]──► [Construct Point: Y] ──► Grid Points        │
│ [0]────────────► [Construct Point: Z]                        │
└──────────────────────────────────────────────────────────────┘
```

## Recommended Layout (Manual Method)

```
x=50          x=200        x=350         x=500         x=650
┌───────────────────────────────────────────────────────────────┐
│ X INPUTS    │ X SERIES    │ CROSS REF   │ POINTS      │ OUTPUT
│             │             │             │             │
│ [X Count]───┼─► [SeriesX]─┼─► [CrossRef]┼─► [ConstPt]─┼─► [Geom]
│ y=50        │   y=50      │   y=80      │   y=80      │   y=80
│ [X Space]───┼─►           │   ▲         │   ▲         │
│ y=120       │             │   │         │   │         │
├─────────────┼─────────────┼───┼─────────┼───┼─────────┤
│ Y INPUTS    │ Y SERIES    │   │         │   │         │
│             │             │   │         │   │         │
│ [Y Count]───┼─► [SeriesY]─┼───┘         │   │         │
│ y=200       │   y=200     │             │   │         │
│ [Y Space]───┼─►           │             │   │         │
│ y=270       │             │             │   │         │
├─────────────┼─────────────┼─────────────┼───┼─────────┤
│ CONSTANTS   │             │             │   │         │
│             │             │             │   │         │
│ [Panel: 0]──┼─────────────┼─────────────┼───┘         │
│ y=350       │             │             │             │
└───────────────────────────────────────────────────────────────┘
```

## Data Tree Structure

Understanding the output structure:

### Rectangular Grid Output
- **Points**: Flat list or tree depending on settings
- **Cells**: Polylines for each grid cell

### Cross Reference Output
- Default (Longest): Pairs items by index
- **Holistic**: Full cross-product (what you usually want for grids)
- **Shortest**: Pairs up to shorter list length

For a 3×4 grid with Holistic cross-reference:
- X Series: [0, 100, 200]
- Y Series: [0, 100, 200, 300]
- Result: 12 points (3 × 4)

## Variations

### 3D Grid (Cubic)
Add Z dimension:
```
[Series X]──► [Cross Ref]──► [Cross Ref 2]──► Points
[Series Y]──►      ▲              ▲
                   │              │
[Series Z]─────────┴──────────────┘
```
Requires two Cross Reference components chained.

### Hexagonal Grid
Use built-in Hexagonal Grid component, or:
- Offset alternate rows by half spacing
- Use Cull Pattern to select odd/even rows

### Centered Grid
Center the grid on origin:
- X range: -Width/2 to Width/2
- Y range: -Height/2 to Height/2

### Variable Density
Non-uniform spacing:
- Replace Series with custom value lists
- Or use Gaussian/other distribution

## Common Mistakes

1. **Wrong Cross Reference mode**: Default "Longest" mode pairs by index, giving a diagonal line not a grid. Use "Holistic" for full grid.

2. **Tree structure confusion**: Cross Reference may produce tree structure. Flatten if you need a simple list of points.

3. **Performance with large grids**: 100×100 = 10,000 points. Consider using mesh vertices instead of individual points for very large grids.

4. **Cell vs Point count**: A 5×5 point grid has 4×4 = 16 cells. Don't confuse point count with cell count.

## Example: Window Grid on Facade

```
Parameters:
- Columns: 8
- Rows: 5
- H Spacing: 300 (3 meters)
- V Spacing: 350 (3.5 meters)
- Window Size: 200 × 250

Components:
[Columns: 8]──────► [Rect Grid]──► [Grid Points]
[Rows: 5]─────────►              │
[H Spacing: 300]──►              │
[V Spacing: 350]──►              └──► [XY Plane]──► [Rectangle]──► [Windows]
                                          ▲              ▲
                                          │              │
                                    [Width: 200]  [Height: 250]
```
