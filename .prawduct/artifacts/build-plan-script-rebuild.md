---
artifact: build-plan
version: 2
scope: script-rebuild
branch: feature/script-rebuild
base: develop
working-branches: []
last_validated: 2026-11-11
depends_on: []
governed_by: [.prawduct/artifacts/project-preferences.md, .prawduct/artifacts/boundary-patterns.md]
partition: serial, delegated — one Opus delegate in an isolated worktree builds 01, 02 and 03 in order, because all three edit `GhScriptTool.cs` and `GhCanvasTool.Zoomable.cs` and 02 reuses 01's rebuild gate; the coordinator owns integration, the combined run, the review and the records
---

# Build Plan — a script component never keeps running a program its ports no longer match

Branch: `feature/script-rebuild` (off `develop` at 6f4b99c). One PR.
Critic mode: one `cumulative` review at the end (a three-chunk plan; Chunk 03 is `cumulative-final`).

## Goals

- **Response taken:** proceed, citing the owner's rulings of 2026-11-11 (`.prawduct/owner-rulings.md`):
  salvage the orphaned `fix/configure-rebuild-decision` branch after checking it is still valid and
  needed, and treat removing a port as destructive for the default-nickname rule (#107).
- **Product:** an agent that reshapes a script component's ports, through either tool, gets a
  component running a program built for those ports, and a reply that says whether the rebuild
  happened. A call that would cut wires never acts on an object picked by a default nickname.
- **Validity of the old branch (checked 2026-11-11):** still needed. On develop,
  `src/Cordyceps/Tools/Unified/GhScriptTool.cs` `ActionConfigure` sets `rebuildFields` only when
  source was written (`if (writtenSource != null)`), so a configure call with `inputs`/`outputs`
  and no `code` neither rebuilds nor reports. The old branch's host-free pieces
  (`Core/ScriptConfigurePlan.cs`, the `ScriptProgram` additions `DescribeUnchanged`, `NotCompared`
  and the null-`expected` read in `CompareRunning`, and both test files) still fit develop's code.
  Its records do not: it adds a monolithic change-log entry, a separate plan, and a VRF-015 entry
  whose number param-spec has since taken. Port the code; rewrite the records.
- **Inferences** (made on the owner's behalf; the boundary review shows them to the owner):
  1. **#84 is answered "rebuild and report", not "refuse".** `zoomable` on a script component
     rebuilds through the same gate as `configure` and reports in the same words. Why: one action
     working on every variable-parameter component is the cheaper contract for agents, the gate
     already exists after Chunk 01, and refusing would break agents that use `zoomable` on scripts
     today. Non-script components (Merge, Entwine, Gate) are unchanged and get no rebuild fields.
  2. **#107's refusal is decided by what the call would remove, not by the action's name.** A call
     is destructive when it would remove at least one existing port: `zoomable remove`;
     `zoomable set_count` to fewer ports than now; `gh_script configure` or `set` when the planned
     parameter sync drops a port that exists. A call that only adds or keeps ports still accepts a
     unique default nickname. Why: refusing harmless calls teaches agents that the rule is noise.
     Mechanism: resolve with ordinary intent, compute the removal set before the first mutation,
     and when it is non-empty re-check the id with `ResolveIntent.Destructive` (which gives the
     existing teaching refusal). If the removal set for `set` (ports derived from code by
     `SyncScriptParams`) cannot be computed without mutating, say so in the report instead of
     guessing; the fallback is to treat every `set` that changes the port list as destructive.
  3. **`group_remove` stays outside the rule.** It removes membership only; nothing is lost, and
     `group_add` reverses it. The owner's "yes" answered a question that named it; the
     recommendation to leave it out is put back to the owner and is not built here.
- **Constraints:** validate-then-mutate (learnings, core): resolve and decide everything before
  the first document mutation. Decision logic goes in a host-free `Core/` file, linked into the
  test project, with tests. All document access through `ExecuteOnUiThread`.
- **Release:** the refusals in Chunk 03 are a breaking change for callers that remove ports by a
  default nickname. It ships in the next major, which the owner ruled waits for the transaction wave.
- **Not in scope:** the outline/tree naming questions (owner, open); `group_remove`; #92 (python
  pin creates a Script with no language).
- **Decisions made mid-build:** *(one line each, naming what settled it)*
  - The rebuild gate, source reader and script-component test moved from `GhScriptTool` to a new host-coupled `src/Cordyceps/Core/ScriptComponents.cs` (not linked into tests), so `GhCanvasTool` reaches the one copy; settled by Chunk 02's "no duplicate copies".
  - develop has no zoomable hazard tip to replace (the old branch's tip never merged), so Chunk 02 adds a behaviour tip instead; settled by reading develop's `zoomable` `ActionInfo`.
  - `set` uses the precise removal path, not the fallback: Cordyceps' own sync is computable from the code and the port nicknames, so `RunScript` parsing moved to host-free `Core/ScriptSignature.cs`, which the sync and the prediction now share; settled by Inference 2's "say so instead of guessing" only where a guess was needed.
  - Ports Rhino itself may change while storing or building source (the code-only `configure` route's `SyncParameters`, the rebuild) are outside the prediction; recorded in VRF-022 step 9 rather than guessed at.
  - A removal the component itself would refuse (index out of range, `CanRemoveParameter` false for the first port set_count would remove) counts as no removal, so the call fails on its own error, not on the id; settled by "only when the call would actually remove an existing port".
  - `zoomable set_count` already at its count on a script component reports `rebuildSkipped` (nothing changed) so every successful script reply answers the rebuild question; settled by Chunk 02's acceptance criterion.
  - Two older defects the delegate reported, fixed at integration because the port-removal rule made them bite harder: the signature reader treated Python in the unified Script component (type name `ScriptComponent`) as a C# signature with no inputs, so the sync removed every input. It now recognises Python from the source too. And `configure` accepted non-script components, treating a Merge's output 0 as a script's `out`; it now refuses them as `set` does. Settled by the no-pre-existing-exception rule.
- **Level:** High for 01 (ported, already reviewed once on its own branch). Medium for 02 and 03:
  the rebuild on a `zoomable` reshape and the removal-set computation are host behaviour checked
  only by code reading until VRF-022 runs.

## Today's behaviour this plan changes (verified in source on develop at 6f4b99c)

- `GhScriptTool.ActionConfigure`: param-only call → no rebuild, no `rebuilt`/`rebuildSkipped` fields.
- `GhCanvasTool.Zoomable.cs` `AddParameter`, `RemoveParameter`, `SetParameterCount`: end at
  `VariableParameterMaintenance()` + `ExpireSolution(false)`; no rebuild, no outcome field (#84).
- Zoomable and `gh_script` resolve their `id` through `TryGetUnprotectedComponent*` without an
  intent, i.e. ordinary: a unique default nickname is accepted even when ports are removed (#107).

## Build Chunks

### Chunk 01: configure rebuilds whenever it reshapes ports, and always reports it

- **Description:** Port the host-free decision from `fix/configure-rebuild-decision` (read it with
  `git show fix/configure-rebuild-decision:<path>`; diff against its merge-base f07eb79) onto
  develop's `ActionConfigure`, which has moved since (strict inputs, canvas-read id resolution).
- **Deliverables:**
  - new `src/Cordyceps/Core/ScriptConfigurePlan.cs` and the `src/Cordyceps/Core/ScriptProgram.cs`
    additions, as on the old branch, adapted to develop.
  - `src/Cordyceps/Tools/Unified/GhScriptTool.cs`: every configure route asks the plan whether to
    rebuild (parameters actually reshaped, or source actually written); a param-only rebuild
    compares against the stored source or reports `NotCompared`; a call that changed nothing
    reports `DescribeUnchanged`. One gate for all routes.
  - new `src/Cordyceps.Tests/ScriptConfigurePlanTests.cs` and the `ScriptProgramTests.cs`
    additions; the link line in `src/Cordyceps.Tests/Cordyceps.Tests.csproj`.
- **Tests:** decision matrix (params × source); `DescribeUnchanged` vocabulary; `CompareRunning`
  with null expected never diverges.
- **Acceptance criteria:** a configure call reports a rebuild outcome on every route; tests pass.

### Chunk 02: zoomable on a script component rebuilds and reports (#84)

- **Description:** Inference 1. After a successful add/remove/set_count on a script component,
  rebuild through Chunk 01's gate and add the same rebuild fields to the reply. Non-script
  components: unchanged replies.
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhCanvasTool.Zoomable.cs`; the script-component
  test and the rebuild call reachable from it (move `RebuildAndVerify` / `IsScriptComponent` to a
  shared place if `GhCanvasTool` cannot reach them; no duplicate copies). The `zoomable` tip that
  documents the hazard is replaced by the behaviour.
- **Tests:** any new decision logic is host-free and tested; the host glue is code-read and queued
  in VRF-022.
- **Acceptance criteria:** a script reshaped by zoomable reports `rebuilt` or `rebuildSkipped`;
  a Merge reshaped by zoomable reports as before.

### Chunk 03: removing a port refuses a default nickname (#107)

- **Description:** Inference 2. Compute each call's removal set before mutating; when non-empty,
  the id must pass `ResolveIntent.Destructive`.
- **Deliverables:** a new host-free removal-set helper in `src/Cordyceps/Core/` (zoomable counts;
  the configure/set sync plan), linked into the tests; the three call sites in
  `src/Cordyceps/Tools/Unified/GhCanvasTool.Zoomable.cs` and `src/Cordyceps/Tools/Unified/GhScriptTool.cs`.
- **Tests:** removal set for remove, set_count down/equal/up, configure dropping/keeping/renaming
  a port, set with a changed signature; refusal only when the set is non-empty.
- **Documentation (whole plan, done in this chunk):** `ActionInfo` for `zoomable`, `configure`,
  `set`; `GetServerInstructions()` where it states the default-nickname rule;
  `src/Cordyceps/Knowledge/CommonErrorsGuide.md`; `CHANGELOG.md` under `## [Unreleased]`
  (Fixed: configure and zoomable rebuild; Changed, breaking: port removal refuses a default
  nickname); `.prawduct/operator-verification.md` new VRF-022 covering all three chunks;
  `.prawduct/changes/script-rebuild.md` narrative and consumer note.
- **Type:** cumulative-final
- **Acceptance criteria:** the suite is green; every surface that lists the destructive actions
  lists the port removals.
- **Done when:** committed, then `/prawduct:critic cumulative` run and blocking findings resolved.

## Status

- [ ] Chunk 01: configure rebuilds whenever it reshapes ports, and always reports it
- [ ] Chunk 02: zoomable on a script component rebuilds and reports (#84)
- [ ] Chunk 03: removing a port refuses a default nickname (#107)
