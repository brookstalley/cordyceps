# Geometry Orientation

**Most oriented geometry uses the plane's Z-axis as its primary direction.**

## Orientation by Component

| Component | Z-axis | XY plane |
|-----------|--------|----------|
| Cylinder | Extends along | — |
| Cone | Extends along (tip at origin) | — |
| Circle | — | Lies flat in |
| Rectangle | — | Lies flat in |
| Text 3D | Faces along | Lies flat in |
| Extrude | Uses Vector input, not plane | — |

## Plane Construction

Plane = Origin + X + Y + Z axes. Z = X × Y (cross product).

| Need | Use | Set |
|------|-----|-----|
| Geometry pointing direction D | Plane Normal | Normal = D |
| Geometry pointing up (world Z) | XY Plane | (automatic) |
| Full axis control | Construct Plane | Ensure X × Y = D |

## Decision Tree

```
Care about rotation around direction axis?
├─ NO → Plane Normal (direction as Normal)
└─ YES → Construct Plane (Z = X × Y must = direction)
```

## Common Mistake

**Wrong**: Vector2Pt (toward target) → Construct Plane (X-axis) → Cylinder
**Result**: Cylinders perpendicular to target

**Right**: Vector2Pt (toward target) → Plane Normal (Normal) → Cylinder
**Result**: Cylinders point toward target

## Patterns

| Goal | Pattern |
|------|---------|
| Cylinders toward point | Vector2Pt → Plane Normal → Cylinder |
| Cylinders standing vertical | Points → XY Plane (Origin) → Cylinder |
| Geometry on surface | Evaluate Surface → Frame output → Geometry |

## Verification

`gh_inspect(action='geometry', id='...')` → check bounding box.
Large extent in one axis = geometry extends along that axis.
