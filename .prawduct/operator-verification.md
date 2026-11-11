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

## VRF-014 — script-recompile-on-set Chunks 01–03 — the write actually recompiles (issue #33)

**Status:** pending
**Added:** 2026-08-27 (script-recompile-on-set, shipped for verification as `v1.5.0-rc.2`)
**Where to verify:** Rhino 8 + Grasshopper, `v1.5.0-rc.2` installed, an MCP client connected.

**Why this needs a human:** the whole fix is a conversation with a live Rhino script component.
`Core/ScriptProgram` is unit-tested against fakes that reproduce the real shapes (explicit
`IScriptObject` implementation, overloaded `ReBuild`, a `Code` whose public `Text` is a container
and whose string `Text` is on `ICode`), which proves the reflection resolves — but no fake can prove
that Rhino's own `Expire()`/`ReBuild()` make the *next solve* run the new program. The build agent
has no headless Rhino; this was read out of the decompiled 8.32 assemblies and statically reasoned,
not executed. The reporter's stale program was never reproduced here.

**Verify:**
- **The reported repro, on the canvas:** fresh C# Script component → `gh_script(action='set')` with
  unmistakable output → a Panel on the output shows the new values. Ground truth on the canvas, not
  through the outputs API.
- **The response tells the truth:** that same call returns `rebuilt: true`, and `verified: true`
  when the program read back matches. A `verified: false` should come with a `runningSource` that
  differs only in the `RunScript` signature (SDK-mode C#) — anything else is a real finding.
- **Python 3 Script components** behave the same as C# ones.
- **Cluster safety (the regression risk):** set script source on a component *inside* a cluster
  editor. The cluster's input hooks must survive and the editor must stay attached — the rebuild
  hooks were chosen because they never call `ExpireSolution(true)`/`NewSolution`, which is what
  corrupted clusters before v1.4.6.
- **Latency:** `set` now compiles inline, so the first script write of a session can take
  noticeably longer. Confirm it is seconds, not minutes — a compile over ~120s would start tripping
  the document-lock timeout for concurrent requests.
- **Version reporting (Chunk 03):** the MCP `initialize` response reports
  `1.5.0-rc.2+build<stamp>`, not `1.5.0.0`. This is what lets a tester prove which build they are on.

## VRF-015 — param-spec Chunk 1 — an out-of-range index refuses instead of wiring the wrong port

**Status:** pending
**Added:** 2026-09-07 (param-spec, BRO-3)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** `Core/ParamSpecLookup` is unit-tested in CI, but the glue that reads
`comp.Params.Input/Output` and indexes back into the live list needs real `IGH_Param` objects, which
the test project cannot load. The behaviour that used to be wrong — the *wire actually created* —
only exists in a running document.

**Verify:**

1. Place a component with exactly two inputs whose second input's name contains a digit — a variable-
   parameter component whose ports Grasshopper auto-numbers is the natural case, or add a second input
   with `gh_canvas(action='zoomable', id='...', side='input', operation='add')`.
2. `gh_wire(action='connect', sourceId=..., targetId=..., targetParam='2')`.
   Expect `success: false`, an error reading `Target: Parameter index 2 is out of range for the input
   side (0-1). Available: ...`, and **no new wire on the canvas**.
3. `gh_wire(action='connect', ..., targetParam='1')` connects to the second port (by position, not by
   the digit in its name).
4. `gh_wire(action='disconnect', ..., sourceParam='', targetParam='0')` now reports
   `Source: 'sourceParam' is required for a component — …` instead of silently disconnecting port 0.
5. `gh_canvas(action='modifier', id='...', side='input', param='2')` still reports the same
   out-of-range message it did before this change (unchanged behaviour, now from the shared rule).

## VRF-016 — strict-inputs Chunk 01 — every Cordyceps component is protected infrastructure

**Status:** pending
**Added:** 2026-11-09 (strict-inputs, #88)
**Where to verify:** Rhino 8 + Grasshopper with an MCP client connected.

**Why this needs a human:** `Core/InfrastructureIds` is unit-tested, but the adapter that gathers
each bridge's wired neighbours through `Attributes.GetTopLevel.DocObject` needs live objects.

**Verify:**

1. Place two Cordyceps components on one canvas (the second on a different port). Wire a slider or
   panel into each, and put a group around one of them.
2. `gh_canvas(action='list')` shows neither component, neither wired neighbour, nor the group.
3. `gh_canvas(action='delete', id=<second component's id>)` is refused as protected.
4. `gh_document(action='clear')` leaves both components, their neighbours and the group in place.

## VRF-017 — strict-inputs Chunk 02 — unknown tool arguments are an error

**Status:** pending
**Added:** 2026-11-09 (strict-inputs, #89)
**Where to verify:** any MCP client connected to a running Cordyceps.

**Why this needs a human:** `Core/ToolArgumentBinder` is unit-tested, but that a real client
receives the error as a tool result (`isError: true`) rather than a protocol failure, and still
sends its ordinary calls unchanged, only shows over a live connection.

**Verify:**

1. `gh_wire(action='list', sourceid='x')` returns `Unknown argument: 'sourceid' (did you mean
   'sourceId'?). Valid parameters: …` (singular: one unknown name) and nothing runs. With two
   unknown names, e.g. `sourceid='x', targetid='y'`, it returns `Unknown arguments: 'sourceid'
   (did you mean 'sourceId'?), 'targetid' (did you mean 'targetId'?). Valid parameters: …`.
2. An ordinary call from the client in use (Claude Code, Claude Desktop through `mcp-remote`)
   still works — the client is not adding arguments of its own that are now refused.
3. A call with no arguments object at all (`gh_inspect` with only `action`) still works.

## VRF-018 — strict-inputs Chunk 03 — component types and ports resolve strictly

**Status:** pending
**Added:** 2026-11-09 (strict-inputs, #90)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** `Core/Naming/TypeNameRule` and `Core/ParamSpecLookup` are unit-tested
against a test catalog. Which names are unique in a real install, whether the five pinned GUIDs
match the installed built-ins, and that nothing lands on the canvas after a refusal need a live host.

**Verify:**

1. `gh_canvas(action='add', type=…)` succeeds for each of: `Number Slider` (range 0..1, value 0.5),
   `slider` with `min=0, max=100, value=50`, `Panel`, `python`, `c#`, `csharp Script`, `filter`,
   `Merge` (created, not ambiguous), `Curve/Circle`, `Circle (Curve)`, `Curve/Primitive/Circle`,
   `Center Box`, `Curve/Interpolate`, `Transform/Rotate`.
2. `type='Circle'` returns `code: ambiguous_name` listing a Curve component and a Params parameter,
   each with a `use` spelling, and **nothing is added to the canvas**.
3. `type='Cull'` returns `code: unknown_type` with Cull Pattern among the suggestions; nothing added.
4. `type='Kangaroo2/Goals-Pt/Anchor'` without Kangaroo returns `code: unknown_category`. A random
   GUID returns `guid_not_installed`; a real component GUID creates it.
5. The pinned names create the pinned GUIDs: `Panel` (59e0b89a-…), `Number Slider` (57da07bd-…),
   `C# Script` (b6ba1144-…), and `Python 3 Script`, which creates the unified **Script** component
   (c9b2d725-…, named "Script"), not the dedicated Python 3 Script (719467e6-… in this install) —
   unchanged from before this branch; `Maths/Python 3 Script` creates the dedicated one. A pin whose
   GUID is not installed falls back to the ordinary rule silently, and a pin whose GUID is installed
   as a differently named type creates that type silently, so check the created object's type and
   GUID, not only that something was created. `Stream Filter`'s pinned GUID (fb97f83d-…) is not
   installed in this install, so it resolves by the ordinary rule to eeafc956-… (Sets/Tree).
6. `gh_wire(action='connect')` from a slider to a Circle: `targetParam='R'`, `'radius'` and `'1'`
   connect; `'Rad'` is refused with `Did you mean 'R' …` and **no wire is created**.
7. On Cull Pattern, `targetParam='Pattern'` is refused with the suggestion `'P'`.
   `gh_canvas(action='modifier')` with a partial `param` is refused the same way.
8. `gh_inspect(action='docs', component='Curve/Circle')` works; `component='Circle'` returns the
   ambiguity payload. `gh://component/Curve/Circle` and `gh://component/Number%20Slider` render.
9. The names the corrected guide tables rely on resolve with `add`: Revolution, Cylinder, Sphere,
   Solid Union, Mirror, Scale, Range, Length, Area, Distance, Repeat Data, Boolean Toggle, Center
   Box, Cull Pattern, Merge, Addition, `Curve/Circle`, `Curve/Interpolate`, `Transform/Rotate`.
   Plain `Rotate` is ambiguous (Vector/Vector and Transform/Euclidean).
10. Adding a type whose only match is deprecated returns `deprecated: true` and a `warning`.
11. Every shorthand alias creates the type listed (check the created object's type and category,
    not only success): `c#`, `csharp`, `csharp script` → C# Script; `python`, `python script`,
    `ghpython` → unified Script (see item 5); `plane`, `xyplane`, `xy` → XY Plane; `xzplane`, `xz`
    → XZ Plane; `yzplane`, `yz` → YZ Plane (all Vector/Plane); `cube` → Box (Params/Geometry);
    `rect` → Params/Rectangle; `circ` → Params/Circle; `ln` → Params/Line; `cyl` → Cylinder
    (Surface/Primitive); `pt` → Point, `crv` → Curve (Params/Geometry); `num` → Number, `int` →
    Integer, `bool` → Boolean, `str` and `string` → Text (Params/Primitive); `slider`,
    `numberslider` → Number Slider; `filter`, `streamfilter` → Stream Filter.
12. `Knowledge/Patterns/GridArray.md`: `type='Rectangular'` creates the Vector/Grid component, and
    from an XY Plane, sliders and the grid, `targetParam` values `Plane`, `Extent X`, `Extent Y`,
    `Size X`, `Size Y` (and nicknames `P`, `Ex`, `Ey`, `Sx`, `Sy`) connect; `type='Hexagonal'`
    resolves. Option B: `Series` (`Start`, `Step`, `Count`), `Cross Reference` (`A`, `B`) and
    `Construct Point` (`X`, `Y`, `Z`) resolve and those ports connect.

13. `gh_canvas(action='search')` and `add` agree on deprecation: pick a name with a deprecated twin
    (`Merge`, `Addition`, `Mirror`), search for it, and confirm every result `add` passes over is
    marked `deprecated: true` in the search results.

**Evidence gathered 2026-11-09 against the installed v1.5.0** (read-only `gh_canvas(action='search')`,
`gh_inspect(action='docs')` and `gh://component/{name}`; this proves the names exist and how many
current types carry each in this install, not that the new code resolves them — items 1-13 still
need running on a build of this branch). Search's `deprecated` flag in v1.5.0 omits the
`OBSOLETE`-class-name test that `add` uses, so these counts are upper bounds for "current".
- Exactly one current type: C# Script (b6ba1144-…, the pinned GUID), Python 3 Script
  (719467e6-…), Script (c9b2d725-…), Stream Filter (eeafc956-…), Panel, Number Slider (both the
  pinned GUIDs), XY/XZ/YZ Plane, Box, Cylinder, Point, Curve, Number, Integer, Boolean, Text,
  Revolution, Sphere, Solid Union, Mirror, Scale, Range, Length, Area, Distance, Repeat Data,
  Boolean Toggle, Center Box, Cull Pattern, Merge, Addition, Interpolate, Arc, Rectangular,
  Hexagonal, Series, Cross Reference, Construct Point, Plane Normal.
- Two current types, a component and a parameter: Rectangle, Circle, Line (hence the qualified
  `rect`/`circ`/`ln` targets). `Rotate`: two components (Vector/Vector, Transform/Euclidean).
- Not a type: `Rectangular Grid` (the component is `Rectangular`), `Hexagonal Grid`.
- Ports confirmed by name and nickname: Rectangular and Hexagonal (`Plane`, `Size X`/`Size`,
  `Extent X`, `Extent Y`), Circle (`Plane` P, `Radius` R), Cull Pattern (`Cull Pattern` P), Series,
  Cross Reference (`List (A)` A, `List (B)` B), Construct Point (`X coordinate` X …), XY Plane
  (`Origin`), Plane Normal (`Origin`, `Z-Axis`), Move (`Geometry`, `Motion`), Transform/Rotate
  (`Geometry`, `Angle`, `Plane`), Scale (`Geometry`, `Center`, `Factor`), Mirror (`Geometry`,
  `Plane`).

## VRF-019 — canvas-read Chunks 01, 02 and 06 — canvas objects resolve by GUID, prefix or nickname; removal actions refuse default nicknames

**Status:** pending
**Added:** 2026-11-09 (canvas-read)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** `Core/Canvas/ObjectResolver` and `Core/IdBatch` are unit-tested against
host-free snapshots, but `Core/Canvas/CanvasReader` (the only code that reads a `GH_Document` into
a snapshot) and the wiring of every id site through `ToolHelpers` need live objects. Which
nicknames count as defaults (and whether a new group's name really is `Group`), how nested groups
appear in `GH_Group.ObjectIDs`, and what a locked object reports are facts of the running host.

**Canvas for the checks:** a Circle renamed `C1`, two Point parameters left at their default
nickname `Pt`, a Number Slider renamed `R`, a group `POSTS` around `C1`, a Value List, and a C#
Script component. A fresh Circle is nicknamed `Circle` or `Cir` depending on Grasshopper's Draw Full Names
display setting; both count as default, so use whichever the canvas shows wherever these steps
say `Circle`. Steps 26-32 add a Circle left at its default nickname `Circle` (the only object
carrying it) and a group made with Ctrl+G; nothing before them depends on either.

**Verify:**

1. `gh_canvas(action='info', id='R')` returns the slider; `id='<first 8 hex of its GUID>'` returns
   the same object.
2. `gh_canvas(action='info', id='Pt')` fails with
   `'Pt' is the nickname of 2 objects: … Point, … Point; name one by its id` with both ids.
   `gh_canvas(action='delete', id='Pt')` deletes nothing; its error reads
   `'Pt' is the default nickname of 2 objects: … Point, … Point. A default nickname never selects
   what this action removes; pass one of these ids`.
3. `gh_canvas(action='delete', ids='["R","nope"]')`: R is deleted, the `nope` entry carries
   `No object named 'nope'.`, overall `success` is false. (Re-create R for the later steps.)
4. `delete ids='["R","<R full GUID>"]'` deletes once; the second entry has
   `note: "same object as entry 0"` and `success: true`. (Re-create R.)
5. `gh_canvas(action='move', moves='[{"id":"R","x":0,"y":0},{"id":"bad","x":1,"y":1}]')`: R moves,
   the `bad` entry fails with `No object named 'bad'.`
6. `move` with the Cordyceps component's GUID returns `Component not found: {guid}`; no response
   in these checks contains the word `Protected`.
7. `gh_canvas(action='preview', ids='["C1"]', enabled='false')` hides C1's preview;
   `gh_canvas(action='enable', ids='["C1"]', enabled='false')` locks C1 (re-enable it after).
8. `gh_wire(action='connect', connections='[{"sourceId":"R","targetId":"C1","targetParam":"R"},{"sourceId":"x","targetId":"C1"}]')`:
   the first wire is added, the second entry reads `Source: No object named 'x'. Did you mean 'R'?` (on this canvas the
   one-letter id 'x' is within two edits of the slider `R`; `NameSuggestions.Closest` allows two
   edits for short text), `success` is false. For the no-suggestion text, use a long unlike id
   such as `no_such_object_here`: the entry then reads `Source: No object named 'no_such_object_here'.`
9. `gh_wire(action='list', componentId='nope')` is an error (it used to return an empty list).
10. `gh_wire(action='disconnect', sourceId='R', sourceParam='0', targetId='C1', targetParam='R')`
    removes the wire, resolving both ends by nickname.
11. `gh_canvas(action='group_create', name='G', ids='["R","nope"]')` creates no group; the error
    starts `Nothing was changed: 1 of 2 ids did not resolve. ids[1] 'nope': …`.
12. `gh_canvas(action='group_add', id='POSTS', ids='["R"]')` adds R to POSTS;
    `group_add id='POSTS', ids='["<POSTS full GUID>"]'` is refused with
    `… is the group itself; a group cannot contain itself`.
13. `gh_canvas(action='group_remove', id='POSTS', ids='["<a Pt GUID>"]')` returns that GUID in
    `notMembers`.
14. `gh_canvas(action='group_move', id='posts', dx=10, dy=0)` fails with
    `No group named 'posts'. Did you mean 'POSTS'?`; `id='POSTS'` moves it.
15. `gh_canvas(action='list', group='POS')` fails; `group='POSTS'` lists its members; a
    component's GUID reads `Object is not a group: …`.
16. `gh_canvas(action='info', id='POSTS')` fails with `… 'POSTS' is a group; group actions take its
    name, and delete, move, rename, info, bounds and zoom take its id (…).`
17. `group_rename`, `group_color` and `group_delete` work with `id='POSTS'` (in that order, renaming
    back before deleting). This deletes POSTS, so it comes after every other step that needs it.
18. `gh_script(action='get', id='<script nickname>')` resolves the script by nickname.
19. `gh_canvas(action='info', id='MCP')` (the bridge component is named "Cordyceps" but its
    nickname is `MCP`, and the resolver matches nicknames only) returns `No object named 'MCP'.`,
    possibly followed by `Did you mean 'C1'?` (two edits away). A leak would be any response that
    returns an object, suggests `MCP`, or says the object is protected or exists.
20. `get`, `set`, `config`, `gh_inspect(action='outputs')` and `gh_inspect(action='geometry')`
    called with a nickname return the full GUID in `id`.
21. `gh_canvas(action='config', id='<the value list>', items='null')` is an error and the list keeps
    its items.
22. (Run before step 17, while POSTS still exists.) A group's GUID on `gh_wire(action='list')`, `gh_inspect(action='outputs')` and
    `gh_canvas(action='get')` is refused (`… is a group; this action works on components and
    parameters, not groups`), while `gh_canvas(action='move')` still acts on POSTS by its GUID.
    For `delete`, make a throwaway group around one Pt (`group_create name='TMP'`) and delete it by
    its GUID: the group goes. (Deleting POSTS here would leave step 17 nothing to act on.)
23. `gh_canvas(action='move', moves='[{"id":"R","x":0,"y":0},{"id":"<R short id>","x":50,"y":50}]')`:
    R moves to 0,0, the second entry fails with `same object as entry 0: give one position per
    object`, and `success` is false.
24. Give an object a date-shaped nickname (`2026-01-01T00:00:00`) and pass it in `moves`, in
    `connections` and in `outline group='["…"]'` (for a group so named): each resolves it.
25. `gh_inspect(action='disconnected')` on a component that has both a missing input and a warning
    lists the missing input, as it did before this branch.

Removal actions and default nicknames (Chunk 06). Add a Circle and leave its nickname `Circle`; it
must be the only object carrying that nickname (C1 is renamed). `<id>` below is the id the error
prints.

26. Ordinary actions still take a unique default nickname: `gh_canvas(action='info', id='Circle')`,
    `gh_canvas(action='move', moves='[{"id":"Circle","x":600,"y":0}]')` and
    `gh_wire(action='connect', sourceId='R', targetId='Circle', targetParam='R')` each act on the
    new Circle.
27. `gh_wire(action='disconnect', sourceId='R', sourceParam='0', targetId='Circle', targetParam='R')`
    removes nothing; the error reads `Target: 'Circle' is the default nickname of <id> Circle, which
    every new Circle gets, so it never selects what this action removes. Pass id '<id>', or name it
    first with gh_canvas(action='rename', id='<id>', nickname='NEW') and pass that name`. The same
    call with `targetId='<id>'` removes the wire.
28. Reconnect R to the Circle. `gh_wire(action='clear', id='Circle')` is refused with the same text
    (no `Target:` prefix) and the wire stays; `clear id='<id>'` (the 8+ hex prefix) clears it.
    Reconnect, and `clear id='<the Circle's full GUID>'` clears it too.
29. `gh_canvas(action='delete', id='Circl')` reads `No object named 'Circl'.` with no
    `Did you mean 'Circle'?`, while `info id='Circl'` does suggest `'Circle'`.
    `delete id='Circle'` deletes nothing and gives the refusal text above; `delete id='<id>'`
    deletes the Circle.
30. Add a Circle and `gh_canvas(action='rename', id='Circle', nickname='C2')` (rename is not a
    removal, so the default nickname resolves); `delete id='C2'` deletes it.
31. Add a Circle renamed `C3` and another left at `Circle`. `delete ids='["C3","Circle"]'`
    deletes C3; the `Circle` entry fails with the refusal text and overall `success` is false. (C3
    rather than C1, which VRF-020 step 11 still needs.)
32. Select the remaining Circle and the Value List and press Ctrl+G.
    `gh_canvas(action='group_list')` reports the new group's name: record it, and it should read
    `Group` (the resolver assumes exactly that). `gh_canvas(action='outline')` prints this group by
    its id, not by `Group`. `group_delete id='Group'` is refused with `'Group' is the default name of
    group <id>, which every new group gets, so it never selects what this action removes. Pass id
    '<id>', or name the group first with gh_canvas(action='group_rename', id='<id>', name='NEW') and
    pass that name`; `group_delete id='<id>'` deletes the group.

Reader facts only a live session confirms (check them while doing the outline checks in VRF-021):
- Default-nickname flags are right across installed types (judged against the proxy's
  `Desc.NickName` and the object's `Name`): a default-nicknamed object prints as its short id in
  outline, a renamed one as its nickname, and `delete` refuses the default nickname and accepts
  the renamed one.
- A group nested in a group appears as a group id inside `GH_Group.ObjectIDs`, and a group with two
  parents reads correctly.
- `Phase` and `Locked` of a locked object; which objects implement `IGH_PreviewObject.Hidden`.
- `SolutionState == GH_ProcessStep.Aborted` after an aborted solve; which of `doc.Enabled` and
  `GH_Document.EnableSolutions` each solver-off route sets (the canvas solver-lock button, and
  `gh_document(action='solver', enabled=false)`).
- `Attributes.GetTopLevel.DocObject` on a source gives the owning component, and
  `Params.Output.IndexOf(source)` the right output index (never -1 for a component-owned source).
- `GH_Group.Attributes.Bounds` gives a usable position before the canvas has painted.
- A slider's value, minimum and maximum come out as invariant text under a comma-decimal locale.
- A free-floating parameter rests at Computed after a solve.
- No parameter has `HasInputGrip` false while holding sources.
- `GH_BooleanToggle.Value` (not in the SDK XML) reads the toggle state.

## VRF-020 — canvas-read Chunk 03 — gh_inspect tree reads a data tree's shape

**Status:** pending
**Added:** 2026-11-09 (canvas-read)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** `Core/DataTrees/TreeSummary`, `TreeAlignment`, `TreeRequest` and
`TreeStatusText` are unit-tested on hand-built branches. Reading `IGH_Structure` through
`Core/DataTrees/DataTreeReader`, and whether an input's `VolatileData` holds the tree after its
modifiers, need a running solve.

**Verify:**

1. Feed a Series of 10 into a Number parameter and graft it:
   `gh_inspect(action='tree', id='<param>')` shows `paths: ["{0;0}..{0;9}: 1 each"]` and the note
   `one item per branch (grafted shape)`. Flatten instead: one branch, `{0}: 10`. A Partition List
   of size 3 on the 10 items shows `perBranch: {min: 1, max: 3}`.
2. Graft input B of a component and read `tree id='<component>', side='input'`: does B's tree show
   the grafted paths (is an input param's `VolatileData` the post-modifier tree)? Record which.
3. Turn the solver off via `gh_document(action='solver', enabled=false)`, then via the canvas
   solver-lock button, then both: `solver` reads `off: this document's solver is disabled`,
   `off: Grasshopper's solver is disabled`, or the combined text, matching the route used.
4. `limit=100` returns exactly 100 path groups on a tree with more (the rest counted in
   `morePathGroups`); `limit=0` gives the default 20; `limit=-1` is an error for `tree` and for
   `log`, and `gh_inspect(action='log', limit=-1, clear='true')` does not clear the log.
5. Null items are counted in `nulls` and in `items`; an invalid curve is counted in `invalid`.
6. `IGH_Structure.get_Branch(int)` and `get_Path(int)` (not in the SDK XML) behave as indexers:
   paths and counts match a Panel on the same output.
7. A locked owner gives `solved: false` with the locked note; a failed owner the failed note; an
   owner expired but not yet solved the "not computed" note.
8. With several warnings on the owner, `message` shows the first as Grasshopper lists it, with
   `(+K more)`.
9. `tree id='<a group's id>'` is an error ending `is a group; tree reads components and
   parameters`; a slider's tree reads `side: "parameter"`.
10. Two inputs of one component with 3 and 12 branches: `side='input'` without `param` gives
    `inputs don't line up: A has 3 branches, B has 12 branches` once, on the first multi-branch tree.
11. `tree ids='["R","C1"]'` (the VRF-019 canvas) returns two trees; `object` is each nickname and
    `id` the full GUID.

## VRF-021 — canvas-read Chunks 04 and 07 — gh_canvas outline, and status over the same objects

**Status:** pending
**Added:** 2026-11-09 (canvas-read)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** `Core/Canvas/OutlineView` is pinned by golden tests over host-free
snapshots. Whether real definitions read well, whether every printed name resolves against the
live document, and whether `status` reads parameter objects (sliders, panels, free parameters) as
outline does need real files and real runtime messages.

**Verify:**

1. `gh_canvas(action='outline')` on a real grouped definition, on a messy ungrouped one (it shows
   `networks` and `unconnected` instead of `groups`), and on a document with a cluster: each reads
   as a fair summary, and `summary` has its eight counts in order.
2. Compare the response size with `gh_canvas(action='list')` + `gh_wire(action='list')` +
   `gh_canvas(action='group_list')` on the same file, and record both numbers.
3. Every object, control and problem line starts `NAME · Type` (`COUNT · Number Slider = …`,
   `SUM · Addition [MATHS] · error: …`), and a child group's line in group detail reads
   `NAME · group · …`. Paste every printed name (the text before a line's first ` · `, `[tags]`, `→`
   destinations, the group detail `id`) into `gh_canvas(action='info', id=…)` or a `group_*`
   action, and every port token before `←` into `gh_wire(action='validate')` as `targetParam`: each
   resolves to the object or port it labels. On a scratch copy of the file, a printed name passed
   to `delete` or `group_delete` is never refused as a default name.
4. Turn the solver off both ways: counts read `N stale`, `solver` names the route, and `note` reads
   `counts are from the last solution and may be out of date`.
5. Lock a component: its counts read `disabled`, it is listed under `disabled`, and its `Phase` is
   not Computed.
6. Expire an object without solving (solver off, then change a slider), then, with the solver ON
   (while an expired object is still waiting, for example during a slow solve): does its `Phase`
   leave Computed? If it does, the summary's `not solved` count includes it and
   `gh_inspect(action='tree', id=…)` on it reads `solved: false`. (With the solver off outline
   prints `N stale` for every object, so that count cannot answer this.)
7. Hide an object's preview: `preview off` appears on its group-detail line and in `summary`.
8. Nest a group in a group: the parent's line says `contains: CHILD (n objects)` and
   `outline group='CHILD'` has a summary ending `inside PARENT`.
9. Draw a highlight group over objects already in module groups: both tags appear and each group
   line carries `shares K objects with …`.
10. Default-nickname detection across installed types (see VRF-019's reader facts).
11. Under a comma-decimal OS locale, slider values and ranges print with `.` (`0.5 (0..1)`).
12. An unwired panel shows its text (`NOTE = "read me"`); a wired panel shows none.
13. An input wired only to the Cordyceps bridge is neither listed as wired nor DISCONNECTED.
14. `gh_inspect(action='status')` lists parameter objects: give a parameter object a runtime error
    or warning (for example a free Number parameter fed a panel holding text it cannot convert) and
    it appears under `components` as `ERROR` or `WARNING`, matching outline's `problems`; a healthy
    slider and panel are listed as `OK`, never `DISCONNECTED`. Components read as they did before this
    branch, in the same `{id, name, type, status, message}` shape.
15. Hold the UI thread with a long solve and call `outline`: it returns the busy result at once
    instead of waiting.
16. `outline id='<a group's id>', depth=3` is an error; `outline depth=3` without `id` is an error;
    `outline id='<a group's id>'` shows that group's detail.
17. `gh_inspect(action='status', category='Params')` returns the sliders, panels and free
    parameters (and no components from other tabs).
18. On the same canvas, `status`'s `summary` error, warning and disconnected counts equal the
    error, warning and disconnected counts in outline's `summary`.
19. Phantom objects: delete a component and undo, or otherwise leave an object that
    `gh_canvas(action='list')` does not show; `status` does not list it either.
20. Rename a slider `Width (m)`: outline prints it by its short id (`<id> · Number Slider = …`),
    and `gh_canvas(action='info', id='Width (m)')` still resolves it. A slider renamed `A · B`
    prints as its id too.
21. Clear a slider's and a panel's nickname in Grasshopper (rename to nothing), then run
    `gh_inspect(action='status')`: each entry's `name` is its type name (`Number Slider`, `Panel`),
    never an empty string. Note what nickname a freshly placed slider and panel actually carry.

## VRF-022 — script-rebuild Chunks 01–03 — reshaping a script's ports rebuilds it; removing a port refuses a default nickname

**Status:** pending
**Added:** 2026-11-11 (script-rebuild)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** the decisions are unit-tested host-free (`Core/ScriptConfigurePlan`,
`Core/PortRemoval`, `Core/ScriptSignature`, and the `ScriptProgram` vocabulary), but the rebuild is
a conversation with a live script component (`Core/ScriptComponents.RebuildForChange`), and whether
a reshaped Python or C# script really runs a program built for its new ports after a `zoomable`
call is a fact of the running host. The refusal re-resolves the id against the live document and
reads which nicknames Grasshopper treats as default for script components and Merge.

**Canvas for the checks:** a fresh C# Script, a fresh Python 3 Script and a fresh Merge, each left
at its default nickname and each the only object of its type (note the nicknames the canvas shows;
use them below as `<cs>`, `<py>` and `<merge>`). Wire a Number Slider into every input of each.
Note each component's GUID; the GUID steps re-run a refused call by id.

**Verify:**

1. **configure, parameters only, rebuilds (C#):** `gh_script(action='set', id='<cs GUID>')` with a
   body that reads `x` and writes `a`, then `gh_script(action='configure', id='<cs GUID>',
   inputs='[{"name":"x","type":"double"},{"name":"y","type":"double"},{"name":"z","type":"double"}]')`
   with no `code`. The response carries `rebuilt: true` and a `verified` (or `verificationSkipped`
   with a reason); `codeSet` is absent; a Panel on `a` shows values from the new program on the
   next solve. A follow-up `gh_script(action='get')` shows the running program's signature carries
   `z`.
2. **configure that changes nothing:** `configure` on a component that cannot vary its parameters
   (any non-script, non-variable component, by GUID) with `inputs` returns `success: false`,
   `parametersApplied: false`, a `parametersError`, `rebuilt: false` and `rebuildSkipped` saying
   nothing changed.
3. **zoomable on a Python script rebuilds:** `gh_canvas(action='zoomable', id='<py>', operation='add')`
   adds an input and the reply carries `rebuilt` and `verified`/`verificationSkipped`. Write code
   that reads the new input (`gh_script set` by GUID) and confirm on the canvas that it runs with
   the new port; then, without writing code, `zoomable add` once more and confirm the next solve
   does not error about the program's ports.
4. **zoomable on a C# script rebuilds:** `zoomable add` with `id='<cs>'` replies with `rebuilt`;
   `gh_script(action='get')` shows the running signature includes the new input.
   `zoomable set_count` to the current count (by GUID) replies `rebuilt: false` with
   `rebuildSkipped` saying nothing changed.
5. **zoomable on Merge is unchanged:** `zoomable add` with `id='<merge>'` succeeds by the default
   nickname and the reply has the same fields as before this branch (`success`, `id`, `operation`,
   `side`, `index`, `parameterName`, `newCount`) and no `rebuilt`/`verified`/`rebuildSkipped`.
6. **Refusals — zoomable:** `zoomable operation='remove'` with `id='<merge>'` fails with an error
   starting `This call removes input '…', and every wire on it, so nothing was changed.` followed by
   the default-nickname teaching text and the id to pass; the Merge keeps every input and wire.
   The same call by GUID removes the port. `zoomable operation='set_count', count=1` by
   `<merge>` is refused the same way (naming every port past the first); by GUID it succeeds.
7. **Refusals — gh_script:** `configure` with `id='<cs>'` and `inputs` that drop `y` (or rename it)
   is refused with `This call removes input 'y', …` and the component, its ports and its wires are
   untouched (`gh_script info`). `gh_script(action='set', id='<cs>')` with code whose `RunScript`
   signature omits an input it has today is refused the same way; by GUID it applies and returns
   `lostConnections`. `configure` with `inputs='[]'` by `<cs>` is refused naming every input.
8. **Non-refusals:** by default nickname, each of these succeeds: `zoomable add` on `<merge>` and
   `<py>`; `zoomable set_count` upward on `<merge>`; `configure` on `<cs>` whose `inputs` keep every
   existing name and add one; `configure` on `<cs>` with only `code` whose signature keeps the
   ports; `set` on `<py>` (Python ports are not derived from code); `set` on `<cs>` with top-level
   statements and no `RunScript`. A `zoomable remove` with an out-of-range `index` by `<merge>`
   fails on the index (`Invalid index …`), not on the nickname.
9. **Port changes the refusal does not predict:** after step 7's `set` by GUID, and after a
   code-only `configure`, note whether Rhino itself added or removed a port while storing or
   building the source (the code-only `configure` route also calls the component's own
   `SyncParameters`). The refusal predicts only the ports Cordyceps' own sync removes; a port Rhino
   removes is outside it. Record what you see.
