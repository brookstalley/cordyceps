# Type System

Grasshopper wraps data in "Goo" containers with automatic conversion. Some conversions work; others fail or lose data.

## Primitive Types

| Type | Description | Sources |
|------|-------------|---------|
| Number | Double | Number Slider, math ops |
| Integer | Whole number | Integer Slider, Series |
| Boolean | True/False | Toggle, comparisons |
| Text | String | Panel, Concatenate |
| Colour | ARGB | Colour Swatch, Gradient |
| Domain | Range (min,max) | Construct Domain |

## Geometry Types

| Type | Description | Components |
|------|-------------|------------|
| Point | 3D (x,y,z) | Construct Point, Divide Curve |
| Vector | Direction | Unit X/Y/Z, Amplitude |
| Plane | Origin + axes | XY Plane, Construct Plane |
| Line | Start→End | Line SDL, Line |
| Circle/Arc | Curved primitives | Circle, Arc 3Pt |
| Curve | Any curve | Polyline, Interpolate |
| Surface | NURBS surface | Loft, Extrude |
| Brep | Solid | Cap, Boolean ops |
| Mesh | Polygon mesh | Mesh Box, Mesh Surface |

## Automatic Conversions

**Safe (lossless):**
- Integer → Number
- Point ↔ Vector (coords as direction)
- Line/Circle/Arc → Curve
- Surface → Brep
- Any geometry → Geometry

**Lossy:**
- Number → Integer (truncates)
- Curve → Line/Circle (only if actually is one)
- Brep → Surface (only if single surface)

**Fails:**
- Number → Point (need 3 coords)
- Text → Geometry
- Curve → Surface
- Mesh ↔ Brep

## Script Type Names

| Script | Grasshopper |
|--------|-------------|
| `int` | Integer |
| `double` | Number |
| `bool` | Boolean |
| `string` | Text |
| `Point3d` | Point |
| `Vector3d` | Vector |
| `Plane` | Plane |
| `Curve` | Curve |
| `Surface` | Surface |
| `Brep` | Brep |
| `Mesh` | Mesh |

## Type Checking

1. `gh_canvas(action='info', id='...')` — see input/output types
2. `gh_wire(action='validate', ...)` — test connection compatibility

## Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Data conversion failed" | Incompatible types | Add conversion component |
| "Null geometry" | Silent failure | Check upstream for errors |
| "List access violation" | Item vs List mismatch | Check access mode, use Flatten/Graft |
| Empty output | Conversion returned nothing | Verify input type |

## Geometry Validity

| Type | Requirements |
|------|--------------|
| Curve | Continuous, non-self-intersecting for some ops |
| Brep | Must be closed for booleans |
| Mesh | Manifold for some operations |
