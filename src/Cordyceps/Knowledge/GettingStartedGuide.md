# Grasshopper via Cordyceps

Grasshopper is a visual dataflow graph for parametric 3D design. Components on a canvas connect via wires. Data flows left-to-right.

## Object Types

Three roles (check `role` field in responses):
- **component**: Processing node with inputs/outputs. Usually what you want.
- **parameter**: Data container (Params category). Holds geometry/values.
- **input**: Interactive control (Params/Input subcategory). Sliders, toggles.

**Ambiguous names**: "Circle" matches both Circle component (Curve/Primitive) and Circle parameter (Params/Geometry). On ambiguity, you get `ambiguous_name` error with all matches. Resolve with GUID or category-qualified name like `Curve/Circle`.

## Data Trees

Grasshopper uses hierarchical data trees with paths like `{0;0;1}`, not flat arrays. Mismatched tree structures cause cross-products. A 10-item list connected to a single-item input runs 10 operations. Read `gh://docs/data-trees` before working with lists.

## Layout

Components have physical dimensions and overlap if placed poorly.

Typical sizes: Number Slider ~200x20px, Panel ~100x50px, Components ~80x50px.

Spacing: 150px horizontal between columns, 70px vertical between rows. Inputs at x≈50, processing at x≈250,400,550..., outputs rightmost.

Use `validate_layout()` to check overlaps.

## Workflow

1. `set_solver_enabled(false)` — prevent partial recalculation during construction
2. Add components with `add_component(type, x, y)` using proper spacing
3. Wire with `bulk_connect(connections)` for efficiency
4. `set_solver_enabled(true)` — run the definition
5. `get_canvas_status()` — check for errors/warnings
6. `validate_layout()` — check for overlaps

## Key Tools

Discovery: `search_components(query)`, `get_component_info(id)`, `get_component_parameters(type)`

Building: `add_component`, `connect_components`, `bulk_connect`, `set_component_value`

Validation: `get_canvas_status`, `validate_connection`, `validate_layout`, `get_disconnected_inputs`

Debugging: `get_component_outputs(id)`, `trace_data_flow(id, direction)`

## Common Errors

1. Adding components with solver enabled → partial recalculation errors OR slow performance
2. Overlapping components → unreadable canvas
3. Wrong role → using Circle parameter instead of Circle component
4. Tree mismatch → unexpected cross-products or null data
5. Unvalidated connections → silent type coercion failures

## Resources

- `gh://docs/data-trees` — essential for list/tree operations
- `gh://docs/canvas-layout` — spacing details
- `gh://docs/geometry-orientation` — how planes work, which axis is "direction" for oriented geometry
- `gh://docs/type-system` — type compatibility
- `gh://component/{name}` — any component's inputs/outputs (includes orientation hints)
- `gh://patterns/*` — radial-array, linear-array, grid-array
