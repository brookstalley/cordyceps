# Grasshopper via Cordyceps

Grasshopper is a visual dataflow graph for parametric 3D design. Components on a canvas connect via wires. Data flows left-to-right.

## Unified Tools

Cordyceps provides 10 unified tools, each with an `action` parameter. Use `action='help'` on any tool to see all available actions and parameters.

**Grasshopper Tools:**
- `gh_canvas` - Add, delete, move, rename, find, search, list, bake components
- `gh_wire` - Connect, disconnect, list, validate wires
- `gh_adjust` - Get/set values, configure sliders, toggles, panels
- `gh_document` - Info, save, clear, solver control, undo/redo, snapshots
- `gh_group` - Create, delete, add/remove members, rename, color groups
- `gh_script` - Get/set script code, configure parameters
- `gh_inspect` - Canvas status, outputs, trace data flow, find disconnected
- `gh_capture` - Capture canvas, viewport, regions

**Rhino Tools:**
- `rhino_scene` - Objects, selection, layers (full CRUD), hide/show, delete, run scripts
- `rhino_render` - Display modes, camera, zoom, render status, settings, sun, skylight, ground plane
- `rhino_material` - List, create, apply, delete PBR materials
- `rhino_environment` - List, set, create, delete render environments

## Object Types

Three roles (check `role` field in responses):
- **component**: Processing node with inputs/outputs. Usually what you want.
- **parameter**: Data container (Params category). Holds geometry/values.
- **input**: Interactive control (Params/Input subcategory). Sliders, toggles.

**Ambiguous names**: "Circle" matches both Circle component (Curve/Primitive) and Circle parameter (Params/Geometry). On ambiguity, you get `ambiguous_name` error with all matches. Resolve with GUID or category-qualified name like `Curve/Circle`.

## Data Trees

Grasshopper uses hierarchical data trees with paths like `{0;0;1}`, not flat arrays. Mismatched tree structures cause cross-products. A 10-item list connected to a single-item input runs 10 operations. Read `gh://docs/data-trees` before working with lists.

## Layout

**Goal: Avoid backwards wires. Prefer vertical stacking over horizontal spread.**

Components have physical dimensions. Place carefully to avoid overlaps.

Typical sizes: Number Slider ~200x20px, Panel ~100x50px, Components ~60-80x50px.

**Spacing:**
- **Horizontal**: 60-80px between columns (about 1x component width)
- **Vertical**: 70px between stacked components
- **Inputs**: Stack sliders vertically at x≈50, y=50/120/190/260...
- **Processing**: Columns at x≈300, 380, 460... (after sliders' right edge)

**Key principle**: Minimize horizontal spread. Stack inputs vertically. Only spread horizontally to follow data flow direction.

Use `gh_canvas(action='validate')` to check overlaps. Fix with `gh_canvas(action='move')`.

## Workflow

1. `gh_document(action='solver', enabled=false)` — prevent partial recalculation during construction
2. **If creating oriented geometry** (cylinders, cones, extrusions), read `gh://docs/geometry-orientation` first — oriented geometry extends along the plane's Z-axis, not X or Y
3. Add components with `gh_canvas(action='add', type='...', x=..., y=...)` using proper spacing
4. Wire with `gh_wire(action='connect', connections='[...]')` for efficiency
5. `gh_document(action='solver', enabled=true)` — run the definition
6. `gh_inspect(action='status')` — check for errors/warnings
7. `gh_canvas(action='validate')` — check for overlaps

## Key Tools

**Discovery:**
- `gh_inspect(action='categories')` — list all component categories
- `gh_canvas(action='search', query='...')` — find components by name
- `gh_canvas(action='info', id='...')` — get component details
- `gh_inspect(action='docs', type='...')` — get component documentation

**Building:**
- `gh_canvas(action='add', type='...', x=..., y=...)` — add component
- `gh_wire(action='connect', sourceId='...', sourceParam='...', targetId='...', targetParam='...')` — single connection
- `gh_wire(action='connect', connections='[{...},{...}]')` — bulk connections
- `gh_adjust(action='set', id='...', value='...')` — set value
- `gh_adjust(action='config', id='...', min=..., max=..., value=...)` — configure slider

**Querying:**
- `gh_canvas(action='list')` — list all components
- `gh_wire(action='list')` — list all connections
- `gh_inspect(action='status')` — get canvas status with errors/warnings

**Validation:**
- `gh_wire(action='validate', sourceId='...', sourceParam='...', targetId='...', targetParam='...')` — check connection validity
- `gh_canvas(action='validate')` — check for overlapping components
- `gh_inspect(action='disconnected')` — find disconnected inputs

**Debugging:**
- `gh_inspect(action='outputs', id='...')` — get component output values
- `gh_inspect(action='trace', id='...', direction='upstream')` — trace data flow

**Scripts:**
- `gh_script(action='get', id='...')` — get script source code
- `gh_script(action='set', id='...', code='...')` — set script code
- `gh_script(action='info', id='...')` — get script details (code, params, errors)
- `gh_script(action='configure', id='...', inputs='[...]', outputs='[...]', code='...')` — configure script

**Visualization:**
- `gh_capture(action='canvas')` — capture Grasshopper canvas
- `gh_capture(action='viewport')` — capture Rhino 3D viewport
- `gh_capture(action='views')` — list available views

## Common Errors

1. Adding components with solver enabled → partial recalculation errors OR slow performance
2. Overlapping components → unreadable canvas
3. Wrong role → using Circle parameter instead of Circle component
4. Getting max(N,M) outputs instead of N×M → need to graft one input. See `gh://docs/data-trees`
5. Unvalidated connections → silent type coercion failures
6. Component overlaps → unreadable canvas. **Fix**: Use `gh_canvas(action='validate')` to detect, then `gh_canvas(action='move')` to fix
7. Geometry pointing wrong direction → oriented geometry (Cylinder, Cone, etc.) extends along the plane's **Z-axis**. Using XY Plane gives vertical geometry; use YZ Plane or Plane Normal for horizontal. See `gh://docs/geometry-orientation`

## Capturing Images

Use capture tools to see what you've built:

- `gh_capture(action='canvas')` — Save the Grasshopper canvas as an image. Auto-fits to content by default.
- `gh_capture(action='canvas', fit=false)` — Capture current view without auto-fitting.
- `gh_capture(action='region', xMin=..., yMin=..., xMax=..., yMax=...)` — Capture a specific canvas region.
- `gh_capture(action='viewport')` — Save the Rhino 3D viewport showing geometry preview.
- `gh_capture(action='viewport', view='Top', width=1920, height=1080)` — Capture a specific view at custom resolution.
- `gh_capture(action='views')` — List available Rhino viewports (Perspective, Top, Front, etc.)

All capture functions return a file path. Use the Read tool to view captured images.

## Rhino Rendering Pipeline

After building geometry in Grasshopper, you can bake to Rhino and create rendered images:

**Workflow:** Create geometry → Bake → Organize layers → Apply materials → Set viewport → Capture

**Baking:**
- `gh_canvas(action='bake', id='...', layer='...')` — bake component output to Rhino

### Rhino Tools

**Objects:**
- `rhino_scene(action='objects')` — list Rhino objects
- `rhino_scene(action='select', ids='[...]')` — select objects
- `rhino_scene(action='deselect')` — clear selection
- `rhino_scene(action='set_layer', ids='[...]', layer='...')` — move objects to layer
- `rhino_scene(action='set_name', ids='[...]', name='...')` — rename objects
- `rhino_scene(action='hide', ids='[...]')` — hide objects
- `rhino_scene(action='show', ids='[...]')` — show objects
- `rhino_scene(action='delete', ids='[...]')` — delete objects

**Layers:**
- `rhino_scene(action='layers')` — list all layers
- `rhino_scene(action='layer_create', name='...')` — create layer
- `rhino_scene(action='layer_set', name='...', visible='false')` — modify layer
- `rhino_scene(action='layer_delete', name='...')` — delete layer

**Viewport:**
- `rhino_render(action='modes')` — list display modes
- `rhino_render(action='display', mode='Raytraced')` — set display mode
- `rhino_render(action='camera')` — get camera info
- `rhino_render(action='camera', location='x,y,z', target='x,y,z')` — set camera
- `rhino_render(action='zoom')` — zoom to fit all
- `rhino_render(action='zoom', ids='[...]')` — zoom to specific objects

**Render Status:**
- `rhino_render(action='render')` — get render status (for Raytraced mode)
- `rhino_render(action='render', wait=200, timeout=30)` — wait for render passes

### Camera Orbit Pattern

No orbit tool provided — calculate camera positions yourself:
1. `rhino_render(action='camera')` → get location, target, distance
2. Calculate new position: `newX = target.x + distance * cos(angle)`, `newY = target.y + distance * sin(angle)`
3. `rhino_render(action='camera', location='newX,newY,z')` → move camera
4. `rhino_render(action='render', wait=200)` → wait for Raytraced
5. `gh_capture(action='viewport', path='frame_001.png')` → capture frame

For complete rendering workflow details, read `gh://docs/rendering`.

## Testing & Validation

If asked to test the MCP server, validate Cordyceps, or help debug connection issues, read `gh://docs/mcp-testing` for comprehensive test instructions covering all features.

## Resources

- `gh://docs/getting-started` — this guide
- `gh://docs/data-trees` — essential for list/tree operations
- `gh://docs/canvas-layout` — spacing details
- `gh://docs/geometry-orientation` — how planes work, which axis is "direction" for oriented geometry
- `gh://docs/type-system` — type compatibility
- `gh://docs/rendering` — complete Rhino rendering pipeline (bake → materials → viewport → capture)
- `gh://docs/mcp-testing` — test instructions for validating MCP server functionality
- `gh://component/{name}` — any component's inputs/outputs (includes orientation hints)
- `gh://patterns/*` — linear-array, grid-array
