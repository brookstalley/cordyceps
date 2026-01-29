# Rhino Rendering Pipeline via Cordyceps

Complete workflow from Grasshopper geometry → Rhino baking → materials → viewport control → frame capture.

## Workflow Overview

1. **Create geometry** in Grasshopper (`gh_canvas(action='add')`, `gh_wire(action='connect')`)
2. **Bake to Rhino** (`gh_canvas(action='bake')`) - converts GH preview to Rhino objects
3. **Organize** with layers (`rhino_scene(action='layers')`)
4. **Set display mode** (`rhino_render(action='display', mode='Rendered')` or `Raytraced`)
5. **Position camera** (`rhino_render(action='camera')`, `rhino_render(action='zoom')`)
6. **Wait for render** (`rhino_render(action='render', wait=200)` - for Raytraced mode)
7. **Capture** (`gh_capture(action='viewport')`)

## Key Tools

### Object Management (rhino_scene)
- `rhino_scene(action='objects')` - list Rhino objects, filter by layer/type
- `rhino_scene(action='select', ids='[...]')` - select by GUID
- `rhino_scene(action='hide', ids='[...]')` - hide objects
- `rhino_scene(action='show', ids='[...]')` or `rhino_scene(action='show', all=true)` - show objects
- `rhino_scene(action='delete', ids='[...]')` - permanent deletion
- `rhino_scene(action='layers')` - list all layers

### Viewport Control (rhino_render)
- `rhino_render(action='modes')` - list available display modes
- `rhino_render(action='display', mode='Shaded')` - set display mode
- `rhino_render(action='camera')` - get location, target, lens, distance
- `rhino_render(action='camera', location='x,y,z', target='x,y,z', lens=50)` - set camera
- `rhino_render(action='zoom')` - zoom to fit all geometry
- `rhino_render(action='zoom', ids='[...]')` - zoom to specific objects

### Render Status (Raytraced mode)
- `rhino_render(action='render')` - returns currentPass, maxPasses, isComplete, progress%
- `rhino_render(action='render', wait=200, timeout=30)` - block until passes reached

### Capture (gh_capture)
- `gh_capture(action='viewport')` - capture to temp file
- `gh_capture(action='viewport', path='/path/to/file.png', width=1920, height=1080)` - custom size
- `gh_capture(action='viewport', view='Top')` - specific view
- `gh_capture(action='viewport', transparent=true)` - transparent background (PNG only)
- `gh_capture(action='views')` - list available views

## Camera Orbit Pattern

No orbit tool provided - LLM calculates positions. Steps:

1. `rhino_render(action='camera')` → read location, target, distance
2. Calculate new position:
   - `angle` = frame_index * angle_step (e.g., 10°)
   - `newX = target.x + distance * cos(angle)`
   - `newY = target.y + distance * sin(angle)`
   - `newZ = location.z` (keep same height)
3. `rhino_render(action='camera', location='newX,newY,newZ')`
4. `rhino_render(action='render', wait=200)` (if Raytraced)
5. `gh_capture(action='viewport', path='frame_001.png')`
6. Repeat for all frames

## Example: Basic Scene Setup

```
# 1. Create geometry in Grasshopper
gh_canvas(action='add', type='Cylinder', x=100, y=100) → cylinder_id
gh_canvas(action='add', type='Box', x=200, y=100) → box_id
# ... wire up with sliders for dimensions

# 2. Bake geometry to Rhino
gh_canvas(action='bake', id=cylinder_id, layer='Geometry', name='Cylinder')
gh_canvas(action='bake', id=box_id, layer='Geometry', name='Box')

# 3. Set up viewport
rhino_render(action='display', mode='Rendered')
rhino_render(action='zoom')

# 4. Get camera for orbit calculations
rhino_render(action='camera') → {location, target, distance}

# 5. Capture frames
for i in range(36):
    angle = i * 10 * (pi/180)
    newX = target.x + distance * cos(angle)
    newY = target.y + distance * sin(angle)

    rhino_render(action='camera', location=f'{newX},{newY},{location.z}')
    gh_capture(action='viewport', path=f'frame_{i:03d}.png', width=1920, height=1080)

# 6. Assemble GIF externally
# ffmpeg -framerate 10 -i frame_%03d.png -loop 0 orbit.gif
```

## Display Mode Performance

| Mode | Quality | Speed | Notes |
|------|---------|-------|-------|
| Wireframe | Low | Instant | Edges only |
| Shaded | Medium | Instant | OpenGL shading |
| Rendered | Medium-High | Fast | Material preview |
| Ghosted | Low | Instant | Transparent view |
| Arctic | Medium | Fast | White studio look |
| **Raytraced** | **Highest** | **Slow** | Cycles ray tracing, progressive |

For Raytraced mode:
- Use `rhino_render(action='render', wait=100)` for preview quality
- Use `rhino_render(action='render', wait=500)` for final quality
- Pass count affects render time significantly

## Coordinate Format

All Point3d values as comma-separated strings: `"x,y,z"`
- Camera location: `"100.5,50.25,30.0"`
- Target point: `"0,0,0"`
