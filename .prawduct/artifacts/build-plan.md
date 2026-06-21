# Build Plan — RSC-2H9K: `rhino_scene(action='place_image')` (PictureFrame placement)

**Work size/type:** Medium · Feature (new MCP action + host-free helper + doc audit)
**Branch:** `feature/place-image-action`
**Critic mode:** chunk after the single build chunk; **final** before ship (one action, one helper — a single cumulative-style pass covers all 7 goals).
**Refs:** `docs/place-image-action.md` (requirement & design, discovery-complete 2026-06-21), backlog `RSC-2H9K`.

## Confidence Check

1. **Problem:** No way to place a raster image into the live Rhino scene as a real document object. The Puzzles print-and-cut generator (external requester) needs to preview a cut layout *over* printed artwork and has deferred its Chunk 06 on this. The only image-into-scene path today (`rhino_render material_texture`) is a PBR texture, not a placed object.
2. **Success:** One `rhino_scene(action='place_image')` call places a Rhino **PictureFrame** at a caller-specified origin/size/rotation on a named layer and returns the new object id. Re-calling with the same `name` + `replace=true` replaces rather than duplicates (idempotent for parametric workflows). Errors return the tool-boundary `{success:false,error}` contract.
3. **Out of scope (v1, per design doc):** non-rectangular/four-corner/skewed placement; arbitrary 3D plane orientation (v1 is world-XY-parallel + optional Z-rotation); interactive point-picking/file dialogs; anything in `rhino_render` materials.

**Requirements confidence: High.** Discovery complete; the contract (params, defaults, return shape, error cases) is fully specified in `docs/place-image-action.md` and was user-reviewed (Option A placement chosen 2026-06-21). No open design decisions.

## verify-api (foreign host API — done before drafting the handler)

Reflected on the actual Rhino 8 RhinoCommon (`8.0.23304.9001`, the version `Cordyceps.csproj` references) via `MetadataLoadContext`. Confirmed:

- `Guid Rhino.DocObjects.Tables.ObjectTable.AddPictureFrame(Plane plane, string texturePath, bool asMesh, double width, double height, bool selfIllumination, bool embedBitmap)` — exactly the design-doc signature; **no `ObjectAttributes` overload** (layer/name set post-add, as `bake`/`set_layer` do).
- `bool Plane.Rotate(double angle, Vector3d axis)` exists (rotates about the plane's own origin) — used for the optional Z-rotation; `Plane.WorldXY`, `Plane.Origin`, `Plane.ZAxis` all present.
- `double RhinoMath.ToRadians(double degrees)` exists — degrees→radians for `rotation`.

## Boundary Investigation

- **MCP Tool / Action Contract (PRIMARY, external):** purely **additive** — a new `place_image` action on `rhino_scene` plus new optional method params (`path`, `x`, `y`, `z`, `width`, `height`, `rotation`, `replace`, `selfIllumination`, `embedBitmap`, `asMesh`). Reuses the existing `layer`/`name` params. No existing action/param/response shape changes → no breaking change. In-repo consumer of the action vocabulary: `McpServer.GetServerInstructions()` (must list the new action) + `rhino_scene` `ActionInfo` (help). External consumer: the Puzzles project (origin of the request) — contract matches its filed requirement.
- **Embedded Documentation Contract (agent-facing):** new action must appear in server instructions + ActionInfo; root `CHANGELOG.md` Added entry; `change-log.md` tagged entry (views source).
- **Grasshopper / Rhino Host API (FOREIGN):** `AddPictureFrame` + post-add `ModifyAttributes` — verified above. Handler runs on the UI thread via `_context.ExecuteOnUiThread()` per project convention.

## Chunks

### Chunk 01: place_image — action + PlaceImageValidation helper + doc audit

- **Type:** code (feature) + doc audit. **Critic mode:** chunk after commit, then final before ship.
- **Host-free helper (unit-tested):** `src/Cordyceps/Core/PlaceImageValidation.cs` — `static string Validate(string path, double width, double height)` returning an error message or null. Validates: `path` non-empty, file exists (`System.IO.File.Exists`), `width > 0`, `height > 0` (the `!(x > 0)` form also rejects NaN). Presence/required-ness of `path`/`x`/`y`/`z`/`width`/`height` is enforced upstream by `ValidateAction` (NaN-excluded `BuildParams`), consistent with the other `rhino_scene`/`gh_canvas` numeric params. Linked into `Cordyceps.Tests.csproj` (host-free: `System`/`System.IO` only — no `DebugLog`, no RhinoCommon, per the "linked code must stay host-free" learning). Plane construction stays in the handler because it needs `Rhino.Geometry` (not linkable into the test project).
- **Tests:** `PlaceImageValidationTests.cs` — happy path (real temp file, positive dims → null); missing path (null/empty/whitespace); non-existent file; `width`/`height` ≤ 0 and NaN; error-message contract per case.
- **Handler:** `ActionPlaceImage(...)` in `RhinoSceneTool` (new partial `RhinoSceneTool.PlaceImage.cs`, mirroring the `.Layers.cs` split):
  - active-doc guard → `PlaceImageValidation.Validate` → resolve/create target layer (current layer if `layer` null; find-or-create otherwise via a shared `FindOrCreateLayer` helper — see decision below).
  - `replace`: if `name` set, delete existing non-deleted objects whose `Attributes.Name` exactly matches `name` **on the target layer**, count them; if `replace=true` with no `name`, proceed but `replaced:0` + a `note` (per design doc).
  - build plane: `Plane.WorldXY`, set `Origin = (x,y,z)`, rotate `RhinoMath.ToRadians(rotation)` about `ZAxis` when rotation ≠ 0.
  - `AddPictureFrame(plane, path, asMesh, width, height, selfIllumination, embedBitmap)`; `Guid.Empty` → error.
  - post-add: duplicate attrs, set `LayerIndex` (+ `Name` if provided), `ModifyAttributes`; `Views.Redraw()`.
  - return `{ success:true, objectId, layer, replaced, note }` (note null unless the replace-without-name case).
- **Wiring:** add the new params to the `RhinoScene` method signature (doubles default `NaN`/`rotation=0`; bools as `true`/`false` strings parsed by `ToolHelpers.ParseBool`, matching the existing convention); add to `BuildParams` (NaN-excluded); add `place_image` to the dispatch switch; add the `place_image` `ActionInfo` (Required `path,x,y,z,width,height`; Optional `rotation,layer,name,replace,selfIllumination,embedBitmap,asMesh`; example; tips). Update the `[Description]` action list on the method + `GetServerInstructions()`.
- **Doc audit:** server instructions `rhino_scene` line; `rhino_scene` ActionInfo; root `CHANGELOG.md` (Added); `change-log.md` tagged entry. Knowledge guides: assessed — no existing rendering/scene guide documents a placement workflow that now lags; a one-line note is optional, not required (decide during audit, record the call).
- **Done when:** `dotnet test -c Release` green (new validation tests + 137 prior); Release build 0 warn/0 err; `releases/Cordyceps.gha` restored; `/prawduct:critic` run and blocking findings resolved; CHANGELOG + change-log + backlog updated; reflection captured.

### Decisions

- **z required (per spec).** The design-doc contract marks `x`,`y`,`z` all required. Honored as-written rather than silently making `z` optional with default 0. (If friction proves real in use, revisit with the requester — not a silent deviation here.)
- **`FindOrCreateLayer` extracted.** `ActionSetLayer` already contains the find-or-create-layer block; `place_image` needs the same. Extract a single private `FindOrCreateLayer(RhinoDoc, string)` and route both through it (behavior-preserving) rather than adding a third copy — avoids the duplication the Critic flags (Goal 7). Recorded as a deliberate, in-scope refactor.
- **`rotation` default 0** (real default; no NaN sentinel needed — 0 is a valid, meaningful value).

## Verification strategy

- **Host-free helper:** unit-tested (the testable seam the design doc calls for).
- **Handler (host-dependent — RhinoDoc, AddPictureFrame, ModifyAttributes):** cannot be exercised in a host-free unit test (project-preferences: document-touching behavior is verified live in Rhino, not unit-tested). Verified by: verify-api reflection (above) + Release build + Critic review; live Rhino verification is the operator step. State honestly if live verification is not performed this session.
- **After every build/test:** `git checkout -- releases/Cordyceps.gha` (post-build target restamps the tracked binary — learnings) and discard the regenerable `.prawduct/.work-model-index.json` before committing.

## Status

<!-- views_enabled: these checkboxes are a DERIVED VIEW. They stay [ ] on an unmerged
     branch and flip to [x] only via `regen-views` from change-log `status=shipped` tags
     at merge time. Live in-flight progress is tracked in the **Context** prose below. -->

- [ ] Chunk 01: place_image — action + PlaceImageValidation helper + doc audit

**Context:** Plan written; verify-api done (RhinoCommon reflection confirmed all three foreign APIs). Next: implement `PlaceImageValidation` + tests, then the handler + wiring, then doc audit, then build/test/Critic.
