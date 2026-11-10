# Component Patterns

**Writing these names.** Component names below are Grasshopper's own; pass them to
`gh_canvas(action='add', type=...)` exactly (case is ignored, partial names are not matched). Where a
component shares its name with a parameter — Circle, Line, Arc, Rectangle, Point — add the category:
`type='Curve/Circle'`. A name in parentheses after a component, as in "Circle (Radius)", is the input
port to wire; pass that name or its nickname as `targetParam`. When a name is refused, the error lists
the candidates or near names, each with a `use` spelling that works.

## Input Patterns

| Input Type | Components | Notes |
|------------|------------|-------|
| Number | Number Slider | `gh_canvas(action='config', id='...', min=0, max=1, value=0.5)` |
| Integer | Number Slider | Configure with `decimals=0` (`action='config'`) |
| Boolean | Boolean Toggle | Or Panel with "True"/"False" |
| Point | Construct Point | Or Panel with "x,y,z" |
| Multiple Points | Panel | One "x,y,z" per line |

## Geometry Creation

| Goal | Pattern |
|------|---------|
| Circle | XY Plane → Circle (Plane); Slider → Circle (Radius) |
| Point Grid | Series → Cross Reference (Holistic mode) → Construct Point |
| Curve from Points | Points → Polyline (straight) or Interpolate (smooth) |
| Surface from Curves | Curves → Loft |
| Solid from Surface | Surface → Extrude → Cap Holes |

## Data Operations

| Goal | Pattern |
|------|---------|
| Combine lists | Merge (add inputs via zoomable) |
| Filter by pattern | List + Boolean Pattern → Cull Pattern |
| Select item | List + Index → List Item |
| Repeat | Items + Count → Repeat Data |

## Conditionals

| Goal | Pattern |
|------|---------|
| If-else | Boolean → Stream Filter; Options → inputs 0,1 |
| Switch (N options) | Integer → Stream Gate; Options → inputs |
| Filter numbers | Numbers → Larger Than → Cull Pattern |

## Transforms

| Transform | Inputs |
|-----------|--------|
| Move | Geometry, Motion |
| Rotate (`Transform/Rotate`; Vector has its own Rotate) | Geometry, Angle (radians), Plane |
| Scale | Geometry, Factor, Center |
| Mirror | Geometry, Plane |

**Linear array**: Geometry + Series → Unit Vector → Move
**Radial array**: Geometry + Series (0 to 2π) → Rotate

## Analysis

| Measurement | Component |
|-------------|-----------|
| Curve length | Length |
| Surface area | Area |
| Bounding box | Bounding Box |
| Point distance | Distance |
| Curve point at t | Evaluate Curve (t=0-1) |

## Script Components

Use `gh_script(action='info', id='...')` to inspect existing scripts.

**C# template:**
```csharp
// Inputs: points (List<Point3d>), radius (double)
var circles = new List<Circle>();
foreach(var pt in points)
    circles.Add(new Circle(Plane.WorldXY, pt, radius));
A = circles;  // Output
```

**Python template:**
```python
import Rhino.Geometry as rg
circles = [rg.Circle(rg.Plane(pt, rg.Vector3d.ZAxis), radius) for pt in points]
a = circles  # Output
```

> Language is preserved automatically on `gh_script(set/configure)` — plain bodies (no `#! python 3` / `// #! csharp` first line) keep the component's existing language.

## Performance

- **Data Dam**: Cache expensive operations (booleans, mesh ops, simulations)
- **Rebuild Curve**: Simplify before boolean operations
- **Batch processing**: Single component with list input, not multiple instances

## Common Components

**Primitives**: Construct Point, Unit X / Unit Y / Unit Z, XY Plane, Curve/Line, Curve/Circle, Curve/Rectangle

**Curves**: Polyline, Interpolate, Curve/Arc, Nurbs Curve

**Surfaces**: Loft, Extrude, Sweep1, Revolution

**Solids**: Cap Holes, Solid Union / Solid Difference / Solid Intersection, Center Box

**Data**: Merge, Graft Tree, Flatten Tree, Path Mapper, Sort List, Cull Pattern

**Math**: Addition, Subtraction, Multiplication, Division, Modulus, Series, Range, Random
