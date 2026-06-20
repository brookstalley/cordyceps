# Build Plan — Backlog batch: docs sync, test coverage, code-quality cleanup

**Work size/type:** Medium · Mixed (Docs sync · Test coverage · Refactor/cleanup · Code-quality sweep)
**Branch:** `fix/mcp-error-contract` (per user decision: MCP-4R2K stays on this branch unmerged; the four ready backlog items stack on top and ship as **one PR**)
**Critic mode:** chunk on the medium refactor (Chunk 03) → **cumulative-final** on the last chunk (Chunk 04), which is the single `/prawduct:critic cumulative` pass over `merge-base...HEAD` and gates the PR.

## Prior work already on this branch (complete — not re-reviewed here except by the final cumulative)

- **MCP-4R2K** (commits `905825c`, `0a525d0`) — Honor the MCP error contract at the server boundary. Chunk complete; Critic clean (verify-resolutions, 0 blocking / 0 warning / 1 resolved note); reflection captured in `reflections.md`. Its prawduct `change-log.md` entry is added as part of this batch (Chunk 01 housekeeping) so `regen-views` has a source. The cumulative-final Critic (Chunk 04) reviews `merge-base...HEAD`, which includes these two commits.

## Confidence Check

1. **Problem:** Four filed, ready backlog items, all rooted in the same survey: (DOC-8M3T) the MCP `initialize` server-instructions listing lags the code by 11 live actions; (TST-6W7H) two host-free `Core` contract classes (`RequestValidator`, `UnifiedToolHelpers`) have zero unit coverage because they aren't linked into the test project; (CQ-2X8B) duplicated proxy-instantiation code and one dead helper method; (CQ-5J9N) ~21 silent-swallow `catch` blocks and ~28 unwaivered broad catches across the codebase.
2. **Success:** server instructions list every live action (no omissions, no phantoms); `RequestValidator`/`UnifiedToolHelpers` are linked and meaningfully unit-tested; the proxy-instantiation duplication is unified behind one host-free-as-possible helper and the dead `ExecuteOnUiThreadAsync` is gone; every silent swallow either logs with context or is narrowed, and every remaining broad catch is either narrowed or carries a `prawduct:allow prawduct/broad-except` waiver with a genuine reason. `dotnet test` stays green throughout.
3. **Out of scope:** GHD-3K6F (undo/redo keep-cut — deferred to discovery after this batch, per user). Rewriting the dispatch architecture. Any tool name/action/param/response-shape change (the PRIMARY contract surface — this batch is strictly additive to docs + internal cleanup, never a breaking change).

**Requirements confidence: High.** All four items are filed with stage `ready` (DOC/TST/CQ) and grounded by a fresh read-only code survey (file:line confirmed). Two backlog premises were found inaccurate and are corrected below — recorded as deliberate scope decisions, not silent drops.

### Corrected backlog premises (CQ-2X8B) — explicit scope decisions

- **"Route proxy/param enumeration through `ToolHelpers.BuildParameterList`."** Not viable as written: `BuildParameterList(IList<IGH_Param>, bool)` reads *live-instance* wiring state (`SourceCount`/`Recipients`) and emits anonymous `{name,nickname,type,sourceCount/recipientCount,optional}`, whereas the two duplicated blocks instantiate a *fresh proxy* and need divergent projections (GhInspectTool wants `{name,type,optional,description}`; ComponentRegistry wants typed `ParameterInfo` with `Access`/`Nickname`). **Decision:** extract a small shared helper that unifies only the genuinely-shared scaffold — `proxy.CreateInstance()` → `is IGH_Component` guard → per-param iteration — leaving each caller's projection in place. `BuildParameterList` is left unchanged.
- **"Drop the dead default switch arms."** Not removable: each tool dispatches via a C# **switch *expression*** over a `string`, where the `_ =>` arm is *syntactically required* (omitting it is CS8509 / a runtime `SwitchExpressionException`). They are unreachable only because `ValidateAction` runs first — i.e. cheap defense-in-depth, not dead code. **Decision:** leave the arms; do not attempt removal. (The full 7×-dispatch-preamble consolidation is also **descoped** — it touches all 7 classes of the PRIMARY contract surface for impact:L gain, the `_ =>` arms can't go away anyway, and wrapping the `switch` in a `Func` adds indirection. Recorded against CQ-2X8B at chunk close.)

## Boundary Investigation

- **Embedded Documentation Contract** (agent-facing, must track code): Chunk 01 edits `GetServerInstructions()`. Investigation already done via the survey — the change is purely **additive** (11 missing action names; zero phantoms to remove), so it tightens the contract toward the code and breaks nothing. Consumers are AI agents reading `initialize`; no in-repo consumer.
- **MCP Tool / Action Contract** (PRIMARY, external): No chunk changes any tool name, action vocabulary, parameter, or `{success,...}` response shape. CQ-2X8B/CQ-5J9N touch *internals* (proxy enumeration, catch handling) behind that surface. The error-contract behavior MCP-4R2K just established (broad catch at the tool boundary → `{success:false,error}`) is a **constraint** on CQ-5J9N: tool-boundary broad catches must be *waived*, never narrowed away (narrowing would let exceptions escape across the MCP boundary again).
- **Host API (FOREIGN):** CQ-2X8B's helper wraps `IGH_ObjectProxy.CreateInstance()` / `IGH_Component.Params` — already-used host surface, no new host behavior relied upon; verified live behavior unchanged (refactor preserves the existing call + catch semantics, only adds logging).

## Chunks

### Chunk 01 — DOC-8M3T: sync `GetServerInstructions()` + MCP-4R2K change-log housekeeping

- **Type:** trivial — **Trivial because:** additive sync of 11 already-existing action *names* into three comma-separated lines of the `GetServerInstructions()` verbatim string (`McpServer.cs:552/557/558`); no logic, no behavior, no control flow, no new files, no test-file edits, no `skills/`/`methodology/`/`templates/`/`CLAUDE.md` edits. Risk is bounded to documentation-string accuracy, which the survey verified exactly (11 missing, 0 phantom).
- Add to `gh_canvas` line: `zoomable`. To `rhino_scene` line: `set_color`, `bbox`. To `rhino_render` line: `view_save, view_load, view_list, view_delete, light_add, light_list, light_set, light_delete`.
- Add the missing prawduct `change-log.md` tagged entry for MCP-4R2K (so `regen-views` has a source for this branch's chunks).
- Doc audit: this IS the doc-contract fix; per-tool `ActionInfo` and Knowledge guides already list these actions (survey-confirmed) — no other doc surface lags. No root `CHANGELOG.md` entry (server-instructions are agent-facing internal docs, consistent with prior practice).
- **Done when:** server-instructions lists match the per-tool action vocabulary exactly; build green; committed.

### Chunk 02 — TST-6W7H: link `RequestValidator` + `UnifiedToolHelpers` into the test project

- **Type:** code (test-only; no production change).
- Add `..\Cordyceps\Core\RequestValidator.cs` and `..\Cordyceps\Core\UnifiedToolHelpers.cs` to the `<Compile Include>` list in `Cordyceps.Tests.csproj` (both confirmed host-free: `System`/`System.IO`/`Linq`/`Newtonsoft.Json` only).
- New `RequestValidatorTests.cs`: cover `ValidateRequired`, `ValidateNotWhitespace`, `ValidateGuid`/`ValidateGuidFormat` (valid + malformed), `ValidateRange` (double+int, in/out of bounds), `ValidatePositive`/`ValidateNonNegative` (double+int), `ValidateFileExtension` (case-insensitive, dotless edge), `ValidateOneOf` (case-insensitive, miss), and the `out error` message + boolean contract on each.
- New `UnifiedToolHelpersTests.cs`: cover `ValidateAction` (null/empty action, unknown action, missing-required, valid → null), `GetParam<T>` (direct match, double/int/bool/string conversions, `"true"/"1"` bool strings, complex JSON round-trip, missing → default, **and the conversion-failure → default path** at `:171`), `GenerateHelp` (shape, optional/example/tips omission when empty), and `BuildParams` (null-skip).
- **Done when:** new tests added and **all green**; `test-evidence record` shows the new higher count; `dotnet test -c Release` passes; committed.

### Chunk 03 — CQ-2X8B: unify proxy-instantiation + remove dead `ExecuteOnUiThreadAsync`

- **Type:** code (behavior-preserving refactor + dead-code removal). **Critic mode:** chunk (run `/prawduct:critic` after commit).
- Add a host-coupled helper (in `Core/ToolHelpers.cs`, sibling to `BuildParameterList`) that unifies the shared scaffold: instantiate a proxy, guard `is IGH_Component`, hand the component (or its input/output `IGH_Param` lists) to a caller-supplied projection. Route **both** `GhInspectTool.ActionDocs` (`:449-460`) and `ComponentRegistry.CreateComponentMatch` (`:366-403`) through it. Each caller keeps its own divergent projection (anon `{name,type,optional,description}` vs typed `ParameterInfo`). The helper's single `catch` **logs via `DebugLog`** with `proxy.Desc.Name`/`Guid` context (this removes 2 of CQ-5J9N's silent swallows at the source — `GhInspectTool.cs:460` and `ComponentRegistry.cs:400`).
- Remove `GrasshopperContext.ExecuteOnUiThreadAsync<T>` (`:101-127`) — survey-confirmed zero callers (only the definition matches).
- Behavior preservation: existing tests stay unchanged (they're contracts); the refactor must not alter any tool's JSON output. Verify the proxy projections produce identical fields.
- Doc audit: no user-facing surface changes (internal helpers). No ActionInfo/server-instructions/Knowledge impact.
- **Done when:** duplication unified, dead method gone, `dotnet test -c Release` green, `/prawduct:critic` (chunk) run and blocking findings resolved, committed, Status updated.

### Chunk 04 — CQ-5J9N: broad-catch / silent-swallow sweep  (cumulative-final)

- **Type:** cumulative-final — last chunk of the single-PR batch; its review IS the one `/prawduct:critic cumulative` over `merge-base...HEAD` (covers MCP-4R2K + Chunks 01-04). **Critic mode:** cumulative.
- **Scope by pattern, not line number** (lines shift): sweep **every** remaining `catch` in `src/Cordyceps/` (survey baseline: 56 total; ~2 already eliminated by Chunk 03; ~19 silent, ~28 broad-logged, 6 rethrow/control-flow, 1 already waived).
  - **Silent swallows (priority):** every `catch` that neither logs nor rethrows must gain a `DebugLog` line with in-scope context, **or** be narrowed to the specific expected exception type when that fully explains the swallow (e.g. JSON parse → `JsonException`, color parse → `FormatException`). Known sites: `GhScriptTool.cs` (`SyncScriptParams`/`SyncParameters`/`VariableParameterMaintenance` trio; `TryGetScriptSource` reflection cascade; `ParseParamDefs`), `Core/UnifiedToolHelpers.cs:171` (`GetParam<T>`), `McpServer.cs:358` (doc-name lookup), `RhinoRenderTool.Materials.cs:194` (cleanup-in-catch), `Core/DeprecationRegistry.cs`, `Core/ToolHelpers.cs` color parsers.
  - **Broad-but-logged catches at the tool boundary** (the sanctioned `{success:false,error}` pattern per `project-preferences.md`): add `// prawduct:allow prawduct/broad-except -- tool boundary; logs + returns structured error` rather than narrowing (narrowing would re-break the MCP error contract MCP-4R2K fixed).
  - **Control-flow / infra catches** (`GrasshopperContext` rethrows, `HttpListenerException when cancelled`, typed `JsonException`/`ReflectionTypeLoadException`): leave as-is or waive with reason; do not silence.
  - **Never** add a waiver that silences — a swallow without a log is always a finding (methodology).
- Doc audit: `Knowledge/CommonErrorsGuide.md` — add any genuinely new failure mode surfaced by newly-added logging (only if real); otherwise note no new error patterns. No user-facing behavior change (logging is observability; structured errors already returned).
- **Done when:** no silent swallow remains in `src/Cordyceps/`; every broad catch is narrowed or waived-with-reason; `dotnet test -c Release` green; `/prawduct:critic cumulative` run (the PR gate) and blocking findings resolved; committed; Status updated; reflection captured.

## Verification strategy

- **Per chunk:** `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release` green; record via `prawduct-hook test-evidence record`. **After every build/test, `git checkout -- releases/Cordyceps.gha`** (post-build target restamps the tracked binary — learnings.md) and discard the regenerable `.prawduct/.work-model-index.json` before committing.
- **Contract (Chunk 01):** diff server-instructions action lists against per-tool `ActionInfo` keys — must match (manual, no host needed).
- **Refactor (Chunk 03):** tests are the behavior-preservation net; confirm proxy projection fields unchanged.
- **Host-touching paths** (proxy enumeration, catch sites in GH tools): unit tests can't exercise the live host; verified by behavior-preservation (unchanged outputs) + Critic. Live Rhino re-verification noted as a carryover if any runtime path changes.

## Governance checkpoints

1. After Chunk 03 (the only architectural-ish change): chunk Critic — confirm the refactor preserved behavior before the sweep piles on.
2. After Chunk 04: cumulative Critic over the whole branch — the PR-readiness gate.

## Status

- [x] Chunk 01 — DOC-8M3T: sync GetServerInstructions + MCP-4R2K change-log entry
- [ ] Chunk 02 — TST-6W7H: link RequestValidator + UnifiedToolHelpers, add tests
- [ ] Chunk 03 — CQ-2X8B: unify proxy-instantiation, remove dead ExecuteOnUiThreadAsync
- [ ] Chunk 04 — CQ-5J9N: broad-catch / silent-swallow sweep (cumulative-final)

**Context:** Chunk 01 complete. `GetServerInstructions()` now lists all 11 previously-missing actions (gh_canvas `zoomable`; rhino_scene `set_color`,`bbox`; rhino_render 4 view + 4 light actions) in code-order; MCP-4R2K prawduct change-log entry added. Build green (68/68). Next: Chunk 02 (TST-6W7H).
