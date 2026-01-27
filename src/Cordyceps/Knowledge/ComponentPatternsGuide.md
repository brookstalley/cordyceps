# Common Component Patterns Guide

## Input Patterns

### Numeric Input
```
Number Slider → [component expecting Number]
```
Configuration: `set_component_value(id, "0.0<0.5<1.0")` for range 0-1, default 0.5

### Integer Input
```
Integer Slider or Number Slider (step 1) → [component expecting Integer]
```

### Boolean Input
```
Boolean Toggle → [component expecting Boolean]
```
Or use Panel with "True"/"False"

### Point Input
```
Construct Point (X, Y, Z sliders) → [component expecting Point]
```
Or: Panel with "0,0,0" format

### Multiple Points
```
Panel (one point per line) → [component expecting Point list]
```
Format: Each line is "x,y,z"

## Geometry Creation Patterns

### Circle from Parameters
```
XY Plane → Circle (Plane input)
Number Slider → Circle (Radius input)
Circle → [downstream geometry]
```

### Grid of Points
```
Series (count=10, step=1) → Cross Reference (A)
Series (count=10, step=1) → Cross Reference (B)
Cross Reference → Construct Point (X from A, Y from B, Z=0)
```

### Rectangular Grid
```
Number Slider (X count) → Square Grid
Number Slider (Y count) → Square Grid
Number Slider (cell size) → Square Grid
Square Grid Points → [downstream]
```

### Curve from Points
```
Points (list) → Polyline (for straight segments)
Points (list) → Interpolate Curve (for smooth curve)
Points (list) → Nurbs Curve (for NURBS control)
```

### Surface from Curves
```
Curve (list) → Loft
Loft → Surface (or Brep)
```

### Solid from Surface
```
Surface → Extrude (direction vector)
Extrude → Cap Holes
Cap Holes → Solid Brep
```

## Data Manipulation Patterns

### Combine Multiple Lists
```
List A → Merge (D1)
List B → Merge (D2)
List C → Merge (D3)  // Add more inputs with manage_zoomable_inputs
Merge → Combined output
```

### Filter List by Pattern
```
List → Cull Pattern
Boolean Pattern [true,true,false] → Cull Pattern
Cull Pattern → Filtered list
```

### Select Single Item
```
List → List Item
Integer (index) → List Item
List Item → Single item
```

### Reverse Order
```
List → Reverse List
Reverse List → Reversed list
```

### Sort Items
```
List → Sort List
Keys (numbers) → Sort List
Sort List → Sorted list
```

### Repeat Pattern
```
Items → Repeat Data
Count → Repeat Data
Repeat Data → Repeated sequence
```

## Conditional Patterns

### If-Then-Else (Stream Filter)
```
Boolean → Stream Filter (Gate)
Option A → Stream Filter (0)
Option B → Stream Filter (1)
Stream Filter → Selected option
```

### Multiple Options (Stream Gate)
```
Integer (0,1,2,3) → Stream Gate
Options A,B,C,D → Stream Gate inputs
Stream Gate → Selected option
```

### Filter by Condition
```
Numbers → Larger Than (compare to threshold)
Numbers → Cull Pattern (use comparison result)
Cull Pattern → Filtered numbers
```

## Transformation Patterns

### Move Geometry
```
Geometry → Move
Vector (direction) → Move
Move → Moved geometry
```

### Rotate Geometry
```
Geometry → Rotate
Angle (radians) → Rotate
Plane (rotation axis) → Rotate
Rotate → Rotated geometry
```

### Scale Geometry
```
Geometry → Scale
Scale Factor → Scale
Center Point → Scale
Scale → Scaled geometry
```

### Mirror Geometry
```
Geometry → Mirror
Plane (mirror plane) → Mirror
Mirror → Mirrored geometry
```

### Array Linear
```
Geometry → Move
Unit Vector → Move (direction)
Moved → Move (chain multiple)
All versions → Merge → Array result
```
Or use Series + Move pattern

### Array Radial
```
Geometry → Rotate
Series (0 to 2*PI, step=angle) → Rotate
All rotations → collected as array
```

## Analysis Patterns

### Curve Length
```
Curve → Length
Length → Number output
```

### Surface Area
```
Surface/Brep → Area
Area → Number output
```

### Bounding Box
```
Geometry → Bounding Box
Bounding Box → Box output + dimensions
```

### Distance Between Points
```
Point A → Distance
Point B → Distance
Distance → Number output
```

### Curve Evaluation
```
Curve → Evaluate Curve
Parameter (0-1) → Evaluate Curve
Evaluate Curve → Point, Tangent, etc.
```

## Script Patterns

### Basic C# Script
```csharp
// Input: List<Point3d> points, double radius
// Output: List<Circle> circles

var result = new List<Circle>();
foreach(var pt in points)
{
    result.Add(new Circle(Plane.WorldXY, pt, radius));
}
circles = result;
```

### Basic Python Script
```python
# Input: points (list), radius (item)
# Output: circles

import Rhino.Geometry as rg

circles = []
for pt in points:
    plane = rg.Plane(pt, rg.Vector3d.ZAxis)
    circles.append(rg.Circle(plane, radius))
```

### Script with Error Handling
```csharp
// Use the 'out' parameter for report output
if(input == null)
{
    Report = "Error: No input provided";
    return;
}
// ... processing
Report = $"Processed {count} items successfully";
```

## Performance Patterns

### Data Dam (Cache Results)
```
Expensive Operation → Data Dam → [downstream]
```
Data Dam only updates when you click it, caching expensive results.

### Simplify Before Boolean
```
Complex Curves → Rebuild Curve (reduce points)
Rebuilt Curves → Loft
Loft → Boolean Operation (faster now)
```

### Batch Processing
```
All Input Data → Process Component → All Output Data
```
Rather than processing one item at a time with multiple component instances.

## Common Component Reference

### Primitives
- **Point**: Construct Point, Point XYZ
- **Vector**: Unit X, Unit Y, Unit Z, Vector XYZ
- **Plane**: XY Plane, XZ Plane, Construct Plane
- **Line**: Line, Line SDL
- **Circle**: Circle, Circle CNR
- **Rectangle**: Rectangle, Rectangle 2Pt

### Curves
- **Polyline**: Polyline, Polygon
- **Interpolate**: Interpolate, Nurbs Curve
- **Arc**: Arc, Arc 3Pt
- **Ellipse**: Ellipse

### Surfaces
- **Loft**: Loft
- **Extrude**: Extrude, Extrude Point
- **Sweep**: Sweep1, Sweep2
- **Revolution**: Revolution

### Solids
- **Cap**: Cap Holes
- **Boolean**: Solid Union, Solid Difference, Solid Intersection
- **Box**: Box, Center Box

### Data
- **Merge**: Merge (combine lists)
- **Graft**: Graft Tree (add branch per item)
- **Flatten**: Flatten Tree (collapse to single list)
- **Path Mapper**: Path Mapper (complex restructuring)
- **Sort**: Sort List
- **Cull**: Cull Pattern, Cull Index

### Math
- **Addition**: Addition (+)
- **Multiplication**: Multiplication (*)
- **Division**: Division (/)
- **Modulus**: Modulus (%)
- **Series**: Series (generate sequence)
- **Range**: Range (generate range)
- **Random**: Random (random numbers)
