# Grasshopper Type System Guide

## Overview

Grasshopper wraps all data in "Goo" containers that handle type conversion. When you connect outputs to inputs, Grasshopper attempts automatic conversion. Some conversions work seamlessly; others fail or lose information.

## Primitive Types

| Type | Description | Common Sources |
|------|-------------|----------------|
| **Number** | Double-precision float | Number Slider, Math operations |
| **Integer** | Whole numbers | Integer Slider, Series with step 1 |
| **Boolean** | True/False | Toggle, Comparison operators |
| **Text** | String data | Panel, Concatenate |
| **Colour** | ARGB color | Colour Swatch, Gradient |
| **Domain** | Number range (min to max) | Construct Domain |
| **Domain²** | 2D domain (U and V ranges) | Construct Domain² |

## Geometry Types

| Type | Description | Common Components |
|------|-------------|-------------------|
| **Point** | 3D point (x,y,z) | Construct Point, Divide Curve |
| **Vector** | 3D direction | Unit X/Y/Z, Amplitude |
| **Plane** | Origin + X,Y axes | XY Plane, Construct Plane |
| **Line** | Start point to end point | Line SDL, Line |
| **Circle** | Plane + radius | Circle, Circle CNR |
| **Arc** | Circular arc | Arc, Arc 3Pt |
| **Curve** | Any curve type | Polyline, Interpolate |
| **Surface** | NURBS surface | Loft, Extrude |
| **Brep** | Boundary representation solid | Cap, BooleanUnion |
| **Mesh** | Polygon mesh | Mesh Box, Mesh Surface |
| **Box** | Axis-aligned box | Box, Bounding Box |
| **Transform** | 4x4 transformation matrix | Move, Rotate, Scale |

## Type Hierarchy and Conversion

### Automatic Conversions (Usually Safe)

```
Integer → Number        (lossless)
Point → Vector          (uses coordinates as direction)
Line → Curve            (line is a type of curve)
Circle → Curve          (circle is a type of curve)
Arc → Curve             (arc is a type of curve)
Surface → Brep          (surface wrapped as brep)
Mesh → Geometry         (mesh is geometry)
Brep → Geometry         (brep is geometry)
Curve → Geometry        (curve is geometry)
```

### Conversions That May Lose Information

```
Number → Integer        (rounds/truncates)
Curve → Line            (only if curve IS a line)
Brep → Surface          (only if single surface)
Geometry → specific     (only if correct type)
```

### Conversions That Will Fail

```
Number → Point          (need 3 coordinates)
Text → Geometry         (incompatible)
Curve → Surface         (different dimensions)
Mesh → Brep             (different representations)
Boolean → Number        (use conditional instead)
```

## Common Type Compatibility

### Point-Related
- **Point → Vector**: Works (uses XYZ as direction from origin)
- **Vector → Point**: Works (uses XYZ as coordinates)
- **Number → Point**: FAILS (need Construct Point with 3 numbers)
- **Text → Point**: FAILS (parse manually if needed)

### Curve-Related
- **Line, Circle, Arc, Polyline → Curve**: Always works
- **Curve → Line**: Only works if the curve IS a line
- **Curve → Circle**: Only works if the curve IS a circle
- **Surface edge → Curve**: Works via Brep Edges

### Surface-Related
- **Surface → Brep**: Always works (wraps as single-face brep)
- **Brep → Surface**: Only works if single untrimmed surface
- **Mesh → Surface**: FAILS (different representation)
- **Loft curves → Surface**: Use Loft component

### Numeric
- **Integer → Number**: Always works
- **Number → Integer**: Works but truncates decimals
- **Boolean → Integer**: Use Conditional or gate

## Parameter Type Hints in Scripts

When creating script components, use these type names:

| Script Type | Grasshopper Parameter |
|-------------|----------------------|
| `int` | Integer |
| `double` | Number |
| `bool` | Boolean |
| `string` | Text |
| `Point3d` | Point |
| `Vector3d` | Vector |
| `Plane` | Plane |
| `Line` | Line |
| `Circle` | Circle |
| `Curve` | Curve |
| `Surface` | Surface |
| `Brep` | Brep |
| `Mesh` | Mesh |
| `Color` | Colour |
| `Transform` | Transform |

## Type Checking Strategy

Before connecting components:

1. **Check output type** via `get_component_info` or `gh://component/{name}`
2. **Check input expected type** same way
3. **Verify compatibility** using the tables above
4. **Use `validate_connection`** to confirm before connecting

## Common Type Errors and Fixes

### "Data conversion failed"
**Cause:** Incompatible types
**Fix:** Add a conversion component between them

### "Null geometry"
**Cause:** Geometry operation failed silently
**Fix:** Check input validity, look for orange/red components upstream

### "List access violation"
**Cause:** Component expected single item, got list (or vice versa)
**Fix:** Check access mode, use Flatten/Graft as needed

### Empty output
**Cause:** Type conversion returned nothing
**Fix:** Verify input type, may need explicit conversion

## Geometry Validity

Some operations require valid geometry:

| Type | Validity Requirements |
|------|----------------------|
| Curve | Must be continuous, not self-intersecting for some ops |
| Surface | Must be properly parameterized |
| Brep | Must be closed for boolean operations |
| Mesh | Must be manifold for some operations |

Check validity with:
- **Brep**: Check `IsSolid`, `IsValid`
- **Mesh**: Check for naked edges, non-manifold edges
- **Curve**: Check `IsClosed`, `IsPlanar` as needed

## Best Practices

1. **Be explicit about types** - Don't rely on implicit conversion
2. **Check connections first** - Use `validate_connection`
3. **Add conversion components** - Make data flow clear
4. **Handle failures** - Some conversions return null; check for errors
5. **Match list structures** - Type conversion applies per-item; tree mismatches compound problems
