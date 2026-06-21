---
artifact: build-plan
version: 2
scope: null
depends_on:
  - artifact: project-preferences
last_validated: null
---

# Build Plan — Solidity Hardening

Derived from a firsthand architectural analysis of the codebase (2026-06-21), not from
upstream product artifacts. Addresses operational-safety, security, testability, and
consistency defects found across `src/Cordyceps/`. Four stages, each shipped as its own PR
with a cumulative Critic gate, merge, and `/clear` before the next stage begins.

## Requirements Confidence

**Level:** High

**Why:** Every issue was located and verified firsthand in the source; scope, success
criteria, and sequencing are each statable in one sentence. The two genuine product
decisions (security posture, stage order) were confirmed with the user, not inferred.

**Open assumptions / unknowns:**
- `[ASSUMPTION: RhinoCommon's InvokeAndWait supports a cancellation/timeout path usable for the mutex fix | HIGH impact | Chunk 01 verify-api step closes this before any handler is drafted; if it does not, the design falls back to a posted-action + ManualResetEvent(timeout) pattern]`
- `[ASSUMPTION: real local MCP clients (Claude Code/Desktop) do NOT reliably send an Origin header | HIGH impact | Chunk 04 verifies actual client behavior FIRST; if they omit Origin, "reject no-Origin" would break the user's own client, so the hardening reduces to path-traversal guard + documenting the residual no-Origin case rather than blocking it]`
- `[ASSUMPTION: the script-param NickName-vs-declared-name mismatch is a real wire-dropping defect | MED impact | Chunk 09 traces to confirm-or-refute before changing behavior]`

**What would raise confidence:** N/A (High). The HIGH-impact assumptions are gated by
verify steps inside their chunks, so they're resolved before code, not after.

## Status

<!-- Stage 1 — Operational safety (PR #1) -->
- [ ] Chunk 01: UI-thread marshaling timeout, cancellation, re-entrancy guard
- [ ] Chunk 02: Server lifecycle — surface bind failure, drain in-flight requests, fix teardown race
- [ ] Chunk 03: Concurrency hygiene — counters and snapshot reads
<!-- Stage 2 — Security hardening, no token (PR #2) -->
- [ ] Chunk 04: Close Origin bypass and add path-traversal guard
<!-- Stage 3 — Testability foundation (PR #3) -->
- [ ] Chunk 05: Extract and unify boolean parsing (host-free, tested)
- [ ] Chunk 06: Extract and unify id-list / JSON-array parsing
- [ ] Chunk 07: Canonical error-response builder and schema standardization
- [ ] Chunk 08: Extract ComponentRegistry alias table and pure ToolHelpers predicates
<!-- Stage 4 — Consistency, doc drift, lurking bugs (PR #4) -->
- [ ] Chunk 09: Trace and resolve flagged correctness bugs
- [ ] Chunk 10: Documentation and consistency cleanup

Context: Not started. Stages are independent and dependency-ordered; build top to bottom.
`/clear` after each stage's merge. Each chunk: build → `/prawduct:critic chunk` → commit.
Each stage's LAST chunk is `Type: cumulative-final` — its review IS the `/prawduct:critic
cumulative` PR gate (commit the chunk first, then run cumulative once).

## Scaffolding

No new project scaffold — the plugin (`src/Cordyceps/Cordyceps.csproj`) and the xUnit test
project (`src/Cordyceps.Tests/Cordyceps.Tests.csproj`) already exist, and CI
(`.github/workflows/dotnet-ci.yml`) already builds Release + runs `dotnet test` on Ubuntu
without Rhino.

### Build & Test Commands

- Build: `dotnet build src/Cordyceps/Cordyceps.csproj -c Release` (Release required; Debug blocked)
- Test: `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`
- After ANY build/test during non-release work, discard the restamped tracked binary:
  `git checkout -- releases/Cordyceps.gha` (and `.prawduct/.work-model-index.json` if regenerated).

### Testability constraint (read before Stages 2–3)

The test project does NOT reference the plugin assembly — it `<Compile Include>`-links
individual **host-free** `Core/*.cs` files to avoid pulling in the Rhino/Grasshopper
runtime. Any new file meant to be unit-tested must therefore be host-free: **no
`using Rhino`/`using Grasshopper`, and no `DebugLog` calls** (DebugLog is host-coupled).
Every extraction chunk adds a `<Compile Include>` link to
`src/Cordyceps.Tests/Cordyceps.Tests.csproj`. When de-silencing a catch in a linked file,
narrow the exception type rather than adding a DebugLog call.

### Verification Strategy

Two classes of chunk, two verification modes:
- **Host-coupled chunks** (Stage 1, the Origin half of Chunk 04, parts of 9–10): cannot be
  unit-tested (they call `RhinoApp`/`RhinoDoc`/`GH_Document`). Verify **live in Rhino** by
  exercising the MCP server as a client would — the per-chunk Acceptance criteria state the
  exact manual scenario. Where a pure sub-decision can be peeled off host-free, extract and
  unit-test it; otherwise state explicitly that verification is live.
- **Host-free chunks** (Stages 2–3 extractions, RequestValidator additions): unit-tested via
  xUnit table-driven `[Theory]`/`[InlineData]`, run in CI without Rhino.

## Project Structure

Existing layout is preserved. New host-free helpers land in `src/Cordyceps/Core/`; their
tests land in `src/Cordyceps.Tests/`; each new test source is linked in the test `.csproj`.

### Module Boundaries

- Host-free logic lives in `src/Cordyceps/Core/` files with no Rhino/Grasshopper imports.
- Host-coupled marshaling stays in `src/Cordyceps/Core/GrasshopperContext.cs` and the tool
  classes under `src/Cordyceps/Tools/Unified/`.
- Tool methods keep returning `{ success, ... }` JSON and catching at the MCP boundary
  (project-preferences error-handling contract).

## Build Chunks

---

### Stage 1 — Operational safety (PR #1)

### Chunk 01: UI-thread marshaling timeout, cancellation, re-entrancy guard

- **Description:** Make `GrasshopperContext.ExecuteOnUiThread` (both overloads) non-wedging.
  Today `_documentMutex.Wait()` and `RhinoApp.InvokeAndWait` have no timeout, so one hung
  UI-thread operation (an infinite-loop script component, a modal) blocks every later request
  forever with no recovery but a Rhino restart; there is also no re-entrancy guard, so a
  UI-thread caller would deadlock. Add a bounded mutex acquire, a bounded wait on the UI-thread
  invocation, and a re-entrancy guard (if already on the UI thread, run inline rather than
  re-marshaling). On timeout, abandon the wait and surface a structured timeout outcome to the
  tool boundary so the caller receives `{ success:false, error:"… timed out" }` instead of a
  permanent block. Preserve the existing capture-exception-and-rethrow + `finally`-release
  semantics exactly.
- **Depends on:** none
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md` (error contract, UI-thread rule), CLAUDE.md
- **Deliverables:** `src/Cordyceps/Core/GrasshopperContext.cs` (both overloads + timeout constant + re-entrancy guard); the timeout→structured-error mapping at the tool boundary in `src/Cordyceps/McpServer.cs`. If the re-entrancy/timeout *policy* (not the marshaling) can be peeled into a host-free predicate, add new `src/Cordyceps/Core/UiThreadPolicy.cs` and link it in `src/Cordyceps.Tests/Cordyceps.Tests.csproj`.
- **Foreign API:** RhinoCommon
- **Tests:** Host-coupled core is verified live. If a host-free policy helper is extracted, unit-test its decisions (already-on-UI-thread → inline; effective-timeout computation; timeout vs success classification).
- **Acceptance criteria:** (1) a tool call whose UI work exceeds the timeout returns a structured timeout error AND the next request succeeds (mutex released, server not wedged); (2) a normal call is unaffected and returns identical results to before; (3) calling `ExecuteOnUiThread` from the UI thread does not deadlock; (4) `dotnet build -c Release` clean; (5) live-in-Rhino scenario exercised and recorded.
- **Critic mode:** final
  <!-- Architectural keystone: every tool depends on this marshaling primitive; its
       coherence must hold before later chunks build on it. -->
- **Done when:**
  0. `verify-api` — read RhinoCommon docs/source to confirm what cancellation/timeout `RhinoApp.InvokeAndWait` actually supports and how to detect the UI thread; capture findings in the chunk's commit message or `.prawduct/artifacts/api-notes-rhinocommon.md`. Design the timeout mechanism from the real surface, not from assumption.
  1. Acceptance criteria met and live verification recorded
  2. `/prawduct:critic final` run and blocking findings resolved
  3. Committed and chunk marked `[x]` in Status; `git checkout -- releases/Cordyceps.gha`

### Chunk 02: Server lifecycle — surface bind failure, drain in-flight requests, fix teardown race

- **Description:** Three lifecycle defects in the HTTP server. (a) When the listener fails to
  bind (port owned by a non-Cordyceps process) the exception is swallowed and a silently-dead
  server is returned with no actionable reason — propagate an actionable status to the
  component. (b) The listen loop fire-and-forgets request handlers with no tracking, so `Stop()`
  detaches in-flight requests after a 2s budget — track in-flight request tasks and drain them
  (bounded) on stop. (c) `Stop()` nulls the shared `_context` while detached handlers may still
  read it — fix the teardown race so an in-flight handler cannot NRE.
- **Depends on:** Chunk 01 (a drained request may be blocked in `ExecuteOnUiThread`; the timeout
  from 01 bounds the drain)
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md`, CLAUDE.md
- **Deliverables:** `src/Cordyceps/McpServer.cs` (bind-failure propagation, in-flight task tracking + bounded drain, teardown ordering), `src/Cordyceps/CordycepsComponent.cs` (surface the bind-failure reason in component status), CHANGELOG.md
- **Tests:** Host-coupled; verify live. Occupy the port with another process and confirm the component shows an actionable error (not a silent "NOT RUNNING"); repeatedly add/remove the component and confirm no port/listener leak; stop the server during a slow in-flight request and confirm clean drain with no NRE.
- **Acceptance criteria:** port-in-use surfaces an actionable status string; `Stop()` drains in-flight requests within the shutdown budget; no NRE on the `_context` teardown race; live scenarios recorded.
- **Done when:**
  1. Acceptance criteria met and live verification recorded
  2. `/prawduct:critic chunk` run and blocking findings resolved
  3. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`

### Chunk 03: Concurrency hygiene — counters and snapshot reads

- **Description:** Close the remaining data races. `CommandCount++` / `LastCommand` in
  `src/Cordyceps/McpServer.cs` are mutated from concurrent HTTP worker threads with no
  synchronization (torn reads / lost increments) — make them safe (Interlocked / lock). The
  `_snapshots` dictionary in `src/Cordyceps/Tools/Unified/GhDocumentTool.cs` is written under
  `ExecuteOnUiThread` but read off-thread in the list-snapshots path — route the read through
  the same synchronization (or a concurrent collection).
- **Depends on:** Chunk 02
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md`
- **Deliverables:** `src/Cordyceps/McpServer.cs`, `src/Cordyceps/Tools/Unified/GhDocumentTool.cs`, CHANGELOG.md
- **Tests:** Host-coupled; verify by reasoning + live smoke (concurrent calls don't corrupt the counter; snapshot list/read under concurrency is stable).
- **Acceptance criteria:** counter updates are atomic; snapshot access is synchronized; behavior otherwise unchanged; build clean.
- **Type:** cumulative-final
- **Done when:**
  1. Acceptance criteria met and live verification recorded
  2. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`
  3. `/prawduct:critic cumulative` run against `merge-base...HEAD` and blocking findings resolved (this IS the PR gate)
  4. `/prawduct:pr create` → user reviews and merges PR #1 → `git checkout main && git pull`
  5. **`/clear`** before starting Stage 2

---

### Stage 2 — Security hardening, no token (PR #2)

### Chunk 04: Close Origin bypass and add path-traversal guard

- **Description:** Per the confirmed **harden-no-token** posture: (a) tighten the Origin check
  in `src/Cordyceps/McpServer.cs` so a request can't trivially bypass DNS-rebinding protection,
  AND (b) add path canonicalization + a traversal/allowed-root guard to the file-operation
  validators in `src/Cordyceps/Core/RequestValidator.cs` so unauthenticated local callers can't
  read/write arbitrary filesystem locations, AND (c) document the residual trust boundary (any
  local process can still POST; no token by design). **Compatibility gate:** the current code
  already rejects a *present-but-non-localhost* Origin; the gap is a *missing* Origin header.
  Many local MCP clients do not send Origin, so blanket-rejecting no-Origin requests could break
  the user's own client. Chunk verifies real client behavior FIRST and only then picks the
  policy — if local clients omit Origin, the hardening lands as path-traversal guard +
  documented residual risk rather than a no-Origin block that breaks the integration.
- **Depends on:** Stage 1 merged
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md`, CLAUDE.md
- **Deliverables:** `src/Cordyceps/Core/RequestValidator.cs` (path-traversal guard — host-free, already linked + tested), `src/Cordyceps/McpServer.cs` (Origin policy per verification), new `.prawduct/artifacts/security-model.md` (trust boundary + residual risk), CHANGELOG.md. If a pure origin-decision predicate is peelable, add it host-free and unit-test it.
- **Foreign API:** MCP client Origin behavior (Claude Code / Claude Desktop local transport)
- **Tests:** Add `[Theory]` path-traversal cases to `src/Cordyceps.Tests/RequestValidatorTests.cs` (`../` escapes, absolute-path escapes, symlink-style, valid in-root paths). Origin logic is host-coupled — verify live; unit-test the peeled predicate if extracted.
- **Acceptance criteria:** path-traversal inputs rejected by new RequestValidator tests; valid paths still pass; the chosen Origin policy does NOT break a real local MCP client (verified); health check still reachable; `security-model.md` + CHANGELOG updated; documentation audit (server instructions / help metadata) done if any user-facing behavior changed.
- **Type:** cumulative-final
- **Done when:**
  0. `verify-api` — confirm whether the real local MCP client sends an Origin header (inspect a live request or client docs); record in the chunk notes. Pick the Origin policy from that evidence.
  1. Acceptance criteria met and tests pass
  2. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`
  3. `/prawduct:critic cumulative` run and blocking findings resolved (PR gate)
  4. `/prawduct:pr create` → user reviews and merges PR #2 → `git checkout main && git pull`
  5. **`/clear`** before starting Stage 3

---

### Stage 3 — Testability foundation (PR #3)

### Chunk 05: Extract and unify boolean parsing (host-free, tested)

- **Description:** Thin vertical slice that proves the extract → link → test → unify pattern the
  rest of the stage repeats. Boolean parsing is currently forked ~4 ways (inline
  `ToLower()=="true"`, raw `bool.TryParse` which rejects `1`/`0`, an inline `enabled` parser, and
  `ToolHelpers.ParseBool`), so `enabled='1'` works in some actions and fails in others. Create
  one canonical host-free parser accepting `true`/`false`/`1`/`0` (case-insensitive), link it
  into the test project, unit-test it, and replace **every** string→bool parse site with it.
  Scope by pattern: all sites that parse a string into a bool.
- **Depends on:** Stage 2 merged
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md` (testing approach), the testability constraint above
- **Deliverables:** new `src/Cordyceps/Core/ParamParsing.cs` (host-free; no Rhino imports, no DebugLog), new `src/Cordyceps.Tests/ParamParsingTests.cs`, `<Compile Include>` link added to `src/Cordyceps.Tests/Cordyceps.Tests.csproj`, and the unified call sites in `src/Cordyceps/Core/ToolHelpers.cs` and the tool classes under `src/Cordyceps/Tools/Unified/`.
- **Tests:** unit tests for `true`/`false`/`1`/`0`/mixed-case/invalid/null/whitespace.
- **Acceptance criteria:** one bool parser; all forked sites unified onto it; `enabled='1'` behaves identically everywhere; `dotnet test -c Release` passes; CI green.
- **Critic mode:** final
  <!-- Establishes the extract/link/test/unify convention chunks 06–08 follow. -->
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic final` run and blocking findings resolved
  3. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`

### Chunk 06: Extract and unify id-list / JSON-array parsing

- **Description:** Consolidate the three forked id/JSON-array paths — typed
  `List<string>` with local try/catch, `List<dynamic>` with fragile `(double)` casts that throw
  on string input, and the `TryParseGuidArray`/`TryDeserializeList` helpers — into one host-free
  parser with consistent error reporting, and remove the duplicated `BuildIdList`
  reimplementations. Scope by pattern: every "single id or JSON array of ids/objects" parse.
- **Depends on:** Chunk 05
- **Artifacts consumed:** testability constraint above
- **Deliverables:** extend `src/Cordyceps/Core/ParamParsing.cs` (host-free), new `src/Cordyceps.Tests/IdListParsingTests.cs` linked in `src/Cordyceps.Tests/Cordyceps.Tests.csproj`, unified call sites in `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`, `src/Cordyceps/Tools/Unified/GhCanvasTool.Values.cs`, `src/Cordyceps/Tools/Unified/GhWireTool.cs`, `src/Cordyceps/Tools/Unified/RhinoSceneTool.cs`.
- **Tests:** single-id, JSON array of ids, JSON array of objects with numeric/string fields, malformed JSON, empty, mixed-type.
- **Acceptance criteria:** one id-list parser; all sites unified; malformed input yields a consistent structured error (no fragile `(double)` throw); tests pass.
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic chunk` run and blocking findings resolved
  3. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`

### Chunk 07: Canonical error-response builder and schema standardization

- **Description:** Tool errors come in two shapes today — the `ToolHelpers.ErrorResponse`
  `{success:false, error}` and many hand-rolled objects with extra fields
  (`suggestion`, `availableOutputs`, …) — so an LLM client can't rely on one error schema.
  Define one canonical host-free error-response builder: always `{success:false, error, …}`
  with structured extras nested under a stable key, and unify the hand-rolled errors onto it.
  Update user-facing docs (server instructions + affected `action='help'` metadata) to state the
  error contract — documentation audit per CLAUDE.md.
- **Depends on:** Chunk 06
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md` (error contract), CLAUDE.md (doc-audit table), testability constraint
- **Deliverables:** host-free error builder (extend `src/Cordyceps/Core/McpResultFormatter.cs` or a new linked file), tests in `src/Cordyceps.Tests/McpResultFormatterTests.cs`, unified call sites across `src/Cordyceps/Tools/Unified/`, server-instructions update in `src/Cordyceps/McpServer.cs`, CHANGELOG.md.
- **Tests:** error builder produces the canonical shape; structured extras preserved and discoverable under the stable key; a formatted error is still classified as an error.
- **Acceptance criteria:** one error schema across tools; structured fields preserved; server instructions document the contract; tests pass.
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic chunk` run and blocking findings resolved
  3. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`

### Chunk 08: Extract ComponentRegistry alias table and pure ToolHelpers predicates

- **Description:** Pull the pure, high-value logic out of the host-coupled classes so it can be
  tested: the `ComponentRegistry` alias map (`"python" → "Python 3 Script"`, etc.) and the pure
  predicates in `ToolHelpers` (protected/infrastructure-id checks, GUID validation). Leave the
  Rhino-coupled resolution in place, delegating to the extracted host-free units.
- **Depends on:** Chunk 07
- **Artifacts consumed:** testability constraint above
- **Deliverables:** new host-free file(s) under `src/Cordyceps/Core/` for the alias table + predicates, new test file(s) linked in `src/Cordyceps.Tests/Cordyceps.Tests.csproj`, delegations from `src/Cordyceps/Core/ComponentRegistry.cs` and `src/Cordyceps/Core/ToolHelpers.cs`.
- **Tests:** alias-resolution matrix (canonical names, aliases, unknowns); protected/infrastructure-id checks; GUID validation accept/reject.
- **Acceptance criteria:** alias + predicate logic is host-free and unit-tested; host classes delegate (no behavior change); tests pass; CI green.
- **Type:** cumulative-final
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`
  3. `/prawduct:critic cumulative` run and blocking findings resolved (PR gate)
  4. `/prawduct:pr create` → user reviews and merges PR #3 → `git checkout main && git pull`
  5. **`/clear`** before starting Stage 4

---

### Stage 4 — Consistency, doc drift, lurking bugs (PR #4)

### Chunk 09: Trace and resolve flagged correctness bugs

- **Description:** Two flagged-but-unconfirmed correctness issues. (a) Script-param sync
  (`SyncScriptParams`/`SyncParamSide` in `src/Cordyceps/Tools/Unified/GhScriptTool.cs`) appears
  to compare code-derived *declared names* against param *NickNames*; a user-renamed param could
  be seen as remove+insert and drop a wire. (b) `material_apply` in
  `src/Cordyceps/Tools/Unified/RhinoRenderTool.Materials.cs` validates a numeric index against
  the `Materials` table but assigns it to object attributes — possibly mismatched collections.
  Trace each to confirm-or-refute with evidence; fix if real, or descope-with-rationale if not.
  Where the comparison/index logic can be peeled host-free, add a regression test.
- **Depends on:** Stage 3 merged
- **Artifacts consumed:** `.prawduct/artifacts/project-preferences.md`, testability constraint
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhScriptTool.cs`, `src/Cordyceps/Tools/Unified/RhinoRenderTool.Materials.cs`; new test file(s) if logic is extractable; CHANGELOG.md if behavior changes.
- **Tests:** regression test for the param-matching identifier choice (extract host-free if feasible); index-path test for material_apply if extractable; otherwise live verification with the exact scenario recorded.
- **Acceptance criteria:** each issue confirmed or refuted with cited evidence; real defects fixed + covered; refuted ones documented; tests pass.
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic chunk` run and blocking findings resolved
  3. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`

### Chunk 10: Documentation and consistency cleanup

- **Description:** Resolve the accumulated drift and DRY debt. (a) Reconcile undo/redo: they're
  disabled (hard error) in `src/Cordyceps/Tools/Unified/GhDocumentTool.cs` but still advertised
  as working in `ToolInfo`/tool description/server instructions — with Chunk 01's timeout work,
  decide to re-enable or to remove them from the advertised surface; no advertise-but-fail.
  (b) Fix the `rhino_light` add error strings in
  `src/Cordyceps/Tools/Unified/RhinoRenderTool.Lights.cs` that name non-existent params
  (`lightLocation`/`lightType` → `location`/`type`). (c) Unify the ~35 inlined
  `RhinoDoc.ActiveDoc == null` checks onto the existing-but-unused
  `ToolHelpers.TryGetRhinoDoc` (`src/Cordyceps/Core/ToolHelpers.cs`). (d) Remove dead code
  (the orphaned `CreateTypedParameter` in `src/Cordyceps/Tools/Unified/GhScriptTool.cs`; the
  redundant double-key param registration in `src/Cordyceps/Tools/Unified/GhWireTool.cs`).
  (e) Rename misnamed `src/Cordyceps.Tests/McpServerTypeTests.cs` (it tests `JsonTypeConverter`).
  (f) Full documentation audit per the CLAUDE.md table.
- **Depends on:** Chunk 09
- **Artifacts consumed:** CLAUDE.md (doc-audit table), `.prawduct/artifacts/project-preferences.md`
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhDocumentTool.cs`, `src/Cordyceps/Tools/Unified/RhinoRenderTool.Lights.cs`, the Rhino tool files under `src/Cordyceps/Tools/Unified/`, `src/Cordyceps/Core/ToolHelpers.cs`, `src/Cordyceps/Tools/Unified/GhScriptTool.cs`, `src/Cordyceps/Tools/Unified/GhWireTool.cs`, the renamed test file, `src/Cordyceps/McpServer.cs` (instructions), CHANGELOG.md.
- **Tests:** existing suite stays green after the test-file rename and the `TryGetRhinoDoc` unification (behavior-neutral); no new logic to test beyond what 05–08 added.
- **Acceptance criteria:** no advertise-but-fail actions; light error strings name real params; one doc-null-check helper used everywhere; dead code removed; test file renamed; doc-audit complete; build + tests pass.
- **Type:** cumulative-final
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. Committed and chunk marked `[x]`; `git checkout -- releases/Cordyceps.gha`
  3. `/prawduct:critic cumulative` run and blocking findings resolved (PR gate)
  4. `/prawduct:pr create` → user reviews and merges PR #4 → `git checkout main && git pull`
  5. **`/clear`** (plan complete)

---

## Explicitly deferred (with rationale — not silently dropped)

- **Large-method refactors for size alone** — `GhScriptTool.SyncParamSide` (~98 lines),
  `ActionConfigure` (~104 lines), `GhCanvasTool.ActionBake`'s type-ladder, `ActionAdd`,
  `Zoomable.SetParameterCount`. Pure size-refactors are risky without characterization tests
  around host-coupled behavior; Chunks 05–09 reduce some of this incidentally. Recommend filing
  as backlog items to tackle once the relevant behavior has live-verified coverage. Lower
  severity than everything in-plan.
- **`WaitForRender` polling + capture `DoEvents()`+`Sleep` UI-thread stalls** — documented and
  intentional; defer unless they cause observed problems.

## Early Feedback Milestone

**Milestone chunk:** Chunk 01. **What the user can do:** immediately exercise the
no-longer-wedging server in Rhino — the highest-severity hazard is gone after the first chunk,
and every subsequent stage is independently shippable.

## Governance Checkpoints

**Commit & PR cadence:** Commit per chunk after `/prawduct:critic chunk` passes. Each stage's
LAST chunk is `Type: cumulative-final`: commit it, then run `/prawduct:critic cumulative` once
(its review IS the PR gate), then `/prawduct:pr create`. One PR per stage; user reviews and
merges; `/clear` before the next stage.

- After Chunk 01: `final` Critic — threading keystone validated before lifecycle work builds on it.
- After Chunk 03: `cumulative` Critic → PR #1 → merge → `/clear`.
- After Chunk 04: `cumulative` Critic → PR #2 → merge → `/clear`.
- After Chunk 05: `final` Critic — extraction convention validated before 06–08 repeat it.
- After Chunk 08: `cumulative` Critic → PR #3 → merge → `/clear`.
- After Chunk 10: `cumulative` Critic → PR #4 → merge → `/clear` (plan complete).
