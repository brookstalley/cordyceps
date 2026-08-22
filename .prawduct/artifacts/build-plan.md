# Build Plan — issues-2026-08-21 (liveness, data modifiers, script-write cascade)

Branch: `feature/liveness-and-modifiers` (integration, off `develop`)
Scope: GitHub issues #30, #29, #27, and the two accepted findings from #28.
Critic mode: chunk (per code chunk) + cumulative before PR.

## Context / decisions

- Prior plan (janitor-2026-07-02) is complete and archived as `build-plan-reliability.md`;
  its six reliability chunks shipped via PR #26. Replaced by this plan.
- Baseline: 406/406 tests green (517 ms) on `develop` @ `af2b2c6`.
- **#28 net48 multi-target: DECLINED** by the user. Rhino 7 support is out of scope; the
  reporter's fork has a natural end date at Rhino 9. The user replies on the issue directly.
  The two *findings* from that issue are accepted and built here (chunks 05, 06).

### User decisions recorded this cycle

1. **`recompute` during an active solve → reject** with a structured busy result
   (`{success:false, solving:true, solving_since}`). Not queued, not deferred-blocking: a
   hidden queue gives the caller no completion signal, and blocking reintroduces the exact
   silence #29 exists to remove.
2. **Liveness probe lives at `gh_inspect(action='status')`** — `gh_inspect` is already the
   "what is going on" tool. The GET `/health` endpoint is enriched in parallel regardless.
3. **UI-thread heartbeat: build it.** Solver state alone cannot distinguish a modal dialog
   from an idle healthy server — the worst possible confusion, and #30's unattended killer.
   Heartbeat stale + solving = "busy, wait"; heartbeat stale + no solve = "UI blocked,
   likely a modal dialog, needs a human".
4. **Multi-document: report identity now, file retargeting.** `SolutionStart`/`SolutionEnd`
   are per-document, so solver state is tracked per-document and the status names the
   document it describes (`DisplayName` + `DocumentID`). Changing *which* document tools
   act on is a separate contract change — filed to the backlog, not built here.
5. **Status envelope: always-on, compact**, on every tool response, carrying the document
   name. Injected at a single choke point (see chunk 03), never per-tool.

### Known hazard, deliberately NOT fixed here (filed to backlog)

Every tool resolves through `ToolHelpers.TryGetActiveDocument` →
`Instances.ActiveCanvas?.Document` (`Core/ToolHelpers.cs:173`). With multiple definitions
open, the human focusing a different canvas tab silently retargets the entire MCP surface:
the agent can believe it is editing `wall-study.gh` while editing another file. The
always-on status block (decision 5) makes this *visible* — it names the document on every
response — but does not make it *safe*. Fixing targeting is a contract change for all 7
tools and gets its own cycle.

## Confidence check

Requirements Confidence: **High**. Three of the four items are user-filed GitHub issues with
reproduction detail and explicit asks; the fourth is two findings from a source audit that
were independently re-verified against the code (see Evidence). All five open design
questions were put to the user and answered before planning.

1. **Problem:** agent-driven sessions cannot tell a busy solver from a dead bridge (#29),
   and MCP calls landing mid-solve can raise a modal that only a human can clear (#30);
   per-parameter data modifiers are unreachable and unreportable through the API (#27);
   the script write path is single-pathed where the read path is defensive (#28).
2. **Success:** an agent can ask "are you alive?" and get an answer within a bounded time
   *while the UI thread is wedged*; every response says which document it acted on and
   whether the host is healthy; no MCP call can expire the bridge inside a running
   solution; Flatten/Graft/Simplify/Reverse are both settable and readable; a script
   component lacking `SetSource` fails with an actionable message instead of opaquely.
3. **Out of scope:** net48 / Rhino 7 (declined); changing which document tools target
   (filed); Reparameterize and other type-specific param extras (#27 calls this phase 2);
   suppressing the GH breakpoint dialog itself (#30 ask 3 — only needed if ask 1 fails).

## Evidence (verified against the tree, not the issue text)

- **#30 mechanism, worse than reported.** `HandleToolCallAsync` calls `RecordCommand(name)`
  on *every* tool call (`McpServer.cs:685`) → `CordycepsComponent.RefreshComponent()` →
  `instance?.ExpireSolution(true)` (`CordycepsComponent.cs:301`), queued via
  `RhinoApp.InvokeOnUiThread`. A repo-wide grep for `SolutionState` / `GH_ProcessStep` /
  `SolutionDepth` / `IsSolving` returns **nothing** — there is no solution-in-progress check
  anywhere. So the modal is reachable from *any* MCP call landing mid-solve, not only from
  stacked recomputes. The correct idiom is already in the file:
  `document.ScheduleSolution(10, d => ExpireSolution(false))` at `CordycepsComponent.cs:173`.
- **#29 is viable off-thread.** The HTTP listener runs on `Task.Run` threads
  (`McpServer.cs:290`), not the UI thread, and `HandleHealthCheckAsync` (`:421`) already
  answers without marshaling. `GrasshopperContext.ExecuteOnUiThread` is what takes the
  120-second `DocumentLock`, so a status path that simply never calls it stays responsive
  while the UI thread is wedged.
- **#27 gap confirmed.** `DataMapping` appears nowhere in `src/`. `GH_DataMapping` =
  `{None, Flatten, Graft}`; `IGH_Param.DataMapping/Simplify/Reverse` are get/set; and
  `IGH_Param.RemoveEffects()` exists for a clear operation. These live on `IGH_Param`, so
  they apply to component ports *and* free-floating params — both branches need handling.
- **#28 finding A confirmed.** `TryGetScriptSource` (`GhScriptTool.cs:699`) probes a
  five-step cascade; the write path calls bare `scriptComp.SetSource(finalSource)` via
  `dynamic` at `:174`, `:294`, `:323`.
- **#28 finding B is NOT mechanical.** See chunk 06 — five behavior traps identified.

## Testability constraint (drives all chunking)

`Cordyceps.Tests.csproj` has **no ProjectReference**. It links ~22 host-free `Core/*.cs`
files directly, because Grasshopper types cannot load in a unit test. Nothing under
`Tools/Unified/` and not `ToolHelpers.cs` is testable. **Therefore every chunk below
extracts its decision logic into a host-free `Core/` class, adds one `<Compile Include>`
line to the test csproj, and unit-tests it** — the established pattern
(`Core/SliderConfig.cs` → `SliderConfigTests.cs`). Host wiring stays thin and is verified
live in Rhino.

## Chunks

### Chunk 01 — Host-free status model  *(track A)*
**Delivers:** `Core/SolverState.cs` + `Core/StatusEnvelope.cs`, both host-free and linked
into the test project.
- Per-document solve state: begin/end by document id, `solving_since`, concurrent-safe.
- UI-thread heartbeat staleness: given last-stamp and now, classify fresh / stale.
- The three-layer derivation: `rhino` (alive, ui responsive, **modal inferred** = stale
  heartbeat with no solve running), `grasshopper` (idle/solving + which document),
  `cordyceps` (server listening, in-flight count, uptime).
- `StatusEnvelope.Inject(toolJson, status)` — parse a tool's JSON result string, add the
  compact `status` object, re-serialize. Must be total: a non-object or unparseable result
  is returned unchanged rather than throwing (a status block must never break a tool).
- **Acceptance:** unit tests for every state transition, the modal inference truth table,
  concurrent begin/end on two documents, and Inject over object / array / malformed /
  already-has-status inputs. No Grasshopper reference in either file.

### Chunk 02 — Host wiring: solution safety + heartbeat  *(track A)*
**Delivers:** the #30 fix and the state feed.
- `RefreshComponent()` no longer calls `ExpireSolution(true)` unguarded. Defer via
  `ScheduleSolution` / skip when a solution is in progress, so no MCP call can expire the
  bridge inside a running solution.
- `CordycepsComponent` subscribes per-document `SolutionStart`/`SolutionEnd` and feeds
  `SolverState`; unsubscribes on removal/close (no leak across document open/close cycles).
- A low-cost UI-thread heartbeat timer stamps `SolverState`.
- **Acceptance:** host-free policy covered by chunk 01 tests; wiring reviewed for
  subscribe/unsubscribe symmetry against the existing `RemovedFromDocument` /
  `DocumentContextChanged` lifecycle. Enqueue an operator-verification entry — the modal
  scenario needs a live Rhino to confirm.

### Chunk 03 — Surfaces: probe, envelope, busy rejection  *(track A)*
**Delivers:** everything an agent can observe.
- `gh_inspect(action='status')` — answered **without** `ExecuteOnUiThread`, reading cached
  state only. This is the whole point; a reviewer must be able to see it cannot block.
- Always-on status injection at the single choke point in `HandleToolCallAsync` /
  `McpResultFormatter`, not per-tool.
- `GET /health` enriched with the same three-layer state.
- `gh_document(action='recompute')` rejects during an active solve with the structured busy
  result (decision 1).
- Docs: `GetServerInstructions()`, `gh_inspect`/`gh_document` `ActionInfo`, and a Knowledge
  guide section on busy-vs-dead and what the status block means.
- **Acceptance:** a reviewer can trace the status path and confirm it never marshals or
  takes the document lock.

### Chunk 04 — #27 data modifiers  *(track B)*
**Delivers:** `Core/DataModifiers.cs` (host-free parse/plan: `none|flatten|graft`, tri-state
simplify/reverse, partial-update semantics) + tests; `GhCanvasTool.Modifiers.cs`
implementing `gh_canvas(action='modifier', ...)` with read mode when only `id`/`side`/`param`
are given; `modifiers` reported per param in `ToolHelpers.BuildParameterList` **and** in the
free-floating `IGH_Param` branch of `BuildFullComponentInfo`; full doc audit.
Param resolution by name **and** index, per the project rule (null-guard it — the
`GhWireTool.GetParameter` shape it mirrors NREs on a null spec).
**Acceptance:** unit tests cover the plan/parse matrix including partial updates and
invalid inputs; `info` round-trips modifier state.

### Chunk 05 — #28 finding A: script write cascade  *(track C)*
**Delivers:** the write path mirrors the read cascade — try `SetSource`, fall back to a
writable `Code` property, and **pre-check `HiddenCodeInput`** so a visible code-input param
yields an actionable message instead of an opaque `InvalidOperationException`. Applies at
all three call sites (`GhScriptTool.cs:174`, `:294`, `:323`).
**Acceptance:** the probe/fallback decision logic extracted host-free and unit-tested;
failure returns a specific, actionable error string.

### Chunk 06 — #28 finding B: System.Text.Json → Newtonsoft  *(track D, main agent)*
**Status: PENDING USER CONFIRMATION** — the justification for this finding was net48 load
conflicts, and net48 was declined. Its standalone value is dependency consolidation; its
cost is a refactor of the most protocol-critical code in the repo. Five behavior traps were
identified during recon and any of them is a silent wire-format regression:
1. STJ's `WhenWritingNull` does **not** apply to `Dictionary<string,object>` values, so a
   null `result` is emitted today as `"result":null`. Newtonsoft's `NullValueHandling.Ignore`
   **would** drop it — `Include` (the default) preserves current behavior.
2. Newtonsoft's default `DateParseHandling.DateTime` would mangle a string id that looks
   like a date; needs `DateParseHandling.None`.
3. `GetRawText()` maps to `JToken.ToString(Formatting.None)`, not bare `ToString()` (which
   indents objects/arrays).
4. `prompts/get` currently **throws** on a non-string argument value via `GetString()`;
   Newtonsoft's `(string)token` silently coerces — preserve or change deliberately.
5. Id echo is byte-lossless under STJ `Clone()`; `JToken` re-formats numbers, so `1.00` /
   `1e2` / >Int64-precision ids will not round-trip identically. Tests assert `"id":1.0`.
**DECISION 2026-08-21: DROPPED by the user.** The swap is not built. Rationale: its sole
justification was the net48 load conflict, and net48 was declined; on .NET 8 `System.Text.Json`
IS the BCL, so there is no extra assembly, no transitive version conflict, and no user-visible
benefit. The residual "consolidate on one library" argument points the *wrong* way on this
runtime — STJ is the platform-native, faster option — so the swap would trade five silent
wire-format regression risks for a move away from the native library.
`project-preferences.md` already scopes the split deliberately ("System.Text.Json used only in
type-conversion code and tests"), so this is a bounded exception, not drift.

**Carried forward instead:** characterization tests pinning today's STJ behavior for traps 1, 2
and 4, which no current test covers. Pure addition, no production change. These make any future
swap safe rather than hopeful — the reporter's "all 56 tests pass against the rewrite" is weaker
evidence than it sounds precisely because those 56 are structurally blind to three of the five
traps.

## Parallelization and integration

Tracks A, B, C are independent and run as **worktree-isolated subagents**, each on its own
branch off the integration branch. Track D is the main agent, last.

| Track | Chunks | Owns | Must not touch |
|-------|--------|------|----------------|
| A | 01-03 | `McpServer.cs`, `CordycepsComponent.cs`, `Core/SolverState.cs`, `Core/StatusEnvelope.cs`, `GhInspectTool`, `GhDocumentTool` | `GhCanvasTool*`, `GhScriptTool` |
| B | 04 | `GhCanvasTool*`, `Core/DataModifiers.cs`, `ToolHelpers.BuildParameterList` | `McpServer.cs`, `CordycepsComponent.cs` |
| C | 05 | `GhScriptTool.cs` + its host-free extraction | `McpServer.cs`, `GhCanvasTool*` |
| D | 06 | `McpServer.cs`, `Core/JsonRpcEnvelope.cs`, `Core/JsonTypeConverter.cs` | — (runs last, alone) |

**`CHANGELOG.md` is owned by the main agent alone** — it is a prepend-style file and three
concurrent writers guarantee conflicts. Tracks B and C also **do not edit `McpServer.cs`**;
they report the `GetServerInstructions()` line their action needs and the main agent applies
it. Track A owns that file because chunk 03 genuinely lives there.

**[DECISION] Subagents do not run the full suite.** Per explicit user instruction, each
subagent runs only the tests for its own chunk (`dotnet test --filter`). The main agent runs
the full 406+ suite at each integration merge and before Critic. This departs from the
default build-cycle guidance ("run the full suite before and after") and is recorded here so
the departure is visible rather than inferred.

## Status

- [ ] Chunk 01 — Host-free status model
- [ ] Chunk 02 — Host wiring: solution safety + heartbeat
- [ ] Chunk 03 — Surfaces: probe, envelope, busy rejection
- [ ] Chunk 04 — #27 data modifiers
- [ ] Chunk 05 — #28 finding A: script write cascade
- [x] Chunk 06 — #28 finding B: STJ → Newtonsoft — **DROPPED, not built** (see chunk entry)
- [ ] Chunk 06a — Characterization tests pinning current STJ wire behavior (traps 1, 2, 4)
- [ ] Integration: full suite green, cumulative Critic, doc audit

## Late decisions

- **The probe action is `gh_inspect(action='connection')`.** The user first chose `status`, but
  that action already existed (component-status enumeration, and it requires the UI thread), so
  one action could not be both without breaking a contract the server instructions tell agents to
  poll. `connection` is the user's chosen replacement name. The pre-existing `status` action was
  additionally hardened to return a prompt busy/blocked result instead of hanging — it went
  through unbounded `InvokeAndWait`, which makes it the literal source of the 32-minute silence
  issue #29 reported.
- **Solve tracking watches `GH_DocumentServer` globally**, not per-bridge-instance as chunk 02
  originally specified. Documents share one UI thread, so a solve in a definition containing no
  bridge component would have gone unrecorded — producing "UI blocked, nothing solving" and thus
  a false "modal, needs a human". The global watch also collapses four lifecycle hooks into one
  start/stop pair, which is what makes the unsubscribe symmetry auditable.
- **`modal_inferred` does not fire for issue #30's own dialog.** That dialog is raised *inside* a
  solve, so `SolutionEnd` never fires and the state reads as "busy solving". Chunk 02 prevents
  that dialog at the source; the inference catches every other modal. Recorded in VRF-012 so a
  verifier does not test for the wrong thing.
- **Pre-existing broad catches in `GhScriptTool` were left unwaived.** Repo-wide there are 60
  broad catches and only `McpServer.cs` boundaries carry `prawduct:allow` pragmas; the prior
  sweep (`CQ-5J9N`) targeted *silent* swallows by adding logging, which all 14 here already do.
  Waiving 14 in one file would be a norm change applied to 23% of the instances, not a fix.
  Flagged for the Critic rather than decided unilaterally mid-cycle.
