# Build Plan — janitor-2026-07-02 (full reliability audit execution)

Branch: `chore/janitor-2026-07-02` (off `develop`)
Scope: execute the approved findings of the 2026-07-02 janitor audit (quality, bugs,
consistency, gaps). Survey ran as 6 parallel investigations; user approved chunks 1–5
(hygiene, doc contract, HIGH bugs, MEDIUM sweeps, testability). Larger redesigns are
filed to the backlog, not built here.
Critic mode: chunk (per code chunk) + cumulative before PR.

## Context / decisions
- Prior plan (gitflow-release-refactor) was stale-complete: merged via PR #22, gated by
  its cumulative Critic; change-log entries correctly `status=merged` (flip to shipped at
  next release). Replaced by this plan per the janitor stale-plan rule.
- Baseline: 224/224 tests green (519 ms); plugin Release build 0 warnings; develop pulled
  to 7eb9e09.
- Deliberate non-goals (filed to backlog instead): Stop() drain threading topology
  (pairs with MCP-9F3Q), Rhino undo-record grouping, nested-layer resolution redesign,
  VRF-001..005 live burn-down (needs operator in Rhino), api-contract.md artifact,
  test-package version bumps, releases/.gha history strategy, Tools/Unified flatten.

## Confidence check
1. Problem: organic growth left silent-success bugs in the tool layer, drifted agent-facing
   docs, and governance/verification debt — user wants "ultra reliable and stable".
2. Success: every approved finding fixed with regression tests where host-free; docs match
   dispatch reality; repo hygiene clean; suite green; Critic clean per chunk.
3. Out of scope: new features, redesigns listed above, applying branch protection, releases.

## Chunks

### Chunk 01: Repo + governance hygiene (no product code)
- [x] Commit the stranded `.work-model-index.json` untrack + gitignore entry.
- [x] Delete merged local branches (docs/readme-refresh, bug-report,
      docs/rhino-scriptcomponent-languagespec) and merged remote topic branches
      (feature/gitflow-release-refactor, fix/add-slider-params-and-configure-wires,
      fix/gh-save-overwrite-and-script-language, fix/ops-safety-stage1,
      fix/mcp-error-contract, chore/janitor-2026-06-20, docs/readme-refresh,
      docs/rhino-scriptcomponent-languagespec). All verified merged via PRs #17–#24.
- [x] Archive `incoming-bugs/place-raster-image-picture-frame-action.md` (feature shipped
      as RSC-2H9K); resolve the `report-bug` advisory.
- [x] Mark `docs/place-image-action.md` as shipped/archival at top.
- [x] Delete 4 unreferenced images (~7 MB): cordyceps_showcase_trimmed.gif,
      cordyceps_logo.png, cordyceps_icon_large_transparent.png, cordyceps_icon_24.png.
- [x] sln: remove stale untracked `cordyceps.sln` (gitignored by `*.sln`, missing the test
      project; both csprojs build directly). Note decision here.

### Chunk 02: Documentation-contract fixes (docs/metadata text only, no behavior)
- [x] GettingStartedGuide.md:25 — bulk-wire example keys → sourceId/sourceParam/targetId/targetParam.
- [x] GettingStartedGuide.md:46-49 — zoomable examples: remove invalid operation='list' and
      nonexistent param=; use add/remove/set_count with side/index/count. Also :12 add `clear` to gh_wire summary.
- [x] CanvasLayoutGuide.md:49-52 — remove nonexistent right/bottom response fields; document
      bounds{x,y,width,height}+pivot layout math.
- [x] Spacing guidance: pick 150px horizontal / 70px vertical (matches server instructions +
      gh_canvas tips) and align CanvasLayoutGuide + GettingStartedGuide + PlanDefinition.md.
- [x] gh_canvas list ActionInfo: `type` → `typeFilter` (code alias lands in Chunk 04).
- [x] Undo/redo: add "currently disabled — use snapshot/revert" to GhDocumentTool ActionInfo,
      server instructions (McpServer.cs:593), README.md:150.
- [x] ResourceRegistry.cs:439 — `search_components` → gh_canvas(action='search').
- [x] RenderingGuide.md — add env_delete (:49) and `script` to rhino_scene list (:38).
- [x] DebugDataMismatch.md:16 — branchCount/dataCount → branches/count.
- [x] SetupScriptComponent.md — fix {{ }} brace-escaping (renders literally; GetPrompt does
      plain Replace).
- [x] PlanDefinition.md:14-18 — point "Check for Patterns" at gh://patterns/* resources.
- [x] README.md — build command add `-c Release` (:179); csproj:21 BlockDebugBuilds message
      corrected; "Port" → "HttpPort" (:47); "110+ actions" → accurate count (:142).
- [x] CHANGELOG under `## [Unreleased]`.

### Chunk 03: HIGH code bugs (each with host-free regression tests where possible)
- [x] H1 CordycepsComponent: override DocumentContextChanged; stop server + release port on
      Close/Unloaded (fixes orphaned listener + permanently-bricked port on file reopen).
- [x] H2 GhScriptTool.ParseParamDefs: malformed inputs/outputs JSON → structured error before
      any mutation (never conflate unparseable with empty). Test via ParamSyncPlan-level seam
      or extracted pure parser.
- [x] H3 GhCanvasTool preview/enable: per-id results like delete; success only if all resolve.
- [x] H4 RhinoSceneTool layer_delete: validate + reassign current layer and pick a
      non-descendant destination BEFORE mutating objects.
- [x] H5 RhinoRenderTool material_create: apply PBR params to the material actually added to
      the doc (BeginChange/EndChange on a PBR RenderMaterial, or Material.ToPhysicallyBased path).
- [x] H6 place_image replace=true: add new frame first, delete old ones only on success.
- [x] Doc-audit each (ActionInfo tips where behavior surface changed).

### Chunk 04: MEDIUM sweep — Grasshopper tools + MCP boundary
- [x] gh_canvas list: dispatch alias `typeFilter ?? type`; search: seed providedParams so
      query-or-type works as documented (or fix description).
- [x] gh_wire disconnect: error when wire didn't exist.
- [x] zoomable add: use indexed RegisterInput/OutputParam overloads.
- [x] Slider set: route value parse through invariant culture (SliderConfig path); config
      reports unparseable value instead of silently ignoring.
- [x] Group protection: TryGetUnprotectedComponent* in group_remove/rename/color; filter
      infraIds from member lists in group_create/group_add; group_add with unresolvable
      explicit groupId errors instead of forking a new group; group_create errors on invalid
      ids JSON (parity with group_add).
- [x] gh_document revert: error when no active canvas. clear: preserve cluster IO hooks (or
      refuse inside cluster editor with clear error).
- [x] Bulk expire: expire every mutated object (delete/enable/wire connect), not just the last.
- [x] gh_script configure params+code path: surface SetSource failure machine-readably.
- [x] Capture: using/try-finally around bitmaps (3 sites).
- [x] Boundary (McpServer): echo JSON-RPC id losslessly + build before dispatch; wrap
      binding/conversion in the structured-error path; JsonTypeConverter coerces
      whole-valued doubles for int/long; chunked-body reject (ContentLength64 < 0);
      Accept */* allowed; DebugLog.Error at level 0.
- [x] PluginRegistry: publish cache only when fully built; no permanent caching of failed
      scan. DeprecationRegistry: initialized=true in finally; volatile.
- [x] UnifiedToolHelpers action validation case-insensitive; strict bool parse helper used by
      gh_canvas enable/preview + gh_document solver (error on garbage).
- [x] gh_inspect trace: filter infraIds; guard Attributes?.GetTopLevel?.DocObject (also
      GhWireTool:302); direction null-safe.
- [x] Doc-audit all of the above (ActionInfo/server instructions/CommonErrors as touched).

### Chunk 05: MEDIUM sweep — Rhino tools
- [ ] TryParsePoint3d: InvariantCulture (fixes camera/light corruption on non-US locales).
- [ ] select: count only successful Select(); require ≥1 filter (error on bare select-all).
- [ ] light_set: validate inputs up front; honor Modify() return; error field on failure.
      light_add: correct param names in errors; validate spotAngle 0–π/2; reject degenerate
      direction vectors.
- [ ] render wait>0: up-front doc/view/Raytraced validation before the poll loop.
- [ ] sun: lat/long/dateTime turn ManualControlOn off (and report mode).
- [ ] Missing error fields on success:false (view_save/load/delete, material_delete,
      env_delete); view_save drop redundant pre-delete (Add replaces).
- [ ] material_apply: legacy Materials.Find(name) fallback (parity with delete).
- [ ] FindByLayer null guards (3 sites); objects truncated flag off-by-one + limit clamp.
- [ ] place_image: absolute-path rule in PlaceImageValidation (+test); check
      ModifyAttributes/FindId result and surface partial failure.
- [ ] Layer name matching: FullPath first, then short name, error on ambiguity;
      FindOrCreateLayer creates nested hierarchy for `A::B` paths.
- [ ] Doc-audit (rhino_scene/rhino_render ActionInfo, RenderingGuide).

### Chunk 06: Testability extraction + test hygiene
- [ ] Extract host-free helpers from ToolHelpers.cs into linkable file(s)
      (Core/ParseHelpers.cs + Core/ResponseHelpers.cs or similar): TryParseGuid,
      Success/ErrorResponse, TryDeserializeList/Array, TryParseGuidArray, ColorToHex,
      TryParseColor, ParseBool, TryParsePoint3d. Link + table-driven tests.
- [ ] Extract ConvertToSnakeCase → Core/McpNaming.cs; pin the 7 tool names as contract tests.
- [ ] PromptRegistry.GetPrompt: extract substitution as pure static; FIX the placeholder bug
      (unfilled {goal} currently renders as literal "goal"); decide rendered form; tests.
- [ ] Rename McpServerTypeTests.cs → JsonTypeConverterTests.cs; fix xUnit1031 blocking waits
      (CommandStatsTests:88, InFlightRequestsTests:100-101); harden the
      InFlightRequestsTests 250 ms snapshot race; drop hard-coded line/count refs in comments.
- [ ] DebugLog: swappable console sink so ring buffer/level gating become testable (+tests).

### Close-out
- [ ] Backlog: file deferred items; update GHC-2N8K (resolved if dedup lands via Chunk 04),
      note GHS-4D8M upstream filing still pending (user action).
- [ ] Cumulative Critic; reflection; change-log entries per chunk (scope=janitor-2026-07-02).
