---
schema: 1
type: feature
scope: script-rebuild
date: 2026-11-11
title: a script component never keeps running a program its ports no longer match
closes: [brookstalley/cordyceps#84]
---
**Why:** a Rhino 8 script component compiles its program from its source and its ports together:
the `RunScript` signature is shaped by the parameters. Storing new source or reshaping the ports
does not recompile on its own, so Cordyceps asks for a rebuild and reads the running program back.
`gh_script configure` with `inputs`/`outputs` and no `code` skipped that rebuild and said nothing,
leaving the component running a program built for ports it no longer had under `success:true`;
`gh_canvas zoomable` reached the same components by another door with the same gap (#84). A fix
for `configure` was built on `fix/configure-rebuild-decision` and never merged; the owner ruled on
2026-11-11 to salvage it after checking it was still needed, and to treat removing a port as
destructive for the default-nickname rule (#107), because removing a port destroys its wires.

**What changed:** the old branch's code was ported onto develop; its records were not.
`Core/ScriptConfigurePlan` decides the rebuild from what a call changed (ports reshaped or source
written), and `ScriptProgram` gained `DescribeUnchanged`, `NotCompared` and a `CompareRunning` that
reads the program without judging it when there is nothing to compare against. The host side moved
out of `GhScriptTool` into `Core/ScriptComponents` (script-component test, source reader,
`RebuildForChange`, `RebuildAndVerify`), so `gh_script` and `gh_canvas` share one gate. Every
`configure` route asks that gate and every response answers the rebuild question; a refused
parameter side is reported as `parametersApplied:false`. `zoomable` add/remove/set_count on a C# or
Python script rebuilds through the same gate and merges the same fields into its reply; other
variable-parameter components reply as before. New `Core/PortRemoval` computes, before the first
mutation, which existing ports a call would remove: zoomable `remove`, a shrinking `set_count`, the
`configure` parameter sync by name, and the sync `set` derives from a C# signature (read by the new
`Core/ScriptSignature`, which the sync itself now uses too). When that set is not empty,
`ToolHelpers.TryConfirmPortRemoval` re-resolves the id with `ResolveIntent.Destructive`, so a
default nickname is refused with the resolver's teaching error, prefixed by what the call would
have removed.

**Decisions:** #84 is answered "rebuild and report", not "refuse" (the plan's Inference 1). The
refusal is decided by what the call would remove, not by the action's name: calls that add or keep
ports still accept a unique default nickname (Inference 2). A rename is a removal plus an addition,
so it counts. A call the component itself would refuse (an out-of-range index, a port it cannot
remove) removes nothing and is not refused for its id. `set` uses the precise path, not the
fallback: the ports Cordyceps' sync removes are computable from the code and the current port
names. What it cannot predict is a port Rhino changes on its own while storing or building source
(the code-only `configure` route also calls the component's `SyncParameters`); VRF-022 step 9 asks
the operator to record whether that happens. `group_remove` stays outside the rule (Inference 3,
put back to the owner).

**Verification:** host-free decisions unit-tested (`ScriptConfigurePlanTests`, the `ScriptProgram`
additions, `PortRemovalTests`, `ScriptSignatureTests`); Release build clean. The rebuild after a
`zoomable` reshape and the refusal's live re-resolution need a running Rhino and are enqueued as
VRF-022.

## Consumer note
breaking: a call that removes an existing port no longer selects its component by a default nickname, even a unique one: `gh_canvas zoomable` with `operation='remove'` or a `set_count` below the current count, and `gh_script set`/`configure` whose `RunScript` signature or `inputs`/`outputs` drop or rename a port. The error starts `This call removes …` and prints the id to pass; nothing is changed. Calls that only add or keep ports are unaffected. Also: `gh_script configure` with only `inputs`/`outputs` now rebuilds and every `configure` response carries `rebuilt`/`rebuildSkipped`; a refused parameter side reports `parametersApplied:false` and `success:false`; `gh_canvas zoomable` on a script component rebuilds and carries the same rebuild fields.
