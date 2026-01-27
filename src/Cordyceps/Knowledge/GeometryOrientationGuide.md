# Geometry Orientation in Grasshopper

## Core Rule

**Most oriented geometry uses the plane's Z-axis as its primary direction.**

- Cylinder: extends along Z-axis from origin
- Cone: extends along Z-axis, tip at origin
- Circle: lies flat in XY plane
- Rectangle: lies flat in XY plane
- Text 3D: faces along Z-axis

## Plane Construction

A plane has Origin + X + Y + Z axes. Z is always computed as X × Y (cross product).

| If you need... | Use this component | Set this as direction |
|----------------|-------------------|----------------------|
| Geometry pointing in direction D | `Plane Normal` | Normal = D |
| Geometry pointing up (world Z) | `XY Plane` | (automatic) |
| Full control of all axes | `Construct Plane` | Ensure X × Y = D |

## Decision Tree: Creating Oriented Geometry

```
Q: Do you care about rotation around the direction axis?
├─ NO → Use `Plane Normal` with direction as Normal input
└─ YES → Use `Construct Plane` with specific X and Y axes
         (Z = X × Y must equal your desired direction)
```

## Common Mistakes and Fixes

| Mistake | Result | Fix |
|---------|--------|-----|
| Using direction as X-axis in `Construct Plane` | Geometry perpendicular to intended direction | Use `Plane Normal` instead, or ensure Z = direction |
| Forgetting Z = X × Y | Unexpected orientation | Verify: if you want Z pointing toward target, X and Y must be perpendicular to that direction |

## Patterns

### Cylinders pointing toward a point

**Wrong:**
```
Vector2Pt (toward target) → Construct Plane (X-axis) → Cylinder
Result: Cylinders perpendicular to target
```

**Right:**
```
Vector2Pt (toward target) → Plane Normal (Normal) → Cylinder
Result: Cylinders point toward target
```

### Cylinders standing vertical

```
Points → XY Plane (Origin) → Cylinder
Result: Cylinders extend upward (Z = world up)
```

### Geometry at angle to surface

```
Surface → Evaluate Surface → Frame output → [Geometry]
Result: Geometry oriented to surface normal
```

## Verifying Orientation

After creating geometry, use `get_geometry(componentId)` and check bounding box:

- Large extent in one axis = geometry extends along that axis
- For cylinder pointing toward origin: long axis of bbox should point toward (0,0,0)

**Quick check:** If bbox shows large Y extent but you expected large X extent, your plane orientation is wrong.

## Component Quick Reference

| Component | Direction axis | Flat surface |
|-----------|---------------|--------------|
| Cylinder | Z | - |
| Cone | Z (tip at origin) | - |
| Circle | - | XY |
| Rectangle | - | XY |
| Arc | - | XY |
| Text 3D | Z (facing) | XY |
| Extrude | Vector input (not plane) | - |
