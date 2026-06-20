# Feature request: native "place raster image" / PictureFrame action

**Filed:** 2026-06-20
**From:** Puzzles project (print-and-cut puzzle generator), Chunk 06 — graphic overlay + registration
**Type:** feature / capability gap
**Priority:** medium (there is a usable-but-awkward workaround today)

## What's needed

A first-class cordyceps action to **place a raster image into the live Rhino scene**
as a Rhino **PictureFrame** object (or equivalent placed-image object), at a caller-
specified placement, returning the new object's id.

Concretely, the Puzzles tool wants to drop the printed artwork onto the board so the
cut layout previews *over the actual picture* (print-and-cut registration check) — the
exact thing Rhino's `_PictureFrame` / `RhinoDoc.Objects.AddPictureFrame(...)` does.

## Why the existing routes don't cover it

Verified against the cordyceps source on 2026-06-20:

1. **No PictureFrame support exists.** A case-insensitive search for
   `PictureFrame` / `AddPictureFrame` across the whole tree returns zero matches. The
   only image-into-scene path is a PBR material texture (below); the only `Bitmap`
   uses are the plugin icon and viewport/canvas screenshot capture
   (`GhDocumentTool.Capture.cs`).

2. **PBR material texture (works today, but isn't a PictureFrame).**
   `rhino_render(action='material_texture', name=<mat>, slot='base-color',
   path='/abs/art.png')` (`RhinoRenderTool.Materials.cs:87-198`) binds an image into a
   render material; `material_apply` (`Materials.cs:415-470`) puts it on a baked
   surface by objectId. This *can* show the artwork — but only in **Rendered /
   Raytraced** display mode (it's a render material, invisible in Wireframe/Shaded),
   and it requires first baking a surface to carry it. It is a texture-on-geometry, not
   a true PictureFrame object.

3. **`rhino_scene(action='script')` can't do it.** It runs
   `RhinoApp.RunScript(cmd, false)` — Rhino **command macros** only
   (`RhinoSceneTool.cs:610-620`), not RhinoCommon/Python. Driving the interactive
   `_PictureFrame` command needs picked points / a file dialog — not reliable headless.

4. **Doc-mutating GH script is the only current "real PictureFrame" path, and it's
   bad.** A `gh_script` body *can* call `RhinoDoc.ActiveDoc.Objects.AddPictureFrame(...)`
   itself — but a Grasshopper component re-solves on every parameter change, so it would
   accumulate duplicate frames unless the script tracks and deletes the prior one each
   solve. Mutating the Rhino document from inside a GH solve is an anti-pattern we don't
   want in the definition.

## Suggested shape

A new action — e.g. `rhino_scene(action='place_image')` (sits next to the other
doc-object actions) — roughly:

| param | meaning |
|---|---|
| `path` | absolute path to the image file (png/jpg/…) |
| placement | a plane/origin + `width`,`height` **or** four corner points (board-space mm) |
| `layer` | destination layer (auto-create, as `bake`/`set_layer` already do) |
| `name` | object name |
| `replace` | bool — if an object with this `name` exists on the layer, replace it (idempotent re-placement, so repeated calls don't silt up the layer) |

Returns `{ success, objectId }`. Internally just
`RhinoDoc.Objects.AddPictureFrame(plane, path, selfIllumination?, width, height, …)`.

Idempotent placement (`replace`) is the key ask — it's what makes this safe to call
repeatedly from a parametric workflow, unlike the GH-script route.

## Workaround in the meantime (Puzzles v1)

Until this lands, Puzzles Chunk 06 ships the **placement geometry + transform** (the
image's mapped board rectangle, baked to a preview layer) and the registration
fiducials, and **defers the actual image render to this future action** — mirroring how
DXF *file* write is already deferred to a future cordyceps DXF-export action. The
texture-on-material route (#2) is available as an interim if a rendered preview is
wanted.

## Reference (verify-api, 2026-06-20)

- `gh_canvas` bake — `GhCanvasTool.cs:895-981` (bakes all outputs/branches of one
  component; generic `Objects.Add` handles arbitrary curves/surfaces/meshes;
  auto-creates the layer; returns `objectIds`/`bakedCount`/`layer`).
- `rhino_render` materials — `RhinoRenderTool.Materials.cs` (`material_create` 337-413,
  `material_texture` 87-198, `material_apply` 415-470; texture visible only in
  Rendered/Raytraced).
- `rhino_scene` script — `RhinoSceneTool.cs:610-620` (command-macro only).
- `rhino_scene` object/layer ops — `RhinoSceneTool.cs` (`objects` 233-284, `delete`
  587-608, `set_layer` 350-394) / `.Layers.cs` (`layer_delete` 182-246) support the
  idempotent list-and-delete / wipe-layer cleanup the `replace` flag would obviate.
