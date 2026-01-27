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

| Spacing Type | Pixels |
|--------------|--------|
| Between columns | 150 minimum |
| After sliders | 200 (sliders are wide) |
| Vertical between sliders | 70 |
| Vertical between components | 70-100 |
| Between groups | 50 |

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
2. Add inputs at x=50, y=50/120/190... (70px gaps)
3. Add processing at x=250, 400, 550...
4. Use `get_component_bounds` for precise positioning
5. Wire with `bulk_connect`
6. `set_solver_enabled(true)`
7. `get_canvas_status()` then `validate_layout()`
8. `add_to_group` for organization
9. Use `auto_space_components(mode="flow")` if layout needs fixing
