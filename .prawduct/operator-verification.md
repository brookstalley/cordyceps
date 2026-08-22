# Operator Verification Queue

<!-- Append-only queue of pre-merge human-verification items for live-integration
     changes that automated tests can't fully cover. Each entry is a level-2 heading:
     `## VRF-<id> — <Chunk N> — <title>`; first body line is `**Status:** pending | verified | accepted`.
     operator_verification_required is false, so these do not block /pr create — they are
     an honesty/handoff record of what only a running Rhino can confirm. -->

## VRF-001 — Chunk 01 — Bounded UI-thread lock (timeout + re-entrancy guard)

**Status:** pending
**Added:** 2026-06-21 (Chunk 01, solidity-hardening Stage 1)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** The host-free timeout/release logic (`Core/DocumentLock`) is unit-tested
in CI, but the UI-thread half — `RhinoApp.InvokeRequired` re-entrancy guard and `InvokeAndWait`
marshaling under the lock — cannot run without a live Rhino. The build agent has no headless Rhino,
so this was reasoned + statically reviewed, not executed.

**Verify:**
- **Normal path unaffected:** ordinary tool calls (e.g. `gh_canvas` add/move, `rhino_scene` list)
  return identical results to before, with no added latency.
- **Wedge no longer hangs the server:** configure a Python script component with an infinite loop
  (e.g. `while True: pass`) and trigger it; while it's stuck, issue another MCP request. Expected:
  the second request returns within ~120s with `{"success": false, "error": "Document is busy…"}`
  (a structured error), **not** an indefinite hang. Before this change every later request hung forever.
- **Server stays responsive after:** subsequent requests keep returning the busy error (rather than
  the connection dying); restarting Rhino clears the wedged holder.
- (Optional) Confirm no deadlock if any UI-thread-originated path calls a tool (the inline branch).

## VRF-002 — Chunk 02 — Server lifecycle (bind-failure surfacing, drain, teardown race)

**Status:** pending
**Added:** 2026-06-21 (Chunk 02, solidity-hardening Stage 1)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** The host-free drain logic (`Core/InFlightRequests`) is unit-tested in CI,
but the `HttpListener` bind path, the `Stop()` drain under live UI-thread marshaling, and the
`_context` teardown race only exist with a running Rhino. The build agent has no headless Rhino, so
these were reasoned + statically reviewed, not executed.

**Verify:**
- **Bind failure shows an actionable reason:** occupy port 26929 with a *non-Cordyceps* process
  (e.g. `python3 -m http.server 26929`), then place the Cordyceps component on that port. Expected:
  a red **error bubble** on the component and a **Status** output reading *"Server: FAILED TO START"*
  with the actionable message (port in use → choose a different port). Before this change it showed a
  bare "NOT RUNNING". Then free the port / change the component's port and confirm it starts cleanly.
- **Clean shutdown drains in-flight work:** issue a slow MCP request (e.g. a tool call that takes a
  few seconds) and, while it's running, remove the component (or change its port). Expected: the
  in-flight request completes normally if it finishes within the shutdown budget; the operator log
  (`gh_inspect(action='log')` before removal, or Rhino command line) shows **no `NullReferenceException`**.
- **Teardown race does not NRE:** repeat the above with a request that outlives the shutdown budget.
  Expected: the request returns a structured *"MCP server is shutting down; the request was not
  processed"* result (or completes), **never** an unhandled `NullReferenceException` in the worker.
- **No listener/port leak:** add and remove the component several times on the same port. Expected:
  each add starts cleanly with no "port in use" error attributable to a leaked prior listener.

## VRF-003 — Chunk 03 — Concurrency hygiene (command counter + snapshot store)

**Status:** pending
**Added:** 2026-06-21 (Chunk 03, solidity-hardening Stage 1)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** The thread-safe counter (`Core/CommandStats`) and its lost-increment
contract are unit-tested in CI, and the snapshot store is now a `ConcurrentDictionary` (BCL-guaranteed
thread-safety). What only a live Rhino confirms is the *integration*: that `RecordCommand` is driven
from concurrent HTTP worker threads and that the off-thread `snapshots` list path coexists with an
on-UI-thread snapshot write without error. The build agent has no headless Rhino.

**Verify:**
- **Counter is exact under load:** issue many MCP requests in quick succession (ideally a few in
  parallel), then read the Cordyceps component's "Commands received" Status line (and/or the `/health`
  endpoint's `commandCount`). Expected: the count equals the number of requests actually made — no
  undercount — and "Last command" reflects a real recent call.
- **Snapshot list is stable under concurrency:** create a snapshot (`gh_document(action='snapshot')`)
  and, around the same time, list snapshots (`gh_document(action='snapshots')`). Expected: listing
  returns the current set with no exception and no corrupt/partial entry, regardless of timing.

## VRF-004 — Chunk 1 (GHC-7X4B) — Slider params applied on `add`

**Status:** pending
**Added:** 2026-06-24 (Chunk 1, slider-add-params)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** The decision logic (`Core/SliderConfig` — which settings to apply, parsed
with InvariantCulture) is unit-tested in CI, but the host glue — applying the plan to a live
`GH_NumberSlider` after `doc.AddObject` (set range, then value so it isn't clamped to the old range,
then decimals) — only runs with a real Grasshopper document. The build agent has no headless Rhino.

**Verify:**
- **Add applies all four:** `gh_canvas(action='add', type='slider', min=0, max=100, value=50, decimals=2)`.
  Expected: the new slider on the canvas reads **0–100**, value **50**, **2** decimals — in a single
  call, no follow-up `action='config'`. Before this fix it landed at the default 0–1 / 0.5.
- **Range-before-value ordering:** add a slider with `min=10, max=20, value=15`. Expected value is
  **15**, not clamped to a stale 0–1 range.
- **Non-slider unaffected:** `gh_canvas(action='add', type='panel', ...)` (or any non-slider) with the
  same params present ignores them and adds normally.
- **Parity with config:** a slider added with these params matches one added plain then `action='config'`-ed
  to the same values.

## VRF-005 — Chunk 2 (GHS-3W9N) — `configure` preserves wires; partial-update semantics

**Status:** pending
**Added:** 2026-06-24 (Chunk 2, configure-wire-preservation)
**Where to verify:** Rhino 8 + Grasshopper with a C#/Python script component wired on both sides, MCP client connected.

**Why this needs a human:** The LCS diff (`Core/ParamSyncPlan` — keep/remove/insert by name) is
unit-tested in CI, but the host glue — reshaping live `IGH_Param` registration from the plan, carrying
existing wires across name-matched params, collecting removed-param wires into `lostConnections`, and
applying type hints + access — only exists with a running Grasshopper document.

**Verify:** (script component with inputs **and** outputs both wired)
- **Name-matched wires survive:** `configure` a side keeping a param's name. Expected: that param's
  wire is still connected after; no silent drop. Before this fix `configure` wiped **every** wire.
- **Partial update — omit a side:** `configure` passing only `inputs` (omit `outputs`). Expected:
  outputs **and their wires** are left untouched. Before this fix configuring inputs wiped outputs.
- **Explicit clear — `[]`:** `configure` passing `outputs=[]`. Expected: outputs cleared and their
  wires returned in `lostConnections`.
- **Rename reports + reconnects:** rename an input. Expected: its old wire appears in `lostConnections`
  with a `reconnectHint`, and that hint is directly usable with `gh_wire(action='connect')` to restore it.
- **Response shape matches `set`:** `lostConnections` + `reconnectHint` look identical to what `set` returns.

## VRF-007 — janitor-2026-07-02 Chunk 03 — six HIGH bug fixes (host-bound halves)

**Status:** pending
**Added:** 2026-07-02 (janitor-2026-07-02, Chunk 03)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** five of the six fixes are document/host-bound (Grasshopper lifecycle
events, LayerTable/RenderMaterial/PictureFrame behavior) and cannot run off the UI thread in a unit
test. Only the `ScriptParamDefs.TryParse` decision (BUG 2) is host-free and CI-tested.

**Verify:**
- **Server survives document close/reopen (BUG 1):** place the component, close the `.gh` file,
  reopen the same file. Expected: no "port in use by another Cordyceps component" error — the
  server restarts cleanly on the same port. Also tab-switch between two open GH documents and back;
  the server should stop on unload and restart on return.
- **configure rejects malformed JSON before mutating (BUG 2, host glue):** wire a script component,
  then `gh_script(action='configure', inputs='[{"name":"x"')` (broken JSON). Expected:
  `success:false, "Invalid inputs JSON: …"` and ALL existing params/wires intact.
- **preview/enable per-id errors (BUG 3):** `gh_canvas(action='preview', id='<bogus-guid>',
  enabled=false)`. Expected: `success:false` with a per-id error entry, not `success:true`.
- **layer_delete of the current layer (BUG 4):** make a layer current, add objects, delete it with
  `deleteObjects=false`. Expected: objects moved to a non-descendant destination (echoed as
  `movedToLayer`), current layer reassigned, layer actually deleted. Also: deleting the only layer
  errors with no side effects.
- **material_create applies PBR (BUG 5):** `material_create name='Chrome' color='#CCCCCC'
  metallic=1 roughness=0.05`, then `material_list`. Expected: the listed material shows
  metallic≈1, roughness≈0.05 (previously silently dropped); render preview looks metallic.
- **place_image failed replace keeps the old image (BUG 6):** `place_image` once, then
  `place_image replace=true` with a corrupt/unreadable image path. Expected: error returned AND the
  original picture frame still present.

## VRF-008 — janitor-2026-07-02 Chunks 04/05 — MEDIUM-sweep host-bound behavior changes

**Status:** pending
**Added:** 2026-07-02 (janitor-2026-07-02, Chunks 04–05; enqueued at close-out per cumulative Critic)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** Chunks 04/05 changed dozens of host-bound behaviors that unit tests
cannot reach. The pure decision halves (bool grammar, coercion, culture parsing, id echo,
SliderConfig.ValueInvalid, place_image path rule) are CI-tested; the host glue below is not.

**Verify (spot-check at least the starred items):**
- ★ **select safety:** bare `rhino_scene(action='select')` errors telling you to pass
  `type='all'`; `type='all'` selects everything; a locked object lands in `notSelectable`.
- ★ **disconnect existence check:** `gh_wire(action='disconnect')` on a wire that doesn't exist
  returns `success:false` with `currentSources` listing the real sources; a real wire disconnects.
- ★ **cluster-safe clear:** inside a cluster editor, `gh_document(action='clear')` preserves the
  cluster input/output hooks (reports `preservedClusterHooks`).
- ★ **nested layers:** `set_layer` / `place_image` with `layer='A::B::C'` creates the hierarchy;
  an ambiguous short name errors listing candidate full paths.
- **group protection:** `group_rename`/`group_color`/`group_remove` refuse the Cordyceps
  infrastructure group; `group_add` with a typo'd groupId errors instead of creating a new group.
- **bulk expire:** delete/enable/connect over multiple targets recompute ALL touched components
  (check inside a cluster, where NewSolution(false) only processes expired objects).
- **render wait:** `rhino_render(action='render', wait=30)` on a Shaded viewport errors
  immediately (no 30s stall); on Raytraced it waits and reports passes.
- **sun mode:** after using azimuth/altitude, a lat/long/dateTime call flips back to computed
  mode (`mode:"computed"`) and visibly moves the sun.
- **light validation:** `light_add` with `spotAngle=120` errors; `light_set` with a bad color
  string errors before mutating; stale light ids appear in `notFound`.
- **bulk notFound:** `rhino_scene(action='delete', ids='[<stale-guid>]')` returns success:false
  with the id in `notFound` (same for hide/show/set_layer/set_name/set_color).
- **JSON-RPC robustness:** an MCP client sending a string or large-integer request id gets a
  correct response (previously the response was destroyed after the tool ran); `Accept: */*`
  works (curl default).

## VRF-009 — reliability Chunk 03 — Stop() drain moved off the UI thread

**Status:** pending
**Added:** 2026-07-02 (feat/reliability-follow-through, Chunk 03)
**Where to verify:** live Rhino 8 + Grasshopper with an MCP client attached.

**Why this needs a human:** the fix removes a guaranteed ~2s Rhino UI stall when server teardown
overlaps an in-flight request (the old synchronous drain waited on handlers blocked in
`RhinoApp.InvokeAndWait` on the very thread doing the waiting). UI responsiveness and the
restart-while-draining path are host behavior no unit test can observe. Supersedes the
drain/teardown bullets of VRF-002 (the lifecycle otherwise unchanged).

**Verify:**
- **Teardown under load:** issue a slow tool call (e.g. a large `gh_canvas` bake or a script
  configure), and while it is in flight delete the Cordyceps component (or close the document).
  Rhino must NOT freeze for ~2 seconds; the debug log should show "Stopping MCP server..."
  immediately and "MCP server stopped" shortly after (the drain now finishes in the background —
  a WARN "still in flight after 2s; detaching" is acceptable only for genuinely wedged work).
- **Restart while draining:** with a request in flight, change the component's port input
  (old server stops, new one starts). The new port must bind and serve immediately; the old
  server's teardown completes in the background without errors in the log.
- **Clean idle teardown:** delete the component with no requests in flight — server stops
  cleanly, port is freed (re-placing the component on the same port works).

## VRF-010 — reliability Chunk 05 — undo records around mutating Rhino actions

**Status:** pending
**Added:** 2026-07-02 (feat/reliability-follow-through, Chunk 05)
**Where to verify:** live Rhino 8 + Grasshopper with an MCP client attached.

**Why this needs a human:** `RhinoDoc.BeginUndoRecord`/`EndUndoRecord` grouping is host behavior
— no unit test can observe Rhino's undo stack. The API surface was verified by reflection
(api-notes-rhinocommon.md), but the actual grouping and record naming need eyes.

**Verify:**
- **Bulk undo as one step:** create ~10 objects, `rhino_scene(action='set_layer', ids=[all], layer='X')`,
  then a single Ctrl-Z in Rhino — ALL objects return to their original layers at once. Rhino's
  undo menu shows "Cordyceps set_layer".
- **layer_delete:** delete a layer with objects (moved to another layer) — one Ctrl-Z restores
  the layer and its objects together.
- **bake:** `gh_canvas(action='bake')` on a component producing many items — one Ctrl-Z removes
  every baked object.
- **Error path:** trigger a failing mutation (e.g. `set_layer` to a bogus layer) — no dangling
  open undo record afterward (subsequent manual edits undo normally, one at a time).
- **script action untouched:** `rhino_scene(action='script', ...)` undo behavior matches the
  native command's own record (not wrapped by Cordyceps).
- **Settings/environment undo actually records:** change render settings or the environment
  (`rhino_render(action='settings'/'env_set')`) and press Ctrl-Z — confirm Rhino's undo stack
  actually records these (the wrapper is in place, but whether RenderSettings/Sun/NamedView
  changes participate in document undo is unverified). If Rhino does NOT record them, soften
  the RenderingGuide "Undo Behavior" section and the rhino_render tool note accordingly.

## VRF-006 — gitflow-release-refactor — `release.sh prep`/`publish` end-to-end

**Status:** pending
**Added:** 2026-06-24 (gitflow-release-refactor, Chunk 02)
**Where to verify:** a real (or throwaway-version) release after this lands on `develop` + `main`, with main strict-protected.

**Why this needs a human:** `scripts/release.sh` is a bash orchestration script — not unit-testable in
the xUnit project. Its guards, arg parsing, and dispatch were statically checked (syntax + branch-guard
failure cases), but the mutating prep/publish flows (build, commit, push, `gh pr create`, tag push, GH
Release, yak push) are dry-run-guarded and only run for real at a release. shellcheck was unavailable on
the build host, so the logic was review-verified, not linted.

**Verify (at the next release, e.g. v1.4.13):**
- **Dry-runs first:** on `develop`, `./scripts/release.sh prep --dry-run`; on `main`,
  `./scripts/release.sh publish --dry-run`. Each should print its full step list and make **no** changes.
- **prep (on develop):** `./scripts/release.sh prep 1.4.13` bumps csproj+manifest, renames CHANGELOG
  `[Unreleased]`→`[1.4.13]`, builds the `.gha`, commits `Release v1.4.13`, pushes develop, and opens a
  develop→main PR. Confirm the PR exists with CHANGELOG notes and `build-test` runs on it.
- **Merge under protection:** the develop→main PR merges only after `build-test` passes (no direct push) —
  confirms strict protection + the release path coexist.
- **publish (on main, after merge + `git pull`):** `./scripts/release.sh publish 1.4.13` builds the yak
  package, **pushes only the `v1.4.13` tag** (not the branch — confirm the branch push is never attempted),
  publishes the GitHub Release with the `.gha`, and pushes to yak.
- **Alignment:** after publish, `develop` and `main` have identical trees (no back-merge needed); the next
  `prep` starts clean.

## VRF-011 — issues-2026-08-21 Chunk 04 — `gh_canvas(action='modifier')` data modifiers

**Status:** pending
**Added:** 2026-08-21 (issues-2026-08-21, Chunk 04 — GitHub issue #27)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** the parse/plan half (`Core/DataModifiers`) is unit-tested in CI, but the
half that matters to a user is host-bound: whether setting `IGH_Param.DataMapping`/`Simplify`/`Reverse`
actually changes downstream tree structure, and whether Grasshopper renders the modifier icon on the
port. Neither can run without a live Rhino.

**Verify:**
- **Graft changes the data, not just the flag:** place a `Move` component, feed its Geometry input a
  list of N points and its Motion input a list of M vectors. Baseline output count is
  `max(N, M)` (index-matched). Then `gh_canvas(action='modifier', id=<move>, side='input',
  param='Geometry', mapping='graft')`. Expected: output becomes N branches of M items (N×M total),
  and the graft icon renders on the Geometry port.
- **Read mode round-trips:** call `action='modifier'` with only `id`/`side`/`param` — the returned
  `mapping` string must feed straight back in as a `mapping=` argument and be accepted.
- **`info` reports it:** `gh_canvas(action='info', id=<move>)` shows `modifiers` on every param, and
  the grafted port reports `"mapping": "graft"`. This is the observability half of the issue — an
  agent could not previously detect a modifier at all.
- **Partial update leaves the rest alone:** set `simplify=true` on a param that already has
  `mapping='graft'`; confirm the graft survives (omitted fields must not reset).
- **Free-floating params:** drop a bare `Param_Point` on the canvas and set a modifier on it directly
  (no component involved) — it is its own target, a separate code path from component ports.
- **Cluster safety:** repeat the graft inside a cluster editor and confirm the cluster's input hooks
  survive (the action uses `ExpireSolution(false)`; `true` is what historically orphans clusters).

## VRF-012 — issues-2026-08-21 Chunks 02/03 — solution safety, heartbeat, connection probe

**Status:** pending
**Added:** 2026-08-21 (issues-2026-08-21, Chunks 02-03 — GitHub issues #30, #29)
**Where to verify:** Rhino 8 + Grasshopper, Cordyceps placed, an MCP client connected, and a
definition containing a deliberately slow script component (~60s per solve).

**Why this needs a human:** the state model and staleness classification are unit-tested in CI, but
every claim that matters is host-bound — whether the modal dialog still appears, whether the
heartbeat actually reflects a wedged UI thread, and whether the probe really answers while Rhino is
blocked. None of it can run without a live Rhino.

**Verify:**
- **The modal no longer appears (the core fix):** start a long solve, then issue several MCP calls
  during it (any tool — the old refresh fired on every call, not just `recompute`). Expected: no
  "The 'Cordyceps (MCP)' object expired during a solution" dialog, and the canvas keeps solving.
- **The probe answers while the UI thread is blocked:** during that same solve, call
  `gh_inspect(action='connection')`. Expected: a prompt reply with `solving: true` and a plausible
  `solving_since` — NOT a timeout. This is the whole point of the chunk; if it hangs, the path is
  marshaling somewhere it must not.
- **Busy recompute is refused, not queued:** during the solve, `gh_document(action='recompute')`
  must return `success:false` with `solving:true`, and must NOT trigger a second solve afterwards.
- **Heartbeat cadence and recovery:** with the canvas idle, `connection` should report the UI thread
  responsive. Then block the UI thread (an infinite-loop script) and confirm it flips to blocked
  within a few seconds — and, critically, that it returns to healthy after the block clears rather
  than latching (a dropped stamp must expire, not freeze the heartbeat forever).
- **Multi-document reporting:** open two definitions, put the bridge in one, start a long solve in
  the OTHER. Expected: the solve is still recorded (documents share one UI thread, so a solve in a
  bridge-less document must not read as "UI blocked, nothing solving"), and `solving_document` names
  the solving one distinctly from the document the call acted on.
- **Our own long operations must NOT read as a modal dialog:** with no solve running, issue a
  deliberately slow document-touching call (a large bake, `capture_views`, or a save of a heavy
  definition) that runs well past the staleness window. During it, call
  `gh_inspect(action='connection')` from a second client. Expected: `ui` reports blocked (honest —
  the thread really is busy) but `modal_inferred` is **false**, and the hint names a Cordyceps
  operation rather than telling the caller to fetch a human. Then confirm `recompute` is refused
  for the solving/blocked reason and NOT with a modal explanation. A `modal_inferred: true` here
  is the regression this bullet exists to catch: tool bodies run on the UI thread, so they starve
  the heartbeat exactly as a dialog does.
- **Known limitation to confirm, not a bug:** `modal_inferred` will NOT fire for the #30 dialog
  itself, because that dialog appears *inside* a solve, so the solve never ends and it classifies as
  "busy solving". Confirm the inference does fire for an ordinary modal raised outside a solve
  (e.g. a save dialog).
- **Status block on every response:** confirm each tool result carries the compact `status` object
  and that it names the document actually acted on.

## VRF-013 — issues-2026-08-21 Chunk 05 — script source write cascade

**Status:** pending
**Added:** 2026-08-21 (issues-2026-08-21, Chunk 05 — GitHub issue #28)
**Where to verify:** Rhino 8 + Grasshopper with a stock Script component and a GhPython component.

**Why this needs a human:** the cascade's decision logic is unit-tested against fakes, but which
branch a REAL Rhino 8 script component takes is only observable live.

**Verify:**
- **Rhino 8 still uses `SetSource`:** `gh_script(action='set')` on a stock Script component, with
  DebugLevel at Info. The `WriteScriptSource: source set on X via SetSourceMethod` log line must
  name the `SetSource` path. If it names the `Code` fallback instead, the `SetSource` overload
  selection is wrong for this host and silently degraded — this is the specific risk worth checking.
- **Language directive survives:** set a Python body without a `#!` line and confirm the component
  still solves (no "Can not determine input code language").
- **Visible code input is actionable:** wire a string into a script component's code parameter so
  the code input becomes visible, then `set`. Expected: a specific error naming the visible code
  input — not an opaque `InvalidOperationException`, and no partial write.
- **`IsScriptComponent` widening did not false-positive:** confirm ordinary non-script components
  are still rejected by `gh_script` (the gate now also accepts any component with a public writable
  `string Code` property).
