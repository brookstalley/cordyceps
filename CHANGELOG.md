# Changelog

All notable changes to Cordyceps will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **`gh_inspect(action='connection')`** - Answers "are you alive?" without touching the Rhino UI thread, so it still replies while Grasshopper is mid-solve or otherwise wedged. Reports whether the solver is running and since when, whether the UI thread is responsive, and the server's own state. Previously a busy solver and a dead bridge were indistinguishable — both produced silence until the client timed out, and the natural reaction (retry, or issue another `recompute`) actively made things worse.
- **Every tool response now carries a compact `status` block** - Each result reports Rhino, Grasshopper and Cordyceps health plus the document the call acted on, so an agent can see a busy solver or a wedged UI thread without asking. Purely additive; when a tool already returns a top-level `status` field its own data is preserved and the host block moves to `host_status`.
- **`GET /health` reports host state** - The health endpoint gains a `host` object carrying the same Rhino/Grasshopper/Cordyceps state as the `connection` action, so a non-MCP caller (a monitor, a shell script) can poll liveness over plain HTTP. Note that `activeDocument` is now the last document seen by the UI-thread heartbeat rather than a live read — the endpoint no longer touches the Grasshopper document from its HTTP worker thread, which is what lets it answer while the host is busy. It is null only while nothing is known yet.
- **`gh_canvas(action='modifier')`** - Read or set per-parameter data modifiers (`mapping='none'|'flatten'|'graft'`, `simplify`, `reverse`) — the right-click options on component ports. Omitted fields are left unchanged, and supplying only `id`/`side`/`param` returns the current state. These are idiomatic Grasshopper and central to data-tree design; previously the only ways to get a Flatten or a Graft were to insert explicit `Flatten Tree`/`Graft Tree` components or to ask a human to right-click the port.
- **`gh_document(action='snapshot_delete')`** - Delete a named snapshot to free memory or make room under the snapshot cap. A missing name is an error, never silent success.

### Changed

- **The manual-install download is now the GitHub Release asset** - The README link pointed at `releases/Cordyceps.gha` committed on `main`. It now resolves to `/releases/latest/download/Cordyceps.gha`, which always serves the current release (and never a pre-release). The built `.gha` is no longer tracked in git: it is a build output, and `releases/` is gitignored.
- **`release.sh publish` builds the plugin it ships** - Publishing previously copied whatever `.gha` was sitting in the working tree into both the Yak package and the GitHub Release without building it, so the published binary's provenance rested on the file being tracked and the tree being clean. It now compiles from the release commit, which also lets a fresh clone of `main` publish.
- **CI uploads the built plugin** - `build-test` attaches `Cordyceps.gha` to each run on `develop`/`main`, so a build of any commit can be picked up without a .NET toolchain. Artifacts expire and need a GitHub login; releases remain the distribution channel.
- **`gh_canvas(action='info')` reports data modifiers** - Every parameter now carries a `modifiers` object (`mapping`, `simplify`, `reverse`). Modifier state was previously invisible to the API, so an agent could not detect that a port was grafted — it had to be inferred from downstream branch counts, and round-tripping a document silently lost the information.
- **`gh_document(action='recompute')` is rejected while a solution is running** - Instead of expiring objects inside the running solution, it now returns a structured `{success:false, solving:true, solving_since}` result naming how long the solve has been going. Stacking recomputes into an in-progress solve is what raised the modal breakpoint dialog described below.
- **`gh_inspect(action='status')` no longer hangs during a solve** - This action needs the UI thread, so it previously blocked for the entire duration of a long solve (one measured case: ~32 minutes of silence from a read-only probe). It now returns a busy/blocked result promptly. Use the new `connection` action for a probe that never needs the UI thread at all.
- **`gh_script(action='set'/'configure')` falls back when a component has no `SetSource`** - The write path now mirrors the probe cascade the read path already used, falling back to a writable `Code` property. When a visible code input parameter blocks the write, or no writable member exists, the error names the cause and what was probed instead of surfacing an opaque host exception.
- **Guidance no longer encourages renaming components** - All agent-facing guidance (server instructions, action help, embedded guides, prompt templates) now recommends annotating with labeled groups (plus panels/scribbles) instead of nicknaming components while building — renamed components are hard to find on the canvas and it is not the Grasshopper convention. The `rename` action and `nickname` parameter remain fully functional for explicit use.
- **One Ctrl-Z now reverts a whole MCP action** - Every mutating `rhino_scene`/`rhino_render` action and `gh_canvas(action='bake')` runs inside a single named undo record (`Cordyceps <action>`). Previously each internal step was its own undo entry, so undoing a bulk operation (e.g. `set_layer` on fifty objects) reverted one object at a time.
- **The snapshot store is bounded (max 20 snapshots, oldest evicted)** - Snapshots are full document serializations that previously accumulated unbounded for the life of the Rhino session. Saving a new name beyond the cap now evicts the oldest snapshot and reports it in the response's `evicted` field; re-using a name replaces that snapshot in place. `snapshot`/`snapshots` responses now include `maxSnapshots`, and listed snapshots carry `createdAtUtc`.
- **Server teardown no longer stalls the Rhino UI** - Stopping the MCP server (deleting the component, changing its port, or closing the document) while a request was in flight previously froze Rhino for ~2 seconds waiting on handlers that could not finish until the UI thread was released. The port is still freed immediately; the wait for in-flight requests now happens in the background.

### Fixed

- **MCP calls can no longer freeze the canvas with a "object expired during a solution" dialog** - Every tool call refreshed the Cordyceps component by expiring it immediately. When that landed while a solution was running, Grasshopper raised its modal breakpoint dialog ("The 'Cordyceps (MCP)' object expired during a solution"), which stops the canvas solving and makes every subsequent MCP call time out until a human clicks Close — fatal for unattended sessions. The refresh is now deferred to after the current solution, so the condition cannot arise from an MCP-initiated call. A burst of calls also coalesces into a single recompute instead of one per call.
- **Closing a Grasshopper document now stops the MCP server** - Previously the server only shut down when the component was deleted from the canvas; closing the `.gh` file left an orphaned server holding the port, so reopening the same file failed permanently with "port is already in use by another Cordyceps component" until Rhino restarted. The component now releases the server and port on document close/unload and restarts cleanly on reopen (including tab-switching between documents).
- **`gh_script(action='configure')` no longer wipes parameters on malformed JSON** - A syntax error in the `inputs`/`outputs` JSON was previously read as an empty list, which cleared every custom parameter and destroyed all wires while reporting `success:true`. Malformed JSON now returns `Invalid inputs/outputs JSON: ...` before anything is touched. A failed `code` update in a params+code call is also now machine-detectable (`codeSet:false` + `sourceError`, overall `success:false`) instead of being buried in a message string.
- **`gh_canvas(action='preview'/'enable')` report per-id results** - Unresolvable ids (and components that don't support preview/enable) were silently skipped with `success:true`; both actions now return per-id results like `delete`, with overall success false when any id fails.
- **`rhino_scene(action='layer_delete')` handles the current layer safely** - Deleting the current layer previously deleted/moved its objects first and then always failed (with a misleading error), leaving the document half-mutated; the destination layer could even be a child of the layer being deleted. The current layer is now reassigned and a valid non-descendant destination chosen *before* any mutation, and failures are diagnosed accurately.
- **`rhino_render(action='material_create')` actually applies PBR parameters** - `roughness`, `metallic`, `emission`, and `opacity` were set on a detached converted copy and never reached the document — every agent-created PBR material silently rendered as plain diffuse. They are now applied to the material that is added to the document; anything that can't be applied is reported in `notApplied` instead of silently dropped.
- **`rhino_scene(action='place_image', replace=true)` no longer destroys the old image on a failed add** - The previous picture was deleted before the new one was placed, so a failed placement (bad/corrupt image) lost the existing state; the new frame is now added first and old frames removed only on success. Attribute failures surface as a `warning` instead of being claimed.
- **Coordinates and slider values parse correctly on all locales** - Camera/light coordinate strings (`"10.5,0,3.2"`) and `gh_canvas(action='set')` slider values were parsed with the OS culture, corrupting values on comma-decimal locales (e.g. `10.5` → `105` on a German system). All numeric parsing is now culture-invariant.
- **Wrong-target operations now fail loudly instead of confirming** - `gh_wire(action='disconnect')` verified nothing and confirmed disconnecting wires that never existed (it now errors and lists the actual sources); `gh_canvas` group actions silently forked a new group on a typo'd `groupId`, swallowed malformed `ids` JSON, and let infrastructure-protection be bypassed via `group_rename`/`group_color`/`group_remove`/`group_add`; `gh_document(action='revert')` reported success with no canvas; `rhino_render(action='render', wait>0)` burned the full timeout then reported success on a non-Raytraced viewport; `rhino_render(action='sun')` ignored latitude/longitude/dateTime forever once manual azimuth/altitude had been used; garbage boolean strings silently mapped to true/false in `enable`/`preview`/`solver`; and `rhino_render` light actions accepted invalid input (bad location/target/color strings were silently skipped, `spotAngle` outside 0–90° passed through unchecked, and error messages named parameters that don't exist). All of these now return structured, actionable errors that validate before mutating.
- **Bulk operations report what they didn't do** - `rhino_scene` delete/hide/show/set_layer/set_name/set_color/select and `rhino_render` light_set/light_delete now return `notFound`/`notSelectable`/`failed` arrays and fail when nothing was affected, instead of `success:true` with a zero count. Bare `select` with no filter no longer silently selects the entire document (pass `type='all'` explicitly). Every `success:false` response now carries an `error` message (several view/material/env actions previously returned bare failures).
- **Nested layers work by full path** - Layer resolution now matches `Parent::Child` full paths, errors on ambiguous short names (listing candidates), and `set_layer`/`place_image` create nested hierarchies instead of failing on `::` in a layer name. `place_image` requires an absolute path (relative paths validated against the process CWD and broke later as linked textures).
- **MCP protocol robustness** - JSON-RPC response ids are echoed losslessly (large/fractional/string ids no longer destroy the response after the tool already executed, which caused clients to retry and double-apply mutations); argument binding/conversion failures return the standard `{success:false}` tool result instead of a raw `-32603` protocol error; whole-valued doubles like `300.0` are accepted for integer parameters; chunked request bodies are rejected instead of bypassing the size cap; `Accept: */*` is accepted; unfilled prompt-template placeholders render as an explicit `[argname]` marker instead of a bare word; ERROR-level log entries always reach the Rhino command line.
- **Deep data-flow hygiene** - `gh_document(action='clear')` inside a cluster editor no longer deletes the cluster's input/output hooks; bulk delete/enable/connect expire every affected component (not just the last), so cluster recomputes see all changes; `gh_inspect(action='trace')` no longer exposes Cordyceps infrastructure components and no longer NREs on phantom params; canvas/viewport capture no longer leaks bitmaps on save failure; component-registry caches no longer race or permanently cache a failed scan.

### Documentation

- **Agent-facing docs corrected to match the real tool contract** - A documentation audit fixed several places where embedded guides, tool help metadata, and server instructions had drifted from the actual code:
  - Getting-started guide: bulk-wire example now uses the real connection keys (`sourceId`/`sourceParam`/`targetId`/`targetParam` instead of the nonexistent `source`/`target`), the zoomable examples use real operations (`add`/`remove`/`set_count` with `side`/`index`/`count` — the documented `operation='list'` and `param=` never existed), and the `gh_wire` summary now lists the `clear` action.
  - Canvas-layout guide: the "Using Bounds" section now documents the real response shape (`bounds{x,y,width,height}` + `pivot{x,y}` from `action='bounds'`; list/find/info return pivot only) instead of nonexistent `right`/`bottom` fields.
  - Spacing guidance unified at **150px horizontal / 70px vertical** across all surfaces (canvas-layout and getting-started guides previously said 60-80px horizontal, contradicting the server instructions and tool help).
  - `gh_canvas(action='list')` help metadata now names the real `typeFilter` parameter (was `type`, which the tool does not accept).
  - `gh_document` undo/redo are now advertised as disabled (they are permanently-stubbed with a "use snapshots" error) in the tool help, server instructions, and README.
  - Prompt templates: `setup_script_component` no longer renders literal `{{...}}` malformed JSON (the renderer never unescaped doubled braces); `plan_definition`'s pattern step now reads the `gh://patterns/*` resources instead of misusing `gh_canvas(action='search')`; `debug_data_mismatch` names the real `gh_inspect(action='outputs')` fields (`branches`/`count`).
  - Component-not-found text now suggests `gh_canvas(action='search', query='...')` instead of the nonexistent `search_components` tool; rendering guide action lists now include `rhino_scene` `script` and `rhino_render` `env_delete`.
  - README: build command shows the required `-c Release` (Debug builds are blocked, and the csproj error message no longer claims `dotnet build` defaults to Release), the optional component input is correctly named **HttpPort**, and the tool count is stated accurately ("over 100 actions").

### Changed

- **README install instructions** - Documented installation via the Rhino Package Manager (run `PackageManager`, search "Cordyceps", install) as the recommended method, with manual `.gha` download kept as the secondary option. Clarified that file-unblocking (Windows) / quarantine-clearing (macOS) is only needed for manual installs, and added the macOS `xattr` quarantine-clear command.

## [1.4.12] - 2026-06-24

### Fixed

- **`gh_canvas(action='add')` now applies slider min/max/value/decimals** - Adding a Number Slider with `gh_canvas(action='add', type='slider', min=0, max=100, value=50, decimals=2)` previously ignored all four configuration params — the slider always landed at the default 0–1 range and 0.5 value, forcing a second `action='config'` call. The `add` dispatcher dropped the params before they reached the component. They are now applied on add (range first, then value, so the value isn't clamped to the old range), giving `add` parity with `config`; both share one `Core.SliderConfig` policy. Non-slider components ignore these params. (GHC-7X4B)
- **`gh_script(action='configure')` now preserves wires instead of destroying them** - `configure` previously unregistered *every* input and output parameter and re-registered them from scratch, so it silently dropped **all** wires on the component — even on parameters whose name didn't change — and returned no record of what was lost. It now reshapes parameters by name using the same LCS sync that `set` already used: parameters matching by name keep their connections, and wires on renamed/removed parameters are returned in a `lostConnections` array (with a `reconnectHint`), directly usable with `gh_wire(action='connect')` to restore them. `configure` is now also a **partial update** — omit a side (don't pass `inputs`/`outputs`) to leave it untouched, or pass `[]` to explicitly clear that side; previously, configuring only `inputs` would wipe all `outputs`. (GHS-3W9N)

## [1.4.11] - 2026-06-24

### Fixed

- **`gh_document(action='save')` can now overwrite an existing `.gh` file** - Saving over an already-existing `.gh` (binary) path previously failed every time with a bare `{"success": false, "error": "Failed to write file"}`, breaking incremental checkpoints, "save before mutating" safety nets, and any repeated save to the same path; only the first save (when the file did not yet exist) succeeded. The cause was a format-dependent overwrite flag — the `.gh` branch passed `overwrite=false` to GH_IO's `GH_Archive.WriteToFile`, while `.ghx` correctly passed `overwrite=true`. Both formats now save with `overwrite=true` (and `rememberPath=true`, matching Grasshopper's File→Save), so re-saving to the same path succeeds. Thanks to @anthonyesau for the detailed report (#14).
- **`gh_script(action='set'/'configure')` no longer silently leaves a Script component broken** - Setting a directive-less body on a bare unified **Script** component (which has no language until one is chosen) leaves it unable to compile — it fails at solve time with *"Can not determine input code language"* and emits no geometry. Previously `set`/`configure` returned `{"codeSet": true}` / a success message with no hint of the problem, so the natural retry (re-sending the same directive-less body) silently re-broke it. Both actions now return a `languageWarning` in that case, telling you to start your `code` with a directive (`#! python 3` / `// #! csharp`); doing so also recovers a component already in the broken state. The dedicated `C# Script` / `Python 3 Script` components are unaffected (they carry a concrete language and never need a directive). This narrows the remaining surface of the report by @anthonyesau (#15); the underlying language wipe is a Rhino-side behavior, but cordyceps no longer hides it.

## [1.4.10] - 2026-06-22

### Added

- **Place raster images** - New `rhino_scene(action='place_image')` action places an image file into the scene as a real Rhino PictureFrame object at a caller-specified origin (`x`/`y`/`z`), size (`width`/`height`, model units), and optional in-plane `rotation` (degrees, flat on world-XY). Auto-creates the target `layer`, sets the object `name`, and returns the new object id. Pass `replace=true` with a `name` to delete prior same-named objects on that layer first, making repeated parametric calls idempotent (returns a `replaced` count). Optional `selfIllumination` (default true), `embedBitmap`, and `asMesh` flags control appearance and storage.
- **PBR texture maps** - New `rhino_render(action='material_texture')` action to assign image-based texture maps to PBR material slots (base-color, roughness, metallic, bump, opacity, emission, displacement, ambient-occlusion, clearcoat, clearcoat-roughness). Supports UV tiling via `repeat` parameter and slot influence via `amount`. Omit `path` to remove a texture from a slot.
- **Material texture inspection** - `rhino_render(action='material_list')` now reports which PBR slots have textures assigned per material.

### Fixed

- **Server start failure now reports an actionable reason** - When the HTTP server can't bind its port (the port is held by another, non-Cordyceps application), the Cordyceps component previously showed a bare "NOT RUNNING" with no cause. It now surfaces the real reason on both the component's Status output and as a canvas error bubble — e.g. *"MCP server could not start on port 26929: … The port may be in use by another application — choose a different port (the HttpPort input) or close the process holding it."* (The existing "port owned by another Cordyceps component" message is unchanged.)
- **Clean server shutdown drains in-flight requests** - Stopping the server (removing the component, or changing its port) previously detached any request still being handled, and could null the shared document context out from under it — risking a crash in the worker thread. Shutdown now waits for outstanding request handlers to finish within a bounded budget before releasing shared state; a request that outlives the budget is detached safely and returns a structured *"server is shutting down"* result instead of faulting.
- **Concurrent commands are counted reliably** - The server's command counter and last-command label (shown on the Cordyceps component's Status output and in the `/health` response) were updated from concurrent HTTP worker threads without synchronization, so simultaneous requests could lose increments (an undercount) or read a stale last-command value. Counting now goes through a thread-safe helper (atomic increment + memory-barriered publish), so the count is exact and the last command is never torn. Snapshot storage (`gh_document` snapshot/revert/list) was likewise a plain dictionary written on the UI thread but listed off-thread; it is now a concurrent collection, so listing snapshots while one is being saved can no longer throw or read corrupt state. No change to any tool's behavior or output shape.
- **Server no longer hangs forever on a wedged operation** - Document access is serialized on Rhino's single UI thread; previously, if one operation never returned (e.g. an infinite-loop script component, or a modal dialog), the lock was held indefinitely and *every* subsequent MCP request blocked forever with no response, recoverable only by restarting Rhino. The lock acquire is now bounded: waiting requests fail fast with a structured `{"success": false, "error": "Document is busy…"}` result instead of hanging, so the server stays responsive. A re-entrancy guard also runs UI-thread-originated calls inline to avoid a self-deadlock. (RhinoCommon's `InvokeAndWait` cannot itself be cancelled, so a genuinely wedged operation still requires a Rhino restart to clear its holder — but it no longer takes the whole server down with it.)
- **MCP error contract honored at the server boundary** - Tool results carrying `{"success": false, ...}` are now reported to MCP clients with the transport `isError` flag set to `true` (previously hardcoded `false`, so failures looked like successes). Additionally, an exception thrown inside any tool method is now returned as a normal tool result (`isError: true`, body `{"success": false, "error": "<message>"}`) instead of escaping as a JSON-RPC `-32603` protocol error — making error reporting uniform across all seven tools. Genuine protocol errors (unknown tool, missing required parameter) still surface as JSON-RPC errors.
- **Script language preserved on `gh_script(set/configure)`** - Setting a script body without the Rhino 8 language directive (`#! python 3`, `// #! csharp`) no longer strips the component's language. Previously this caused "Can not determine input code language" at solve time and the component emitted no geometry — which bit anyone following the plain-body examples in the docs. The component's existing directive is now preserved automatically; a directive included in the new code is respected as-is.
- **`gh_inspect(action='docs')` flags failed introspection** - When a component's proxy can't be instantiated, the response now sets `paramsUnavailable: true` with an explanatory `note` instead of returning `success: true` with silently-empty `inputs`/`outputs`. Callers can now distinguish "component has no parameters" from "introspection failed" (the failure is also logged, retrievable via `gh_inspect(action='log')`). The field is absent when introspection succeeds, so the success-path response is unchanged.

## [1.4.9] - 2026-03-26

### Fixed

- **Type marshaling** - MCP clients that send string-encoded numbers (e.g., `"300"` instead of `300`) no longer cause errors. `ConvertJsonValue` now performs cross-type coercion for all primitive types. Fixes #13.

### Changed

- **Accurate MCP schema types** - Numeric tool parameters (`lens`, `wait`, `timeout`, `azimuth`, `sunAltitude`, `intensity`, `latitude`, `longitude`, `groundAltitude`, `shadowIntensity`, `spotAngle`, `xMin`/`yMin`/`xMax`/`yMax`, `limit`, `padding`, `width`, `height`) are now declared with their proper numeric types instead of `string`. The MCP schema now reports `"type": "number"` or `"type": "integer"` for these parameters.
- **JSON Schema integer distinction** - `GetJsonType` now maps `int`/`long` to `"integer"` (was `"number"`) per JSON Schema specification.

## [1.4.8] - 2026-02-10

### Changed

- **Developer documentation** - Added mandatory documentation audit checklist to CLAUDE.md. Every code change must now verify that tool help metadata, server instructions, knowledge base guides, resource registry, prompt templates, and CHANGELOG are updated as needed. Also documented the full user-facing documentation system (Knowledge/, ResourceRegistry, PromptRegistry, server instructions) in the Architecture section.

## [1.4.7] - 2026-02-05

### Fixed

- **Cluster-safe solution expiration** - Replaced all `ExpireSolution(true)` and `NewSolution(true)` calls with `ExpireSolution(false)` across all tools. When the cluster editor is open, the active document is the cluster's internal document; triggering a solution on it causes the parent to recreate the cluster, orphaning the editor and severing all `GH_ClusterInputHook` connections. Using `ExpireSolution(false)` marks the component as needing recompute without triggering a destructive solution cycle. Affects: `gh_canvas` (set, config, enable, add, delete, constant, zoomable), `gh_wire` (connect, disconnect, clear), `gh_script` (set, configure).

- **Surgical script parameter sync** - `gh_script(action='set')` no longer calls `SetParametersFromScript()`, which rebuilds all parameters from scratch and destroys cluster input hooks. Instead, uses LCS-based (longest common subsequence) diffing to identify unchanged parameters and only adds/removes what actually changed via `IGH_VariableParameterComponent`. Parameters whose names haven't changed retain all their connections.

## [1.4.6] - 2026-02-05

### Fixed

- **Cluster document corruption** - Fixed critical bug where modifying components inside clusters (scripts, values, wires, etc.) would corrupt cluster inputs, turning them all to null. Two issues fixed: (1) Operations now use the component's owning document via `OnPingDocument()` rather than assuming the active canvas document. (2) Changed `NewSolution(false)` to `NewSolution(true)` for incremental recomputation - the `false` parameter clears ALL volatile data including cluster input proxies. Affects: `gh_script`, `gh_canvas` (set, config, enable, delete, zoomable), `gh_wire` (connect, disconnect, clear).

## [1.4.5] - 2026-02-05

### Changed

- **README reorganization** - Improved structure and aesthetics:
  - Added Features section highlighting key capabilities
  - Added brief MCP explanation with link to modelcontextprotocol.io
  - Separated Installation from Usage with cleaner flow
  - Added Scripting section with Python example (MCP as protocol, not just for AI)
  - Collapsible sections for client-specific configuration
  - Simplified tool tables
  - Consolidated footer with Changelog link
  - Removed verbose resource URI listings

- **Knowledge base optimization** - 49% token reduction (1648 → 835 lines):
  - Converted verbose prose to tables throughout
  - Removed redundant tool parameter listings (use `action='help'`)
  - Eliminated basic Grasshopper knowledge LLMs already have
  - Consolidated duplicate workflow instructions
  - Optimized for LLM consumption, not human reading

## [1.4.4] - 2026-01-31

### Added

- **Object display color** - New `rhino_scene(action='set_color', ids='[...]', color='#FF0000')` action to set per-object display colors
- **Bounding box calculation** - New `rhino_scene(action='bbox', ids='[...]')` action returns combined bounding box with min, max, center, and size
- **Standard view presets** - New `preset` parameter on `rhino_render(action='camera')` for quick standard views: top, bottom, front, back, left, right, perspective, iso_nw, iso_ne, iso_sw, iso_se
- **Named views** - Save and restore camera positions:
  - `rhino_render(action='view_save', name='MyView')` - save current view
  - `rhino_render(action='view_load', name='MyView')` - restore saved view
  - `rhino_render(action='view_list')` - list all named views
  - `rhino_render(action='view_delete', name='MyView')` - delete a named view
- **Scene lighting** - Full control over scene lights:
  - `rhino_render(action='light_add', type='point', location='10,10,20')` - create point, spot, or directional lights
  - `rhino_render(action='light_list')` - list all lights
  - `rhino_render(action='light_set', ids='[...]', intensity='2.0')` - modify light properties
  - `rhino_render(action='light_delete', ids='[...]')` - delete lights

### Changed

- **Partial class refactoring** - Large tool files split into partial classes for maintainability:
  - `GhCanvasTool` → Groups.cs, Values.cs, Zoomable.cs
  - `RhinoRenderTool` → Lights.cs, Materials.cs, Settings.cs, Views.cs
  - `RhinoSceneTool` → Layers.cs
  - `GhDocumentTool` → Capture.cs

### Fixed

- **Script component type hints** - `gh_script(action='configure')` now correctly applies type hints to component parameters. Previously all parameters remained as "Generic Data" regardless of the types specified. Supports common types: int, double, bool, string, Point3d, Vector3d, Curve, Mesh, Brep, and more. (Closes #7)
- **Null reference in group operations** - Fixed potential null reference when matching groups by name/nickname in `gh_canvas` group actions
- **Null document in render wait** - Fixed potential null reference in `rhino_render(action='render')` wait loop when no document is active
- **Null light geometry** - Fixed potential null reference when modifying lights via `rhino_render(action='light_set')`
- **Consistent layer response fields** - Standardized layer visibility/locked field names across all layer actions

## [1.4.2] - 2026-01-31

### Added

- **Zoomable parameter management** - New `gh_canvas(action='zoomable')` action for managing variable-count parameters (ZUI components like Merge, Addition, etc.). Supports adding, removing, and listing zoomable inputs/outputs.

### Fixed

- **Phantom objects in find/list actions** - Fixed bug where orphaned objects from undo history could appear in component queries. Added `IsActiveDocumentObject()` and `GetActiveObjects()` helpers to filter objects by verifying their document reference matches the current document.

## [1.4.1] - 2026-01-31

### Added

- **Common Errors Guide** (`gh://docs/common-errors`) - New resource with comprehensive error→solution reference

### Fixed

- **Deadlock prevention in RefreshComponent** - UI thread invocation now uses fire-and-forget pattern to avoid deadlock when UI thread is waiting on the component lock
- **Document mutex for concurrent requests** - Added SemaphoreSlim to GrasshopperContext to prevent race conditions when multiple HTTP requests try to modify the document simultaneously
- **Thread-safe DebugLevel property** - Added volatile modifier to prevent stale reads across HTTP worker and UI threads

### Changed

- **Prompt tool names updated** - All prompts now use unified tool names (gh_canvas, gh_wire, etc.) instead of deprecated individual tool names
- **Server instructions clarified** - Added "VERIFY PERIODICALLY" guidance to help LLMs catch collateral errors in components they didn't directly modify
- **Documentation cross-references** - Added links to common-errors guide from GettingStartedGuide and BestPracticesGuide
- **Fixed tool count** - GettingStartedGuide now correctly says "7 tools" instead of "8 tools"

## [1.4.0] - 2026-01-30

### Added

**DebugLevel Input Parameter**
- CordycepsComponent now includes a DebugLevel input (Info, Warn, Error, Debug)
- Each HTTP request logs level, method, and endpoint
- Debug mode provides detailed timing and parameter information

### Changed

- **Consolidated to 7 unified tools** with action-based dispatch:
  - `gh_canvas` - Component operations (add, delete, move, find, search, list, info, bounds, validate, constant, bake, zoom, view, get, set, config, preview, enable, group_*)
  - `gh_wire` - Connection operations (connect, disconnect, validate)
  - `gh_document` - Document operations (info, save, clear, solver, recompute, undo, redo, snapshot, revert, snapshots, capture_*)
  - `gh_script` - Script component operations (get, set, configure, info)
  - `gh_inspect` - Inspection operations (status, outputs, warnings, trace, debug, log)
  - `rhino_scene` - Scene operations (objects, select, deselect, set_layer, set_name, layers, layer_*, hide, show, delete, script)
  - `rhino_render` - Render operations (display, camera, zoom, modes, render, settings, ground, sun, skylight, material_*, env_*)

### Deprecated

- Individual tools (gh_add, gh_connect, gh_info, etc.) replaced by unified action-based tools

## [1.3.0] - 2026-01-29

### Added

- Rhino scene and render tools for object management and viewport control

## [1.2.0] - 2026-01-28

### Added

- Script component support (C# and Python)
- Capture tools for canvas and viewport screenshots

## [1.1.0] - 2026-01-27

### Added

- Group management tools
- Undo/redo support with snapshots

## [1.0.0] - 2026-01-26

### Added

- Initial release
- Core Grasshopper component operations
- MCP server implementation
