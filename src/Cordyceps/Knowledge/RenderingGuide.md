# Rendering Pipeline

Grasshopper geometry → Rhino baking → materials → viewport → capture.

## Workflow

1. Create geometry in GH (`gh_canvas`, `gh_wire`)
2. Bake: `gh_canvas(action='bake', id='...', layer='...')`
3. Display mode: `rhino_render(action='display', mode='Raytraced')`
4. Camera: `rhino_render(action='camera', ...)` or `rhino_render(action='zoom')`
5. Wait: `rhino_render(action='render', wait=200)`
6. Capture: `gh_document(action='capture_viewport', path='...')`

## Realistic Rendering Tips

**Display modes**: Materials/lighting only work in `Rendered` (fast preview) or `Raytraced` (photorealistic).

**Lighting**: Enable BOTH sun AND skylight. Skylight prevents black shadows.
- Sun: altitude 30-45° (daylight), azimuth 135°/225° (3/4 lighting)
- `rhino_render(action='sun', sunEnabled=true, azimuth=180, sunAltitude=45)`
- `rhino_render(action='skylight', skylightEnabled=true)`

**Background**: Use gradient. `rhino_render(action='settings', style='gradient', colorTop='#87CEEB', colorBottom='#E8E8E8')`

**Ground plane**: `rhino_render(action='ground', groundEnabled=true, shadowOnly=true)`

**Materials**: Set roughness (0.1=polished, 0.9=matte). Use realistic colors.

**Textured materials**: Use `material_texture` to assign image maps to PBR slots for photorealistic results.

**References**: [physicallybased.info](https://physicallybased.info/), [pixelandpoly.com/ior](https://pixelandpoly.com/ior.html)

## Key Actions

Use `action='help'` on any tool for full parameter details.

### rhino_scene
`objects`, `select`, `deselect`, `set_layer`, `set_name`, `set_color`, `bbox`, `place_image`, `hide`, `show`, `delete`, `layers`, `layer_create`, `layer_set`, `layer_delete`

`place_image` puts a raster image into the scene as a real Rhino PictureFrame object at a given origin/size/rotation — use it (not a PBR `material_texture`) when you need the image as a placeable, selectable document object (e.g. previewing a layout over printed artwork). `replace=true` with a `name` keeps repeated parametric calls idempotent.

### rhino_render
**Viewport**: `modes`, `display`, `camera`, `zoom`
**Views**: `view_save`, `view_load`, `view_list`, `view_delete`
**Render**: `render` (get status or wait for passes)
**Settings**: `settings`, `ground`, `sun`, `skylight`
**Lights**: `light_add`, `light_list`, `light_set`, `light_delete`
**Materials**: `material_list`, `material_library`, `material_instantiate`, `material_create`, `material_texture`, `material_apply`, `material_delete`
**Environments**: `env_list`, `env_current`, `env_set`, `env_create`

### gh_document
`capture_viewport`, `capture_canvas`, `capture_views`

## Built-in Material Types

Use with `material_instantiate`:

| Type | Use For |
|------|---------|
| Metal | Gold, silver, copper, aluminum |
| Glass | Windows, bottles (IOR ~1.5) |
| Plastic | Non-metallic glossy |
| Paint | Painted surfaces |
| Gem | Diamonds, rubies (dispersion) |
| Plaster | Matte walls |
| Emission | Glowing, screens, neon |

## Textured PBR Materials

For photorealistic results, assign image textures to PBR material slots using `material_texture`.

### Workflow

All calls via `rhino_render`:

```
1. rhino_render(action='material_create', name='Grass', color='#4A7C2E', roughness=0.9)
2. rhino_render(action='material_texture', name='Grass', slot='base-color', path='C:/textures/grass_albedo.jpg', repeat='8,8')
3. rhino_render(action='material_texture', name='Grass', slot='roughness', path='C:/textures/grass_rough.jpg', repeat='8,8')
4. rhino_render(action='material_texture', name='Grass', slot='bump', path='C:/textures/grass_normal.jpg', repeat='8,8', amount=50)
5. rhino_render(action='material_apply', ids='[...]', material='Grass')
```

### PBR Texture Slots

| Slot | Purpose |
|------|---------|
| `base-color` | Albedo/diffuse color map |
| `roughness` | Surface roughness (white=rough) |
| `metallic` | Metalness (white=metal) |
| `bump` | Normal/bump map for surface detail |
| `opacity` | Transparency (white=opaque) |
| `emission` | Self-illumination map |
| `displacement` | Geometry displacement |
| `ambient-occlusion` | Crevice shadowing |
| `clearcoat` | Clear lacquer layer |
| `clearcoat-roughness` | Clearcoat roughness |

### Parameters

- **repeat**: UV tiling as `"u,v"`. Use `"4,4"` or higher for large surfaces.
- **amount**: Slot influence 0-100 (default 100). Lower values blend with base property.
- **Omit path** to remove a texture from a slot.

### Tips

- Use `material_list` to inspect which slots have textures assigned.
- All common formats supported: .png, .jpg, .bmp, .tif, .exr, .hdr
- Textures require absolute file paths.

## Camera Orbit Pattern

No orbit tool - calculate positions:

1. Get current: `rhino_render(action='camera')` → location, target, distance
2. For each frame:
   - angle = frame * step (radians)
   - newX = target.x + distance * cos(angle)
   - newY = target.y + distance * sin(angle)
   - `rhino_render(action='camera', location='newX,newY,z')`
   - `rhino_render(action='render', wait=200)` if Raytraced
   - `gh_document(action='capture_viewport', path='frame_NNN.png')`

## Display Mode Performance

| Mode | Quality | Speed |
|------|---------|-------|
| Wireframe | Low | Instant |
| Shaded | Medium | Instant |
| Rendered | Medium-High | Fast |
| **Raytraced** | **Highest** | **Slow** |

Raytraced: `wait=100` for preview, `wait=500` for final quality.

## Coordinate Format

All Point3d as comma-separated: `"x,y,z"` (e.g., `"100.5,50.25,30.0"`)
