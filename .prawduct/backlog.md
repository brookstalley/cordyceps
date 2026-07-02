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

- **[DOC-4Q7N]** Author api-contract.md for the MCP tool surface
  `effort: M · impact: M · area: documentation · source: janitor · added: 2026-07-02 · status: open · stage: requirements`

  Surfaced by the 2026-07-02 janitor audit. The product's defining trait is an external programmatic
  contract (7 tools, ~106 actions) consumed by agents and scripts, but `.prawduct/artifacts` has no
  api-contract artifact (the plugin ships a template at `templates/api-contract.md`).

  **Requirements work:** decide + record a versioning/deprecation policy for tool actions
  (project-state 'accommodate' already names a deprecation registry) and publish the contract
  surface with stability tiers.

- **[TST-2R5H]** Scripted MCP smoke-test harness for live-Rhino verification
  `effort: M · impact: M · area: tooling · source: janitor · added: 2026-07-02 · status: open · stage: requirements · related: TST-8B3D`

  Surfaced by the 2026-07-02 janitor audit. The Grasshopper host can't run headless, so all host
  glue is verified manually via the operator-verification queue (7 pending entries). A small
  scripted MCP client (Python/TS) that runs a canned smoke sequence against a live Cordyceps server
  (add/wire/set/inspect/layer/material/place_image round-trips with assertions) would make VRF
  burn-downs cheap and repeatable.

  **Requirements to decide:** where it lives, and how results are recorded as evidence.

- **[TST-8B3D]** Burn down the operator-verification queue (VRF-001..VRF-007)
  `effort: S · impact: L · area: tooling · source: janitor · added: 2026-07-02 · status: open · stage: ready · related: TST-2R5H`

  Surfaced by the 2026-07-02 janitor audit. All 7 entries are pending; VRF-001..005 cover changes
  ALREADY SHIPPED to users in v1.4.10–12 (wedge-hang recovery, lifecycle teardown, slider parity,
  configure wire preservation) and VRF-007 covers the janitor HIGH fixes. One session in Rhino 8
  following the written steps; VRF-006 folds into the next release.

- **[TST-5N9X]** Dependency/toolchain refresh: test stack + csproj comments + SDK pin policy
  `effort: S · impact: S · area: tooling · source: janitor · added: 2026-07-02 · status: open · stage: ready`

  Surfaced by the 2026-07-02 janitor audit. Test packages are ~2 years old (Microsoft.NET.Test.Sdk
  17.8.0, xunit 2.6.6, runner 2.5.6 — batch-bump within the v2 line); two stale csproj comments
  (a net7 reference, and a 'Rhino 8.21+' claim vs the Grasshopper 8.0.23304 pin); and
  decide + document whether the oldest-8.x SDK pin is a deliberate min-version strategy.

- **[CQ-9W2F]** Repo structure cosmetics: Tools/Unified flatten, Knowledge/Prompts naming, tracked .gha strategy
  `effort: M · impact: S · area: code-quality · source: janitor · added: 2026-07-02 · status: open · stage: requirements · reviewed: 2026-07-02`

  Surfaced by the 2026-07-02 janitor audit. Three cosmetic/structural nits to batch when convenient
  (low priority):
  - `Tools/Unified` is the only `Tools/` subdir (vestigial from the v1.3 unification) — consider
    flattening.
  - `Knowledge/Prompts` (markdown templates) vs `Prompts/` (registry code) is confusable naming.
  - `releases/Cordyceps.gha` is a tracked binary (56 blobs, 27.4 MiB across history, pack still
    compact) — consider LFS or GitHub-Release-asset-only at a future major cleanup.

  **[2026-07-02] Scope extended (cumulative Critic, design reviewer)** — three code-quality dedup
  follow-ups, same batch-when-convenient priority:
  - (a) Migrate call sites from the `ToolHelpers` forwarders to `Core/ParseHelpers` +
    `Core/ResponseHelpers` and delete the forwarders (or mark them internal/`[Obsolete]`) — dual
    public API for identical behavior.
  - (b) Extract the ~20-line bulk-result bookkeeping (notFound list + zero-effect check + error
    construction) copy-pasted across 9 `rhino_scene`/`rhino_render` actions into a shared helper
    (e.g. `BulkOutcome`), keeping per-action response field names.
  - (c) Have `RhinoSceneTool.PlaceImage` `FindOrCreateLayer` delegate its resolution phase to
    `Layers.ResolveLayerIndex` so the two ambiguity contracts (including the verbatim error string)
    can't drift.

- **[MCP-7D2N]** Fold `_disposed` into the ServerState model or guard Start() after Dispose()
  `effort: S · impact: S · area: mcp-server · source: critic · added: 2026-07-02 · status: open · stage: ready · related: MCP-9F3Q, MCP-3D8V`

  From the reliability cumulative Critic (2026-07-02, NOTE severity): `McpServer._disposed` remains
  an independent bool the `ServerState` enum never consults — a disposed instance ends in `Stopped`,
  where `CanStart` returns true, so `Start()` on a disposed server would proceed. Pre-existing and
  unreachable via `CordycepsComponent` (stopped instances are removed from `_servers` and restart
  paths construct fresh instances), so no user-reachable invalid state today.

  **Fix shape:** either add a `Disposed` terminal state to `Core/ServerState` (`CanStart` false) or
  guard `Start()` on `_disposed`; add a transition-table test either way.

## Promoted

<!-- Items currently being addressed in an active build plan. /backlog pick
     skips these by default (work is already in flight). -->

- **[MCP-9F3Q]** Introduce a ServerState enum as the single source of truth for server lifecycle
  `effort: M · impact: M · area: mcp-server · source: critic · added: 2026-06-21 · status: promoted · stage: ready · reviewed: 2026-07-02 · related: MCP-3D8V`

  Stage-1 cumulative Critic NOTE (non-blocking, forward-looking, on Chunk 01/02 code). Server
  lifecycle state in `McpServer` is currently reconstructed from 3 interdependent signals —
  `IsRunning` + `StartError` + `_context` — with no single source of truth. No invalid combination
  is currently reachable, but Stage 2+ will add conditions that increase the combinatorial surface.

  **Fix shape:** introduce a `ServerState` enum (e.g. Stopped/Starting/Running/Failed) as the single
  source of truth, and derive `IsRunning`/`StartError` from it, before that Stage 2+ complexity lands.
  Refactor, behavior-preserving. Doc-audit: internal lifecycle only; check whether any tool surfaces
  server status to clients.

  **[2026-07-02] Promoted** into the active build plan (today's triage).

- **[MCP-3D8V]** McpServer.Stop() drain runs on the UI thread it is waiting for
  `effort: M · impact: M · area: mcp-server · source: janitor · added: 2026-07-02 · status: promoted · stage: ready · reviewed: 2026-07-02 · related: MCP-9F3Q, MCP-5T7W`

  Surfaced by the 2026-07-02 janitor audit. `Stop()` is always invoked from the UI thread (the
  `SolveInstance` port-change path, `RemovedFromDocument`, and now `DocumentContextChanged`) while
  holding `CordycepsComponent._lock`. An in-flight tool handler is a worker blocked in
  `RhinoApp.InvokeAndWait` waiting for that same UI thread — so `_inFlight.DrainWithin(2s)` can
  **never** succeed for exactly the handlers it protects. Result: a guaranteed ~2s UI stall + detach
  whenever teardown overlaps a request, and the `McpServer.cs` comment claiming handlers finish
  against a still-valid context is wrong for that case.

  Correctness is protected by the captured-context guard (no NRE), so this is a **latency/topology
  fix**, not a correctness fix: run the drain off the UI thread (cancel + fire-and-forget teardown
  task) or skip the drain when `!RhinoApp.InvokeRequired`. Pairs naturally with MCP-9F3Q's
  ServerState enum; MCP-5T7W covers the adjacent DrainWithin timeout-vs-fault return-value question.

  **[2026-07-02] Promoted** into the active build plan (today's triage).

- **[MCP-5T7W]** Decide + test InFlightRequests.DrainWithin timeout-coincident-with-fault behavior
  `effort: S · impact: M · area: mcp-server · source: critic · added: 2026-06-21 · status: promoted · stage: ready · reviewed: 2026-07-02 · related: MCP-3D8V`

  Stage-1 cumulative Critic NOTE (non-blocking, forward-looking, on Chunk 01/02 code).
  `Core/InFlightRequests.DrainWithin` returns `true` on any `AggregateException`, which can mask a
  budget-timeout that coincided with a handler fault. The masking loses only a WARN log — correctness
  is still protected by the `_context == null` guard — but the timeout+fault combination is currently
  untested.

  **Fix shape:** make an explicit decision about the correct return value when a drain timeout
  coincides with a handler fault, then add a regression test covering that combination. Touches
  `Core/InFlightRequests.cs` + the test project.

  **[2026-07-02] Promoted** into the active build plan (today's triage).

- **[GHD-6M2J]** Bound or evict GhDocumentTool snapshot store (unbounded process-lifetime)
  `effort: S · impact: M · area: gh-document · source: critic · added: 2026-06-21 · status: promoted · stage: ready · reviewed: 2026-07-02`

  Stage-1 cumulative Critic NOTE (non-blocking, forward-looking, low priority). `GhDocumentTool._snapshots`
  is an unbounded process-lifetime store (pre-existing; Chunk 03 only changed the collection type for
  thread-safety). Snapshots accumulate for the life of the Rhino session with no eviction or cap.

  **Fix shape:** consider a bound (max snapshot count) or an eviction policy (e.g. LRU / oldest-first).
  Gated by explicit user action, so memory growth is operator-driven and low priority. Doc-audit: if a
  cap is introduced, check `gh_document` snapshot ActionInfo for the new limit semantics.

  **[2026-07-02] Impact raised S→M (cumulative Critic, sustainability).** The 2026-07-02 janitor
  branch materially increases pressure on this store: undo/redo are now advertised as DISABLED in
  ActionInfo/server-instructions/README with explicit "use action='snapshot' before changes and
  action='revert'" guidance, steering every agent mutation workflow into this unbounded
  process-lifetime ConcurrentDictionary of full document serializations. Auto-timestamped names mean
  repeated unnamed snapshots never overwrite. Fix shape unchanged (cap + oldest-first eviction — the
  LogBuffer pattern is now in-tree — and/or a snapshot_delete action).

  **[2026-07-02] Promoted** into the active build plan (today's triage).

- **[RSC-6K1W]** Wrap multi-step Rhino mutations in undo records
  `effort: M · impact: L · area: rhino-scene · source: janitor · added: 2026-07-02 · status: promoted · stage: ready · reviewed: 2026-07-02`

  Surfaced by the 2026-07-02 janitor audit. No code path calls
  `RhinoDoc.BeginUndoRecord`/`EndUndoRecord` (grep-verified), so each per-object mutation is its own
  undo step: a user pressing Ctrl-Z after an MCP `layer_delete` gets back one object of fifty.
  User-felt impact.

  **Fix shape:** wrap each mutating `rhino_scene`/`rhino_render` action body in
  `BeginUndoRecord("Cordyceps <action>")` / `EndUndoRecord` in a `finally`. Doc-audit: mention undo
  grouping in tool notes.

  **[2026-07-02] Promoted** into the active build plan (today's triage).

- **[GHC-8V3T]** Stop renaming/nicknaming components on the canvas — annotate via groups instead
  `effort: M · impact: M · area: gh-canvas · source: user · added: 2026-07-02 · status: promoted · stage: ready · reviewed: 2026-07-02`

  **User decision (2026-07-02, from user reports):** Cordyceps must NOT rename/nickname components —
  renamed components are hard to find on the canvas. Most Grasshopper users never rename components;
  the native convention is annotation via **groups with labels** (plus panels/scribbles), so Cordyceps
  should snap to that convention.

  **Scope resolution (user decision, 2026-07-02):** the main open contract question is decided — do
  **NOT** remove or deprecate the rename capability. The `gh_canvas` `rename` action and the
  `nickname` parameter stay fully functional (a user/agent who explicitly wants to rename still can).
  What changes is the **propensity** to rename as part of building: Cordyceps guidance must stop
  encouraging renaming/nicknaming during normal canvas construction. This supersedes the earlier
  deprecate-or-remove question in (a)/(b) below; the contract question is closed, so the item is now
  implementable (`stage: ready`).

  **Remaining scope (implementable):**
  - (a) audit all guidance surfaces — server instructions (`McpServer.cs` `GetServerInstructions()`),
    ActionInfo descriptions/tips/examples, Knowledge guides (CanvasLayoutGuide / BestPractices /
    GettingStarted), and prompt templates — and remove anything that encourages nicknaming components
    while building; recommend groups with labels (and panels/scribbles) for annotation instead;
  - (b) check whether any Cordyceps code path auto-applies nicknames unprompted (e.g. `add` or script
    `configure` defaulting a nickname) and stop doing so;
  - (c) add an explicit note in the rename/add ActionInfo that renaming makes components hard to find
    and groups are the preferred annotation mechanism;
  - (d) note groups already have create/rename/color actions that serve the annotation need — no new
    capability required for the replacement convention.

  **[2026-07-02] Promoted** into the active build plan (today's triage).

## Archive

<!-- Shipped and dropped items, kept for searchability. Never deleted. -->

- **[GHS-4D8M]** gh_script(set/configure) silently succeeds when it leaves a Script component unable to determine its language (Rhino LanguageSpec wipe — upstream)
  `effort: M · impact: M · area: gh-script · source: user · added: 2026-06-24 · status: shipped · stage: ready · reviewed: 2026-07-02 · closed-by: mcneel-upstream-filing · related: GHS-7K2P · refs: issue #15, docs/upstream-rhino-scriptcomponent-languagespec.md`

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
  cordyceps-fixable**. This item then tracked the upstream report through filing.

  **[2026-07-02] Closed — filed upstream, McNeel acknowledged.** The drafted report was filed with
  McNeel; McNeel acknowledged it and promised a fix in a future Rhino release (user-reported
  2026-07-02). All Cordyceps-side work is done (mitigation shipped in v1.4.11; upstream report filed
  and accepted) — the remaining wait is on McNeel's fix, not on us, so the item is archived. If the
  upstream fix lands and warrants verification or removal of the `languageWarning` mitigation, file a
  fresh item then. Related to GHS-7K2P (directive-preservation fix via `Core/ScriptDirective.cs`).

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
