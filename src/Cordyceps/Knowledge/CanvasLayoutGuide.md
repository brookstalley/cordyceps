# Canvas Layout

## Pivot vs Bounds

Position (x,y) is the **pivot point**, not top-left corner.

| Component | Pivot | Typical Size |
|-----------|-------|--------------|
| Number Slider | Left edge | 200×20, extends right |
| Standard component | Center | 80-120 wide |
| Panel | Top-left | Expands with content |

**Key**: Slider at x=50 extends to ~x=250. Next column starts at x=300+, not x=130.

## Component Sizes

| Component | Width | Height |
|-----------|-------|--------|
| Number Slider | 150-250 | 20 |
| Panel | 100-200 | 50-100 |
| Toggle | 80 | 20 |
| Simple component | 80-100 | 40 |
| Medium (3-4 I/O) | 100-120 | 50-60 |
| Complex (5+) | 120-150 | 60-80 |

## Spacing

| Type | Pixels |
|------|--------|
| Horizontal between columns | 150 |
| Vertical between stacked inputs | 70 |
| Vertical between components | 50-70 |
| Between groups | 30 |

**Goal**: Avoid backwards wires. Stack vertically. Keep columns readable — 150px gaps leave room for wires.

## Standard Layout

| Column | X | Contents |
|--------|---|----------|
| Inputs | 50 | Sliders, parameters (stacked y=50, 120, 190...) |
| Processing | 300, 450, 600... | Operations (+150 per column) |

## Constants

Prefer component defaults (Construct Point → 0,0,0; XY Plane → origin).
When explicit: Panel at y=250+ with nickname ("Zero", "Pi").

## Using Bounds

`gh_canvas(action='bounds', id='...')` returns `bounds` (`{x, y, width, height}`) plus `pivot` (`{x, y}`).
`list`/`find`/`info` responses include only the pivot `x`/`y`, not the bounding box.
Next column position: `x = bounds.x + bounds.width + 150`

## Workflow

1. `gh_document(action='solver', enabled=false)`
2. Inputs at x=50, y=50/120/190 (70px vertical gaps)
3. Processing at x=300, 450, 600 (150px horizontal gaps)
4. Wire: `gh_wire(action='connect', connections='[...]')`
5. `gh_document(action='solver', enabled=true)`
6. `gh_canvas(action='validate')` — check overlaps

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| x=100 after slider at x=50 | Use x=300+ (sliders are ~200 wide) |
| Vertical gap of 20px | Use 70px minimum |
| Overlapping groups | Check bounds, leave 50px between |
