# Requirement & Design — `rhino_scene(action='place_image')`

> **Status: SHIPPED** (2026-06-21, commit `ceab6e0`; backlog RSC-2H9K archived). Kept as the
> design record for the shipped contract — the implementation matches this doc. The originating
> report (`incoming-bugs/place-raster-image-picture-frame-action.md`) was archived by the
> 2026-07-02 janitor pass and is preserved in git history.

**Backlog:** RSC-2H9K · **Stage:** ready (discovery complete 2026-06-21)
**Source:** `incoming-bugs/place-raster-image-picture-frame-action.md` (Puzzles project, Chunk 06)

## Problem

There is no way to place a raster image into the live Rhino scene as a real document
object. The Puzzles print-and-cut generator needs to preview a cut layout *over* the printed
artwork (registration check) and has deferred its Chunk 06 waiting on this. Existing routes don't
cover it: the PBR material-texture path (`rhino_render material_texture`) only shows in
Rendered/Raytraced mode and needs baked geometry; `rhino_scene script` is command-macro only;
a doc-mutating GH script accumulates duplicate frames on every solve.

## Success

A single `rhino_scene(action='place_image')` call places a Rhino **PictureFrame** at a
caller-specified location and size on a named layer and returns the new object id. Calling it
again with the same `name` + `replace=true` replaces rather than duplicates — safe to call
repeatedly from a parametric workflow.

## Out of scope (v1)

- Non-rectangular / skewed / four-corner-point placement (the host API takes a plane + width +
  height; arbitrary quads are not natively supported).
- Arbitrary 3D plane orientation (deferred — see Placement below; v1 is flat with optional Z-rotation).
- Interactive point picking / file dialogs (not reliable headless).
- Anything in `rhino_render` materials — that path already exists and remains the documented interim.

## Foreign API (verified 2026-06-21 via reflection on Rhino 8 RhinoCommon)

```csharp
Guid Rhino.DocObjects.Tables.ObjectTable.AddPictureFrame(
    Plane plane, string texturePath, bool asMesh,
    double width, double height, bool selfIllumination, bool embedBitmap)
```

- **No `ObjectAttributes` overload.** Layer + name must be set *after* add: fetch the object by the
  returned GUID, set `Attributes.LayerIndex` / `Attributes.Name`, commit via
  `Objects.ModifyAttributes(...)` — the same post-add attribute pattern `bake` / `set_layer` use.
- Width runs along the plane's X axis, height along its Y axis, in model units.
- **Build-time `verify-api` required** on the implementing chunk (read the live SDK / probe before
  drafting the handler) — this is a foreign API the project doesn't own.

## Contract — `rhino_scene(action='place_image')`

| Param | Type | Req | Default | Meaning |
|---|---|---|---|---|
| `path` | string | yes | — | Absolute path to the image file (png/jpg/…). Validate it exists. |
| `x`, `y`, `z` | double | yes | — | Placement origin in model units (the picture's plane origin). |
| `width` | double | yes | — | Width along plane X, model units. Must be > 0. |
| `height` | double | yes | — | Height along plane Y, model units. Must be > 0. |
| `rotation` | double | no | 0 | In-plane rotation about Z, **degrees**. Plane is world-XY-parallel at the origin, rotated by this angle. |
| `layer` | string | no | current | Destination layer; auto-create if missing (as `bake`/`set_layer` do). |
| `name` | string | no | null | Object name; required for `replace` to match. |
| `replace` | bool | no | false | If true and `name` is set: delete existing objects with that exact name **on the target layer** before adding (idempotent re-placement). |
| `selfIllumination` | bool | no | true | Picture self-lit → visible without scene lights (preview-friendly). |
| `embedBitmap` | bool | no | false | Link to the file (small doc) vs. embed in the .3dm. |
| `asMesh` | bool | no | false | Surface-backed (Rhino `_PictureFrame` default) vs. mesh-backed. |

**Plane (v1, flat):** `Plane.WorldXY` translated to `(x,y,z)`, then rotated `rotation` degrees about
its Z axis. Arbitrary normal/orientation is a later extension.

**Returns:** `{ success: true, objectId, layer, replaced: <int count deleted> }`.

**Errors (tool-boundary `{success:false,error}`):** missing/invalid file path; non-positive
width/height; layer create failure; `AddPictureFrame` returns `Guid.Empty` (add failed). `replace`
with no `name` → succeed but `replaced: 0` (nothing to match) and a `note`.

## Resolved decisions (discovery)

- **Tool = `rhino_scene`** (doc-object lifecycle, not `rhino_render`). Additive action; no breaking
  change to the contract surface.
- **Units = model units**, consistent with all other `rhino_scene` geometry.
- **Placement = Option A** (origin + size + optional Z-rotation, flat). Chosen by user 2026-06-21.
- **`replace` default = false** (safe/additive); the parametric consumer passes `replace=true`.
- Defaults: `selfIllumination=true`, `embedBitmap=false`, `asMesh=false`.

## Documentation audit (on build)

- `rhino_scene` `UnifiedToolInfo` — add `place_image` ActionInfo (params, example, tips).
- `McpServer.GetServerInstructions()` — add `place_image` to the `rhino_scene` action list.
- CHANGELOG — Added entry.
- Consider a short note in the relevant Knowledge guide if a placement workflow is worth documenting.

## Boundary / testing notes

- Crosses the **MCP Tool/Action Contract** (additive action) and **Embedded Documentation Contract**.
- The handler is host-dependent (RhinoDoc, AddPictureFrame) → verified live in Rhino per
  `project-preferences.md`, not host-free unit tests. Any pure helpers extracted (e.g. plane
  construction from x/y/z/rotation, param validation) should live host-free so they *can* be
  unit-tested.
