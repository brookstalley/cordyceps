# Canvas Layout Guide

## Core Concept: Pivot vs Bounds

When placing a component at (x, y), that coordinate is the **pivot point**, not the top-left corner.

| Component Type | Pivot Location | Typical Bounds |
|----------------|----------------|----------------|
| Number Slider | Left edge | 200×20, extends right |
| Standard component | Center | 80-120×40-80 |
| Panel | Top-left | 100-200×50-100, expands with content |

**Key rule**: Slider at x=50 has right edge at ~x=250. Next column starts at x=250+gap, not x=50+gap.

## Component Dimensions

| Component | Width | Height |
|-----------|-------|--------|
| Number Slider | 150-250 | 20 |
| Panel | 100-200 | 50-100 |
| Toggle | 80 | 20 |
| Value List | 120 | 30 |
| Simple component (1-2 I/O) | 80-100 | 40 |
| Medium component (3-4 I/O) | 100-120 | 50-60 |
| Complex component (5+) | 120-150 | 60-80 |
| Group | Adds ~40px padding | Adds ~40px padding |

## Spacing Rules

**Goal: Avoid backwards wires, minimize horizontal spread, prefer vertical stacking when readable.**

| Spacing Type | Pixels | Notes |
|--------------|--------|-------|
| Horizontal between columns | 60-80 | ~1x component width; just enough to avoid overlap |
| After sliders | 60-80 | Sliders are wide but wire to their right edge |
| Vertical between stacked inputs | 70 | Sliders, panels at same X should stack vertically |
| Vertical between components | 50-70 | Tighter than horizontal |
| Between groups | 30 |  |

**Key principle**: Horizontal spacing should be just enough to prevent overlapping—about 1-1.5x a typical component's width (60-80px). Excessive horizontal spacing makes definitions hard to read.

## Standard Layout

| Column | X Position | Contents |
|--------|------------|----------|
| Inputs | 50 | Sliders, parameters |
| Constants | 50-150 | Panels with 0, 1, Pi |
| Processing 1 | 250 | First operations |
| Processing 2+ | 400, 550, 700... | +150 per column |

| Row | Y Position | Contents |
|-----|------------|----------|
| Primary flow | 50-150 | Main data path |
| Secondary | 200-300 | Branches |
| Constants | 300+ | Helper values |

## Handling Constants

**Preferred**: Use component defaults (many components default to 0 or standard values)
- `Construct Point` → (0,0,0)
- `XY Plane` → World XY at origin
- `Unit X/Y/Z` → factor 1

**When explicit constants needed**: Create panels at y=250+ with clear nicknames ("Zero", "One", "Pi")

## Using Bounds from Responses

Spatial operations return bounds:
```
{x, y, width, height, right, bottom}
```

Calculate next position: `next_x = previous.right + 150`

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Component at x=100 after slider at x=50 | Use x=250+ (slider needs ~200 width) |
| Vertical gap of 20px | Use 70px minimum |
| Overlapping groups | Check bounds, leave 50px between |
| Assuming panel stays small | Panels expand; leave extra space |

## Workflow

1. `set_solver_enabled(false)`
2. Add inputs at x=50, y=50/120/190... (70px vertical gaps, stacked)
3. Add processing columns at x=300, 380, 460... (60-80px gaps)
4. Wire with `bulk_connect`
5. `set_solver_enabled(true)`
6. `get_canvas_status()` then `validate_layout()`
7. `add_to_group` for organization

## Fixing Overlaps

**Best approach**: Place components correctly initially using the spacing rules above. Use `validate_layout()` to detect any problems, then use `move_component()` to fix specific overlaps manually.
