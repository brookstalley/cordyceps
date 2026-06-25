# Backlog — Cordyceps

<!-- Structured backlog (Prawduct v1.7+). Managed with the `/backlog` skill:
     /backlog            summary + menu
     /backlog pick       what to work on next (filters + natural language)
     /backlog add        file a new item (searches for duplicates first)
     /backlog find <q>   search title/metadata/body
     /backlog list       tabular view (default: open, added within 90d)
     /backlog update ID  change metadata or status
     /backlog migrate    convert legacy unstructured items to this format

     Items move between the three sections below via `/backlog update ID status=...`.
     The framework never infers status from build plans or change logs — an agent
     or human makes the call explicitly (see backlog-system-requirements.md D4/§5).

== Item shape ==

  - **[PFX-XXXX]** One-line title
    `effort: M · impact: M · area: stop-hook · source: reflection · added: 2026-05-29 · status: open`

    Free-form body of any length — a single sentence or multi-paragraph analysis
    with file refs, fix-shape, and open questions. The author chooses what fits.

  ID format `[PFX-XXXX]`:
    PFX = 2–3 uppercase letters naming the work-space the item was filed from.
          Derive a sensible prefix from the item's area; reuse existing ones so
          related items share a prefix. Starter vocabulary (extend freely):
            STH stop-hook · CRT critic · SYN sync · LLM prompt/LLM · BKL backlog
            MIG migration · JNT janitor · MET methodology · DOC docs · TST tests
          A project may optionally declare its prefix vocabulary as
          `backlog_prefixes:` in project-state.yaml for validation — not required.
    XXXX = 4-char random alphanumeric (base36). Random IDs avoid cross-branch
           collisions; ~1.7M combinations per prefix.

  Metadata bar (one backticked, dot-separated line; required on new items):
    effort: S | M | L     S = <30 min · M = hours · L = multi-chunk
    impact: S | M | L     S = cosmetic · M = quality-of-life · L = user-felt/structural
    area:   <tag>         free-form topic tag; reuse existing tags to enable grouping
    source: builder | critic | reflection | janitor | user
    added:  YYYY-MM-DD
    status: open | promoted | shipped | dropped
  Optional, on the same line (distinct concepts — keep them straight):
    related:   PFX-XXXX, PFX-XXXX   cross-references to related items
    closes:    PFX-XXXX             this item supersedes another backlog item (item → item)
    closed-by: <chunk-id|tag>       what shipped this item, set on status=shipped (item → release)
    reviewed:  YYYY-MM-DD           last-touched timestamp (auto-set on any update)
    accepted-by: @actor             soft claim "someone is on this" so others don't
                                    double-pick; pick/list exclude claimed items.
                                    Does NOT auto-expire; auto-cleared on ship/drop.
                                    Not a lock (backlog.md is eventually-consistent).
    stage: <lifecycle>              idea | research | requirements | design | ready.
                                    Where the item sits in the feature lifecycle;
                                    only `ready` is implementable. Absent/early =>
                                    pick routes to discovery/planning, not code.
    refs: <doc#section>, <doc>      links to governing artifacts (requirements /
                                    arch / design docs). Distinct from `related:`
                                    (which is item -> item).

  Legacy items (no metadata) remain valid — tools treat them as
  `effort: ? · impact: ? · area: untagged · status: open` and rank them lower.
  Run `/backlog migrate` to add structure at your own pace; nothing is forced. -->

## Open

<!-- Items available to pick up. -->

- **[MCP-9F3Q]** Introduce a ServerState enum as the single source of truth for server lifecycle
  `effort: M · impact: M · area: mcp-server · source: critic · added: 2026-06-21 · status: open · stage: ready`

  Stage-1 cumulative Critic NOTE (non-blocking, forward-looking, on Chunk 01/02 code). Server
  lifecycle state in `McpServer` is currently reconstructed from 3 interdependent signals —
  `IsRunning` + `StartError` + `_context` — with no single source of truth. No invalid combination
  is currently reachable, but Stage 2+ will add conditions that increase the combinatorial surface.

  **Fix shape:** introduce a `ServerState` enum (e.g. Stopped/Starting/Running/Failed) as the single
  source of truth, and derive `IsRunning`/`StartError` from it, before that Stage 2+ complexity lands.
  Refactor, behavior-preserving. Doc-audit: internal lifecycle only; check whether any tool surfaces
  server status to clients.

- **[MCP-5T7W]** Decide + test InFlightRequests.DrainWithin timeout-coincident-with-fault behavior
  `effort: S · impact: M · area: mcp-server · source: critic · added: 2026-06-21 · status: open · stage: ready`

  Stage-1 cumulative Critic NOTE (non-blocking, forward-looking, on Chunk 01/02 code).
  `Core/InFlightRequests.DrainWithin` returns `true` on any `AggregateException`, which can mask a
  budget-timeout that coincided with a handler fault. The masking loses only a WARN log — correctness
  is still protected by the `_context == null` guard — but the timeout+fault combination is currently
  untested.

  **Fix shape:** make an explicit decision about the correct return value when a drain timeout
  coincides with a handler fault, then add a regression test covering that combination. Touches
  `Core/InFlightRequests.cs` + the test project.

- **[GHD-6M2J]** Bound or evict GhDocumentTool snapshot store (unbounded process-lifetime)
  `effort: S · impact: S · area: gh-document · source: critic · added: 2026-06-21 · status: open · stage: ready`

  Stage-1 cumulative Critic NOTE (non-blocking, forward-looking, low priority). `GhDocumentTool._snapshots`
  is an unbounded process-lifetime store (pre-existing; Chunk 03 only changed the collection type for
  thread-safety). Snapshots accumulate for the life of the Rhino session with no eviction or cap.

  **Fix shape:** consider a bound (max snapshot count) or an eviction policy (e.g. LRU / oldest-first).
  Gated by explicit user action, so memory growth is operator-driven and low priority. Doc-audit: if a
  cap is introduced, check `gh_document` snapshot ActionInfo for the new limit semantics.

- **[GHS-4D8M]** gh_script(set/configure) silently succeeds when it leaves a Script component unable to determine its language (Rhino LanguageSpec wipe — upstream)
  `effort: M · impact: M · area: gh-script · source: user · added: 2026-06-24 · status: open · stage: ready · reviewed: 2026-06-24 · related: GHS-7K2P · refs: issue #15, docs/upstream-rhino-scriptcomponent-languagespec.md`

  `gh_script(set/configure)` silently returns `{"success": true}` in a case where it leaves a unified
  `ScriptComponent` unable to determine its language — the component then fails at solve time (the same
  class of failure as GHS-7K2P, but via a different mechanism). A partial fix landed this session: a
  `languageWarning` guard was added so the tool now surfaces a warning instead of silently reporting
  success (issue #15, partial).

  **[2026-06-24] Investigation + upstream report drafted.** A McNeel bug report is drafted at
  `docs/upstream-rhino-scriptcomponent-languagespec.md`, pending filing on McNeel Discourse/YouTrack
  (next action). Investigation findings:
  - The unified `ScriptComponent` silently loses its language on a **directive-less `SetSource`** — the
    error surfaces only at solve time, **not** from `SetSource` itself.
  - It **IS recoverable** via a directive-bearing `SetSource` — this **corrects the original
    "permanently broken" claim**.
  - Setting `LanguageSpec` via reflection **does not stick**.

  **Cordyceps mitigation shipped in v1.4.11:** the `languageWarning` guard + directive preservation
  (the latter via `Core/ScriptDirective.cs`, GHS-7K2P). The remaining root cause — Rhino's
  `LanguageSpec` being wiped — is **upstream in Rhino 8's `ScriptComponent` and is not
  cordyceps-fixable**. This item now tracks the upstream report through filing.

  **Next action:** file the drafted report with McNeel (Discourse / YouTrack). Related to GHS-7K2P
  (the directive-preservation fix via `Core/ScriptDirective.cs`); distinct mechanism, same `gh-script`
  area. Doc-audit: if the warning surfaces in tool responses, check `gh_script` ActionInfo + server
  instructions.

- **[GHC-2N8K]** De-duplicate the host-bound slider-apply block shared by gh_canvas add and config
  `effort: S · impact: S · area: gh-canvas · source: critic · added: 2026-06-24 · status: open · stage: ready · related: GHC-7X4B, GHS-3W9N`

  NOTE from the cumulative Critic review of GHC-7X4B / GHS-3W9N; user asked to track it. The pure
  decision logic is already shared via `Core/SliderConfig` (`SliderConfig.Plan(...)` → `SliderConfigPlan`),
  but the ~4-line **host-bound apply step** — set Minimum, then Maximum, then DecimalPlaces (and value)
  on the live `GH_NumberSlider` — is copy-pasted verbatim in two sites:
  - `GhCanvasTool.cs` `ActionAdd` (~lines 460-463)
  - `GhCanvasTool.Values.cs` `ActionConfig` (~lines 125-128)

  **Risk:** low but real — a future change to the apply sequence (e.g. ordering, or a new slider
  property) could be made in one site and missed in the other.

  **Fix shape:** extract the apply into a single helper (e.g. `SliderConfig.ApplyTo(slider)`, or a small
  host-side method both call) so add and config share one apply path as well as one plan path.

  Low divergence risk today and no user-facing payoff, so it sat below the earn-the-entry bar until
  requested. Add and config are currently verified to produce identical sliders by the
  operator-verification parity check (VRF-004) — that parity is what this item protects against future
  drift. Doc-audit: internal refactor, behavior-preserving; no user-facing surface expected to change.

## Promoted

<!-- Items currently being addressed in an active build plan. /backlog pick
     skips these by default (work is already in flight). -->

_(none currently in flight)_

## Archive

<!-- Shipped and dropped items, kept for searchability. Never deleted. -->

- **[GHC-7X4B]** gh_canvas(action='add') silently drops slider min/max/value/decimals
  `effort: S · impact: M · area: gh-canvas · source: user · added: 2026-06-24 · status: shipped · stage: ready · reviewed: 2026-06-24 · closed-by: fix/add-slider-params-and-configure-wires`

  Symptom: `gh_canvas(action='add', type='slider', min=0, max=100, value=50, decimals=2)` ignores
  min/max/value/decimals. The new Number Slider lands at the default 0–1 range and 0.5 value
  regardless of what the caller passed.

  Root cause (verified in current source): the public `GhCanvas` method
  (src/Cordyceps/Tools/Unified/GhCanvasTool.cs:296-298) declares `min`, `max`, `value`, `decimals`
  as optional parameters, but the `add` dispatcher calls `ActionAdd(type, x, y, nickname)` (~line 367)
  and `ActionAdd` (~line 401) accepts only those four — the slider params are silently dropped before
  they reach the component.

  Fix shape (clean-room — mirrors our OWN existing code): forward min/max/value/decimals into
  ActionAdd, and after `doc.AddObject` apply them to GH_NumberSlider exactly as the existing `config`
  action already does in GhCanvasTool.Values.cs (ActionConfig). The goal is parity: `add` should
  configure a slider the same way `config` already can. Non-slider components must ignore the params
  gracefully.

  Verification: unit test that adds a slider with explicit range/value/decimals and asserts the
  created slider reflects them; confirm non-slider add still works. Doc audit: the `add` ActionInfo
  should list min/max/value/decimals as optional with a Tip about slider configuration.

  Acceptance: add-with-slider-params yields a correctly-configured slider; tests + help metadata
  updated.

  **[2026-06-24] Shipped:** bugfix resolved on branch `fix/add-slider-params-and-configure-wires`
  (slider params now forwarded into `ActionAdd` and applied via `Core/SliderConfig.cs`).
  Critic-approved; 224 tests green. Archived as part of the closing PR.

- **[GHS-3W9N]** gh_script(action='configure') destroys all wires instead of preserving by name
  `effort: M · impact: L · area: gh-script · source: user · added: 2026-06-24 · status: shipped · stage: ready · reviewed: 2026-06-24 · closed-by: fix/add-slider-params-and-configure-wires · related: GHS-4D8M, GHS-7K2P`

  Symptom: `gh_script(action='configure', ...)` drops EVERY wire on the script component — including
  params whose names did not change — and returns no `lostConnections` array. This silently discards
  the user's wiring. The sibling `set` action behaves correctly: it keeps wires on name-matched params
  and reports the rest in `lostConnections`.

  Root cause (verified in current source): `ConfigureViaVariableParams`
  (src/Cordyceps/Tools/Unified/GhScriptTool.cs:806) unconditionally unregisters every input/output
  param (~lines 816-825) and re-registers them, so all connections are lost. The `set` path instead
  routes through the LCS-based `SyncParamSide`/`SyncScriptParams` helper (~lines 528 / 471), which
  preserves wires on unchanged names and collects removed-param wires into `lostConnections`.

  Fix shape (clean-room — uses our OWN existing helper): make `configure` perform its param sync
  through the same `SyncParamSide` helper that `set` already uses, so unchanged-name params keep their
  wires and removed-param wires surface in `lostConnections` (identical response shape to `set`,
  directly usable with gh_wire(action='connect') to restore). Target: configure and set should
  preserve wiring identically.

  Open design question (decide during build, from our own API surface — not required for the core fix):
  should `configure` be a true partial update that distinguishes "param side omitted → leave it alone"
  from "empty list passed → clear that side"? Flag it; don't assume.

  Verification: regression test — configure a script that already has wires, assert name-matched params
  keep wires and removed params appear in lostConnections. Doc audit: the `configure` ActionInfo Tips
  and Knowledge/GettingStartedGuide.md should describe wire preservation + lostConnections (today only
  `set` is documented — GettingStartedGuide.md:37).

  Acceptance: configure preserves wires like set; lostConnections returned for dropped params;
  regression test + docs/help updated.

  **[2026-06-24] Shipped:** bugfix resolved on branch `fix/add-slider-params-and-configure-wires`
  (`configure` now syncs params through the shared LCS helper, preserving name-matched wires and
  surfacing removed-param wires in `lostConnections`; see `Core/ParamSyncPlan.cs`). Critic-approved;
  224 tests green. Archived as part of the closing PR.

- **[GHD-8P4N]** gh_document(save) cannot overwrite an existing .gh file
  `effort: S · impact: M · area: gh-document · source: user · added: 2026-06-24 · status: shipped · stage: ready · reviewed: 2026-06-24 · closed-by: gh-document-save · refs: issue #14`

  Reported by @anthonyesau (#14). `gh_document(action='save')` could not overwrite an existing
  `.gh` (binary) file — every repeated save returned a bare `{"success": false, "error": "Failed to
  write file"}`, breaking incremental checkpoints, "save before mutating" safety nets, and any
  repeated save to the same path; only the first save (when the file did not yet exist) succeeded.
  Root cause was a format-dependent overwrite flag: the `.gh` branch passed `overwrite=false` to
  GH_IO's `GH_Archive.WriteToFile`, while `.ghx` correctly passed `overwrite=true`.

  **[2026-06-24] Shipped (this session):** save policy extracted to a pure, host-free helper
  `Core/GhArchiveSave.cs` returning `overwrite=true, rememberPath=true` for both formats (matching
  Grasshopper's File→Save semantics); `GhDocumentTool.cs` now calls it. 15 unit tests in
  `GhArchiveSaveTests.cs`, including a regression guard for the format-dependent overwrite. CHANGELOG
  + change-log entries added. Live re-verification against the running Cordyceps MCP server still
  pending (host-dependent save handler, can't be unit-tested).

- **[RSC-2H9K]** Native place-raster-image / PictureFrame action (rhino_scene)
  `effort: M · impact: M · area: rhino-scene · source: user · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-21 · closed-by: ceab6e0 · refs: docs/place-image-action.md`

  External feature request filed by the Puzzles project (print-and-cut puzzle generator, Chunk 06):
  a first-class action to place a raster image into the live Rhino scene as a PictureFrame object at
  a caller-specified placement, returning the new object id — to preview a cut layout over printed
  artwork. Discovery (2026-06-21) resolved the contract in `docs/place-image-action.md`: tool =
  `rhino_scene(action='place_image')`; placement = origin (x,y,z) + width + height + optional
  Z-rotation, flat; units = model units; idempotent `replace` (default false) matches by `name` on
  the target layer.

  **[2026-06-21] Shipped:** built on `feature/place-image-action`. New
  `rhino_scene(action='place_image')` + host-free `Core/PlaceImageValidation.cs` (12 unit tests) +
  shared `FindOrCreateLayer` (extracted from `set_layer`). Build-time `verify-api` reflection
  confirmed `AddPictureFrame`/`Plane.Rotate`/`RhinoMath.ToRadians` on Rhino 8 RhinoCommon (no
  `ObjectAttributes` overload → layer/name set post-add). Critic `final` clean (0 findings) over the
  branch; Release build 0/0; 149 tests pass. Doc-audit: server instructions, `rhino_scene`
  ActionInfo, root CHANGELOG, change-log, RenderingGuide. Fast-forward merged direct to main
  (`ceab6e0`). **Live Rhino operator verification still pending** (host-dependent handler, can't be
  unit-tested per project-preferences).

- **[CQ-7T4P]** gh_inspect(docs)/component search returns success:true with empty params when a proxy can't be instantiated
  `effort: S · impact: S · area: code-quality · source: critic · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-21 · closed-by: d1e1787 · related: CQ-2X8B, CQ-5J9N`

  Surfaced by the cumulative Critic on the CQ-2X8B refactor. `ToolHelpers.WithProxyComponent` (and
  its callers `GhInspectTool.ActionDocs` + `ComponentRegistry.CreateComponentMatch`) now LOG when
  `proxy.CreateInstance()` fails, but the tool result still returns `success: true` with an empty
  inputs/outputs list — the caller can't distinguish "component genuinely has no params" from "we
  failed to introspect it."

  **Fix shape:** surface a result-level signal (e.g. a `"paramsUnavailable": true` flag or a `note`
  field) when `WithProxyComponent`'s callback didn't run. This is a behavior change to the tool
  response, so it was deliberately kept out of the behavior-preserving CQ-2X8B refactor. Builds on
  the logging added by CQ-5J9N. Doc-audit: `gh_inspect` ActionInfo if the response shape changes.

  **[2026-06-21] Shipped:** built on `fix/proxy-params-unavailable` (commit `ccd8e1d`); Critic
  `final` + `verify-resolutions` clean (the final pass caught a third params-surfacing path,
  `gh://component/{name}`, fixed in-chunk). Pushed direct to main (`d1e1787`). 137 tests pass.
  Doc-audit done: root CHANGELOG + `gh_inspect` `docs` ActionInfo.

- **[MCP-4R2K]** Honor the MCP error contract at the server boundary
  `effort: M · impact: L · area: mcp-server · source: reflection · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: #18`

  Two related defects in `src/Cordyceps/McpServer.cs` break the MCP error contract for tool calls:

  (a) **Hardcoded `isError = false`** (~`:645`): tool results carrying `{"success": false, ...}` are
  reported to MCP clients as non-errors. The `isError` flag should be derived from the parsed
  `success` field of the tool's JSON payload so clients can distinguish failures.

  (b) **Inconsistent exception handling across tools.** Most tool dispatch methods don't wrap their
  action switch in try/catch, so a thrown exception escapes as a raw JSON-RPC `-32603` error (caught
  at `McpServer.cs:433`) instead of the standard `{success:false, error}` tool payload. `GhScriptTool`
  DOES wrap (~`:307`), so behavior is inconsistent across the 7 tools for the same failure class.

  **Fix:** derive `isError` from the parsed `success` field, and apply consistent
  try/catch-to-structured-error handling — either centrally at the server boundary or uniformly in
  each tool. Doc-audit: behavior change to the error contract; check whether server instructions /
  MCP testing guide describe error reporting.

  **[2026-06-20] Promoted:** picked into the active build plan (artifacts/build-plan.md) on branch
  `fix/mcp-error-contract`; shipping as one PR with DOC-8M3T, TST-6W7H, CQ-2X8B, CQ-5J9N.

  **[2026-06-20] Shipped:** merged to main via PR #18 (squash f9e0663).

- **[DOC-8M3T]** GetServerInstructions() missing 11 live actions
  `effort: S · impact: M · area: documentation · source: reflection · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: #18`

  `McpServer.cs` `GetServerInstructions()` (the first thing agents see on MCP initialize) lags the
  code. Missing actions:
  - `gh_canvas` missing `zoomable` (~`L552`)
  - `rhino_scene` missing `set_color`, `bbox` (~`L557`)
  - `rhino_render` missing `view_save`, `view_load`, `view_list`, `view_delete`, `light_add`,
    `light_list`, `light_set`, `light_delete` (~`L558`)

  All 11 actions exist in code, in per-tool `ActionInfo`, and in the Knowledge guides — only the
  initialize-time listing is behind. Direct CLAUDE.md Documentation-Audit violation (server
  instructions row). Pure omission — no phantom actions to remove. Quick, mechanical fix.

  **[2026-06-20] Promoted:** picked into the active build plan (artifacts/build-plan.md), stacked on
  MCP-4R2K on branch `fix/mcp-error-contract`; shipping as one PR with TST-6W7H, CQ-2X8B, CQ-5J9N.

  **[2026-06-20] Shipped:** merged to main via PR #18 (squash f9e0663).

- **[TST-6W7H]** Link RequestValidator + UnifiedToolHelpers into the test project
  `effort: S · impact: M · area: tooling · source: reflection · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: #18 · related: TST-9Q4M`

  Both `Core` classes are GH/Rhino-free and just aren't linked into `src/Cordyceps.Tests` yet (the
  test csproj links source files individually to avoid pulling in the GH runtime). They are the
  input-validation and action-dispatch contract every tool relies on, so they're high-value, low-cost
  to cover.

  **Fix:** add `RequestValidator` and `UnifiedToolHelpers` to the `<Compile Include>` list in the test
  csproj, then write unit tests for `RequestValidator` (GUID / range / one-of / file-ext / etc.) and
  for `UnifiedToolHelpers.ValidateAction` / `GetParam<T>` / `GenerateHelp`. Builds on the test-evidence
  wiring shipped in TST-9Q4M.

  **[2026-06-20] Promoted:** picked into the active build plan (artifacts/build-plan.md), stacked on
  MCP-4R2K on branch `fix/mcp-error-contract`; shipping as one PR with DOC-8M3T, CQ-2X8B, CQ-5J9N.

  **[2026-06-20] Shipped:** merged to main via PR #18 (squash f9e0663).

- **[CQ-2X8B]** Consolidate tool-class duplication
  `effort: M · impact: L · area: code-quality · source: reflection · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: #18 · related: CQ-5J9N`

  Several pieces of duplicated / dead code across the tool layer:
  - Proxy-instantiation + param enumeration duplicated between `GhInspectTool.cs:451-457` and
    `ComponentRegistry.cs:368-389`, despite the existing `ToolHelpers.BuildParameterList`
    (`ToolHelpers.cs:657`) that already does this.
  - Repeated dispatch preamble across all 7 tool classes.
  - Dead / unreachable "Unknown action" default switch arms (`ValidateAction` already rejects unknown
    actions before dispatch reaches the switch).
  - Unused `GrasshopperContext.ExecuteOnUiThreadAsync` (`GrasshopperContext.cs:101`).

  **Fix:** route proxy/param enumeration through `ToolHelpers.BuildParameterList`, factor the shared
  dispatch preamble, drop the dead default arms, and remove the unused async helper. Low user-facing
  impact but reduces drift surface. Pairs well with CQ-5J9N (both touch the tool dispatch layer).

  **[2026-06-20] Promoted:** picked into the active build plan (artifacts/build-plan.md), stacked on
  MCP-4R2K on branch `fix/mcp-error-contract`; shipping as one PR with DOC-8M3T, TST-6W7H, CQ-5J9N.

  **[2026-06-20] Shipped:** merged to main via PR #18 (squash f9e0663).

- **[CQ-5J9N]** Broad-catch / silent-swallow sweep
  `effort: M · impact: M · area: code-quality · source: reflection · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: #18`

  ~40 `catch(Exception)` / empty catch blocks exist across the codebase with ZERO `prawduct:allow`
  waivers. Most surface `ex.Message` (acceptable, but unwaivered); some swallow silently with no
  logging:
  - `GhScriptTool.cs:285-290` (SyncScriptParams / SyncParameters / VariableParameterMaintenance)
  - `GhScriptTool.cs:688-700` (TryGetScriptSource)
  - `GhInspectTool.cs:460` (proxy.CreateInstance)
  - `McpServer.cs:358`

  **Fix:** add `prawduct:allow` waivers where the broad catch is genuinely needed (with rationale),
  add `Core.DebugLog` logging to the silent swallows, and narrow catch types where possible.

  **[2026-06-20] Promoted:** picked into the active build plan (artifacts/build-plan.md), stacked on
  MCP-4R2K on branch `fix/mcp-error-contract`; shipping as one PR with DOC-8M3T, TST-6W7H, CQ-2X8B.

  **[2026-06-20] Shipped:** merged to main via PR #18 (squash f9e0663).

- **[GHD-3K6F]** Finish or formally cut undo/redo
  `effort: M · impact: L · area: gh-document · source: reflection · added: 2026-06-20 · status: dropped · stage: requirements · reviewed: 2026-06-20`

  `GhDocumentTool.cs:375` / `:384` (`ActionUndo` / `ActionRedo`) ship as disabled stubs returning an
  error that points users to snapshots instead. Root cause: the GH undo system disposes the HTTP
  response before it can be sent (a threading issue — undo runs and tears down the in-flight request).

  **Decision needed before building** (hence `stage: requirements`): either
  (a) implement correctly — capture the HTTP response off the UI thread so the undo operation can't
      dispose it mid-flight; or
  (b) formally descope — remove the stub actions and document snapshots as the supported mechanism.

  Once the keep/cut call is made, advance to `ready` (option b) or `design` (option a). Doc-audit:
  either path touches `gh_document` ActionInfo and server instructions.

  **[2026-06-20] Dropped (cut):** user decision — undo/redo is formally cut. Snapshots remain the
  supported mechanism. The disabled stub actions in `GhDocumentTool.cs` still exist, returning an
  error that points to snapshots; only this backlog tracking item is dropped — the stub code was
  deliberately NOT removed.

- **[TST-9Q4M]** Wire .NET/xUnit test evidence into the Prawduct gate
  `effort: M · impact: M · area: tooling · source: critic · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: 63cdd58 · related: GHS-7K2P`

  `prawduct-hook test-evidence record` is pytest-only (runs `sys.executable -m pytest`), so it
  cannot record evidence for this C#/.NET repo — every chunk gets a non-blocking
  "no .test-evidence.json" Critic WARNING and the gate is unsound for this project.

  The `project-state.yaml` `test_command:` option requires a `{junit_xml}` literal, but
  `dotnet test` has no built-in junit logger; wiring this needs the `JunitXml.TestLogger` NuGet
  package on `Cordyceps.Tests` plus a `test_command: dotnet test ... --logger
  "junit;LogFilePath={junit_xml}"` (point at a script since `#`/operators in the command get
  truncated).

  Surfaced by the Critic while fixing GHS-7K2P.

  **[2026-06-20] Promoted:** built on branch `fix/dotnet-test-evidence` (commit 63cdd58);
  Critic clean (0 blocking); gate verified end-to-end (test-evidence record → 53/0, test-status
  current). Pending merge to main.

  **[2026-06-20] Shipped:** closed-by 63cdd58 ("Wire .NET/xUnit test evidence into the Prawduct
  gate").

- **[GHS-7K2P]** `gh_script(set)` silently drops the script component's language directive → "Can not determine input code language"
  `effort: M · impact: L · area: gh-script · source: user · added: 2026-06-20 · status: shipped · stage: ready · reviewed: 2026-06-20 · closed-by: #16 · refs: PR #16 (commit facde40)`

  `ActionSet`/`ActionConfigure` in `src/Cordyceps/Tools/Unified/GhScriptTool.cs` call
  `scriptComp.SetSource(code)` (`:164`, and `:260`/`:283` in configure), which overwrites the
  leading `#!` language directive that Rhino 8's unified `ScriptComponent` uses to infer the
  script language. If the caller's `code` lacks the directive, the component loses its language
  association and emits no geometry, failing at solve time with a runtime error on `out`:
  "Can not determine input code language". Manifests only at solve time — `set` returns
  `success: true`.

  Hits anyone following cordyceps' own docs: `Knowledge/Prompts/SetupScriptComponent.md` and
  `Knowledge/ComponentPatternsGuide.md` both show directive-less Python bodies. Full root cause
  and live-verified repro: see PR #16 / commit facde40 (the original
  `incoming-bugs/script-component-language-lost-on-setsource.md` report was removed in the
  2026-06-20 janitor pass and is preserved in git history).

  **Preferred fix:** before `SetSource`, auto-preserve/prepend the directive matching the
  component's current language (Python 3 / Python 2 / C#) when `code` doesn't already start
  with a recognized `#!` directive — backward compatible. Also fix the docs/templates to show
  `#! python 3` / `#! csharp` as line 1, and mention the directive requirement in `gh_script`
  help text. Touches the Documentation Contract boundary (server instructions, ActionInfo help,
  Knowledge guides — see CLAUDE.md Documentation Audit table).

  **[2026-06-20] Promoted:** fix built on branch `fix/gh-script-language-directive`
  (build-plan.md Chunk 01); Critic passed (0 blocking). Pending merge to main.

  **[2026-06-20] Shipped:** merged to main as squash commit facde40 (PR #16). gh_script(set/configure)
  now preserves the script language directive via Core/ScriptDirective.cs; 28 unit tests; docs audited.
