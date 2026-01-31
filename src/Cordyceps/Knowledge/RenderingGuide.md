# Rhino Rendering Pipeline via Cordyceps

Complete workflow from Grasshopper geometry → Rhino baking → materials → viewport control → frame capture.

## Workflow Overview

1. **Create geometry** in Grasshopper (`gh_canvas(action='add')`, `gh_wire(action='connect')`)
2. **Bake to Rhino** (`gh_canvas(action='bake')`) - converts GH preview to Rhino objects
3. **Organize** with layers (`rhino_scene(action='layers')`)
4. **Set display mode** (`rhino_render(action='display', mode='Rendered')` or `Raytraced`)
5. **Position camera** (`rhino_render(action='camera')`, `rhino_render(action='zoom')`)
6. **Wait for render** (`rhino_render(action='render', wait=200)` - for Raytraced mode)
7. **Capture** (`gh_document(action='capture_viewport')`)

## Realistic Rendering

**Display modes**: Materials/lighting only work in Rendered (preview) or Raytraced (photorealistic). Use Raytraced + wait for quality output.

**Lighting**: Always enable BOTH sun AND skylight. Skylight prevents black shadows. Sun: altitude 30-45° (daylight), azimuth 135°/225° (3/4 lighting).

**Background**: Use gradient (#87CEEB top, #E8E8E8 bottom). Avoid white.

**Ground**: Enable with shadowOnly=true or neutral material.

**Materials**: Set roughness (0.1=polished, 0.7=stone, 0.9=matte) and IOR for glass (1.5). Use realistic colors (rocks=gray/brown).

**Research**: Search "[material] PBR values" or "[scene] Rhino lighting" for specifics. Refs: [physicallybased.info](https://physicallybased.info/), [pixelandpoly.com/ior](https://pixelandpoly.com/ior.html)

## Key Tools

### Object Management (rhino_scene)
- `rhino_scene(action='objects')` - list Rhino objects, filter by layer/type
- `rhino_scene(action='select', ids='[...]')` - select by GUID
- `rhino_scene(action='deselect')` - clear selection
- `rhino_scene(action='set_layer', ids='[...]', layer='...')` - move objects to layer
- `rhino_scene(action='set_name', ids='[...]', name='...')` - rename objects
- `rhino_scene(action='hide', ids='[...]')` - hide objects
- `rhino_scene(action='show', ids='[...]')` or `rhino_scene(action='show', all=true)` - show objects
- `rhino_scene(action='delete', ids='[...]')` - permanent deletion
- `rhino_scene(action='layers')` - list all layers
- `rhino_scene(action='layer_create', name='...', color='#FF0000')` - create layer
- `rhino_scene(action='layer_set', name='...', visible='false')` - modify layer
- `rhino_scene(action='layer_delete', name='...')` - delete layer

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

### Render Settings (rhino_render)
- `rhino_render(action='settings')` - get background style, colors
- `rhino_render(action='settings', style='gradient', colorTop='#87CEEB', colorBottom='#FFFFFF')` - set background
- `rhino_render(action='ground', groundEnabled='true', shadowOnly='true')` - ground plane
- `rhino_render(action='sun', sunEnabled='true', azimuth='180', sunAltitude='45')` - sun position
- `rhino_render(action='skylight', skylightEnabled='true')` - ambient lighting

### Materials (rhino_render)

**IMPORTANT**: Materials only render in **Raytraced** or **Rendered** display modes. Raytraced provides physically accurate rendering; Rendered gives faster preview quality.

**List & Inspect:**
- `rhino_render(action='material_list')` - list all materials in the document
- `rhino_render(action='material_library')` - list available built-in material types

**Built-in Material Types** (use with `material_instantiate`):
| Type | Description |
|------|-------------|
| Metal | Metallic materials - gold, silver, copper, aluminum |
| Glass | Transparent with refraction - windows, bottles, lenses |
| Plastic | Non-metallic with varying glossiness |
| Paint | Painted surfaces with color and sheen |
| Gem | Gemstones with dispersion - diamonds, rubies |
| Plaster | Matte diffuse - walls, ceilings |
| Emission | Light-emitting - screens, neon, glowing objects |
| PhysicallyBased | Full PBR with all parameters |
| Blend | Blend between two materials |
| DoubleSided | Different materials on front/back faces |
| Picture | Image-based materials for decals |

**Create Materials:**
- `rhino_render(action='material_instantiate', type='Metal', name='Copper', color='#B87333')` - create from built-in type
- `rhino_render(action='material_create', name='Red Metal', color='#FF0000', metallic=1, roughness=0.3)` - create custom PBR material

**Apply & Delete:**
- `rhino_render(action='material_apply', ids='[...]', material='Copper')` - apply to objects
- `rhino_render(action='material_delete', name='Copper')` - delete material

**Workflow Example:**
```
# 1. Create materials from built-in types
rhino_render(action='material_instantiate', type='Metal', name='Gold', color='#FFD700')
rhino_render(action='material_instantiate', type='Glass', name='Clear Glass')

# 2. Apply to baked geometry
rhino_render(action='material_apply', ids='["guid1"]', material='Gold')
rhino_render(action='material_apply', ids='["guid2"]', material='Clear Glass')

# 3. Set Raytraced mode to see materials
rhino_render(action='display', mode='Raytraced')
rhino_render(action='render', wait=200)  # Wait for render passes
```

### Environments (rhino_render)
- `rhino_render(action='env_list')` - list render environments
- `rhino_render(action='env_current')` - get current environment for each usage
- `rhino_render(action='env_set', environment='Studio', usage='all')` - set environment
- `rhino_render(action='env_create', name='Blue Sky', color='#87CEEB')` - create solid color environment

### Capture (gh_document)
- `gh_document(action='capture_viewport')` - capture to temp file
- `gh_document(action='capture_viewport', path='/path/to/file.png', width=1920, height=1080)` - custom size
- `gh_document(action='capture_viewport', view='Top')` - specific view
- `gh_document(action='capture_viewport', transparent=true)` - transparent background (PNG only)
- `gh_document(action='capture_views')` - list available views

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
5. `gh_document(action='capture_viewport', path='frame_001.png')`
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
    gh_document(action='capture_viewport', path=f'frame_{i:03d}.png', width=1920, height=1080)

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
