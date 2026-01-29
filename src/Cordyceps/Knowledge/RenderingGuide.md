# Rhino Rendering Pipeline via Cordyceps

Complete workflow from Grasshopper geometry → Rhino baking → materials → viewport control → frame capture.

## Workflow Overview

1. **Create geometry** in Grasshopper (add_component, connect_components)
2. **Bake to Rhino** (bake_geometry) - converts GH preview to Rhino objects
3. **Organize** with layers (rhino_create_layer, rhino_set_object_layer)
4. **Apply materials** (rhino_create_material, rhino_apply_material)
5. **Configure lighting** (rhino_set_sun, rhino_set_skylight, rhino_set_ground_plane)
6. **Set environment** (rhino_set_current_environment, rhino_set_render_settings)
7. **Set display mode** (rhino_set_display_mode - Rendered, Raytraced)
8. **Position camera** (rhino_get_camera, rhino_set_camera, rhino_zoom_extents)
9. **Wait for render** (rhino_wait_for_render - for Raytraced mode)
10. **Capture** (capture_viewport)

## Key Tools

### Object Management
- `rhino_get_objects(layer?, type?, name?)` - query Rhino objects
- `rhino_select_objects(objectIds)` - select by GUID
- `rhino_set_object_layer(objectIds, layer)` - move to layer (creates layer if needed)
- `rhino_hide_objects(objectIds)`, `rhino_show_objects(objectIds)` - visibility
- `rhino_delete_objects(objectIds)` - permanent deletion

### Layer Management
- `rhino_get_layers()` - list all layers with properties
- `rhino_create_layer(name, color?, visible?, parent?)` - create layer
- `rhino_set_layer_properties(name, color?, visible?, locked?)` - modify
- `rhino_delete_layer(name, deleteObjects?)` - remove layer

### Materials (PBR)
- `rhino_get_materials()` - list document materials
- `rhino_create_material(name, color, roughness?, metallic?, transparency?, emission?, ior?)` - create PBR material
- `rhino_apply_material(objectIds, material)` - apply by name or index
- `rhino_delete_material(name)` - remove material

**PBR Parameters:**
- `color` - hex "#808080" or RGB "128,128,128"
- `roughness` - 0 (mirror) to 1 (matte)
- `metallic` - 0 (dielectric) to 1 (metal)
- `transparency` - 0 (opaque) to 1 (transparent)
- `emission` - glow color for emissive materials
- `ior` - index of refraction (glass ~1.5, water ~1.33)

### Viewport Control
- `rhino_get_display_modes()` - list available modes
- `rhino_set_display_mode(mode, view?)` - Wireframe/Shaded/Rendered/Ghosted/Arctic/Raytraced
- `rhino_get_camera(view?)` - get location, target, up, lens, distance
- `rhino_set_camera(location?, target?, lens?, view?)` - position camera
- `rhino_zoom_extents(view?)` - fit all geometry
- `rhino_zoom_objects(objectIds, view?)` - fit specific objects

### Render Environments
- `rhino_get_environments()` - list all environments in document
- `rhino_get_current_environment()` - get active environment for each usage
- `rhino_set_current_environment(environment, usage?)` - set by name/GUID
  - `usage`: 'background', 'lighting', 'reflection', or 'all' (default)
- `rhino_create_environment(name, color)` - create solid-color environment
- `rhino_delete_environment(name)` - remove environment

### Render Settings (Background)
- `rhino_get_render_settings()` - get background style, colors, transparency
- `rhino_set_render_settings(style?, colorTop?, colorBottom?, transparent?)`
  - `style`: 'solid', 'gradient', or 'environment'
  - Colors as hex "#808080" or RGB "128,128,128"

### Ground Plane
- `rhino_get_ground_plane()` - get enabled, altitude, shadowOnly, material
- `rhino_set_ground_plane(enabled?, altitude?, autoAltitude?, shadowOnly?, showUnderside?, material?)`
  - `shadowOnly=true` - invisible plane that catches shadows
  - `material` - name of render material to apply

### Sun
- `rhino_get_sun()` - get enabled, azimuth, altitude, intensity, location, dateTime
- `rhino_set_sun(enabled?, azimuth?, altitude?, latitude?, longitude?, dateTime?, intensity?, north?)`
  - **Manual mode**: set `azimuth` (0-360, north=0) and `altitude` (-90 to 90)
  - **Calculated mode**: set `latitude`, `longitude`, `dateTime` (ISO 8601)
  - `intensity` - sun brightness multiplier
  - `north` - north direction angle on XY plane

### Skylight
- `rhino_get_skylight()` - get enabled, shadowIntensity, customEnvironment
- `rhino_set_skylight(enabled?, shadowIntensity?, customEnvironmentOn?, customEnvironment?)`
  - `customEnvironment` - environment name for skylight (instead of default sky)

### Render Status (Raytraced mode)
- `rhino_get_render_status(view?)` - returns currentPass, maxPasses, isComplete, progress%
- `rhino_wait_for_render(minPasses?, timeout?, view?)` - block until passes reached

### Capture
- `capture_viewport(outputPath?, view?, width?, height?, transparent?, waitForRender?, renderTimeout?)`
  - `waitForRender` - minimum passes before capture (Raytraced only)
  - `renderTimeout` - max wait time in seconds

## Camera Orbit Pattern

No orbit tool provided - LLM calculates positions. Steps:

1. `rhino_get_camera()` → read location, target, distance
2. Calculate new position:
   - `angle` = frame_index * angle_step (e.g., 10°)
   - `newX = target.x + distance * cos(angle)`
   - `newY = target.y + distance * sin(angle)`
   - `newZ = location.z` (keep same height)
3. `rhino_set_camera(location="newX,newY,newZ", target="same")`
4. `rhino_wait_for_render(minPasses=200)` (if Raytraced)
5. `capture_viewport(path=frame_001.png)`
6. Repeat for all frames

## Example: Stonehenge Scene

```
# 1. Create geometry in Grasshopper (pillars, lintels, ground)
add_component(type="Cylinder", x=100, y=100) → pillar_id
add_component(type="Box", x=200, y=100) → lintel_id
# ... wire up with sliders for dimensions

# 2. Bake to Rhino with layers
bake_geometry(id=pillar_id, layer="Stones", name="Pillar")
bake_geometry(id=lintel_id, layer="Stones", name="Lintel")
bake_geometry(id=ground_id, layer="Ground", name="Terrain")

# 3. Create and apply materials
rhino_create_material(name="Stone", color="#808080", roughness=0.8)
rhino_create_material(name="Grass", color="#228B22", roughness=0.9)

rhino_get_objects(layer="Stones") → stone_ids
rhino_apply_material(objectIds=stone_ids, material="Stone")

rhino_get_objects(layer="Ground") → ground_ids
rhino_apply_material(objectIds=ground_ids, material="Grass")

# 4. Set up viewport
rhino_set_display_mode(mode="Raytraced")
rhino_zoom_extents()

# 5. Get camera for orbit calculations
rhino_get_camera() → {location, target, distance}

# 6. Capture 36 frames (10° each)
for i in range(36):
    angle = i * 10 * (pi/180)
    newX = target.x + distance * cos(angle)
    newY = target.y + distance * sin(angle)

    rhino_set_camera(location=f"{newX},{newY},{location.z}")
    rhino_wait_for_render(minPasses=200, timeout=10)
    capture_viewport(outputPath=f"frame_{i:03d}.png", width=1920, height=1080)

# 7. Assemble GIF externally
# ffmpeg -framerate 10 -i frame_%03d.png -loop 0 stonehenge.gif
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
- Use `rhino_wait_for_render(minPasses=100)` for preview quality
- Use `rhino_wait_for_render(minPasses=500+)` for final quality
- Pass count affects render time significantly

## Example: Studio Lighting Setup

```
# Create a clean studio backdrop
rhino_set_render_settings(style="gradient", colorTop="#E0E0E0", colorBottom="#808080")

# Enable ground plane for shadows
rhino_set_ground_plane(enabled="true", shadowOnly="true", altitude="0")

# Configure skylight for soft ambient light
rhino_set_skylight(enabled="true")

# Add sun for directional light
rhino_set_sun(enabled="true", azimuth="135", altitude="45", intensity="1.5")

# Set raytraced mode
rhino_set_display_mode(mode="Raytraced")
```

## Example: Outdoor Scene with HDR Environment

```
# Check available environments (may include HDR environments loaded in Rhino)
rhino_get_environments()

# Set environment for background and lighting
rhino_set_current_environment(environment="Default Environment", usage="all")
rhino_set_render_settings(style="environment")

# Configure sun based on location and time
rhino_set_sun(
    enabled="true",
    latitude="40.7128",      # New York
    longitude="-74.0060",
    dateTime="2024-06-21T14:00:00",
    intensity="1.0"
)

# Enable skylight for additional fill
rhino_set_skylight(enabled="true")

# Ground plane with grass material
rhino_set_ground_plane(enabled="true", autoAltitude="true", material="Grass")
```

## Coordinate Format

All Point3d values as comma-separated strings: `"x,y,z"`
- Camera location: `"100.5,50.25,30.0"`
- Target point: `"0,0,0"`
- Colors: hex `"#FF8000"` or RGB `"255,128,0"`
