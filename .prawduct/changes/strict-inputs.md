---
schema: 1
type: fix
scope: strict-inputs
date: 2026-11-09
title: a call does what it says, or says why not
---
**Why:** a review of the Hyperceps fork (Yibin Yang, MIT) against Cordyceps found three places
where a call that named something slightly wrong succeeded on something else, and one unrelated
protection gap. A misspelled argument name was dropped and the default used (`tools/call` only
looked up the method's own parameters). A near-miss component name created the shortest installed
name containing the text (`ComponentRegistry`, in two places with two different rules). A partial
port name wired a port nobody named (`ParamSpecLookup`'s substring step). And only the first
Cordyceps component on a canvas was infrastructure (`GetCordycepsInfrastructureIds` stopped at the
first match). Issues #88, #89, #90. The owner decided on 2026-11-09 that resolution goes strict as
a breaking change with no lenient mode, because the callers are agents that read the current help
on every session.

**What changed:** each rule is one host-free class with unit tests and a thin Grasshopper adapter.
`Core/ToolArgumentBinder` refuses unknown arguments before the tool runs, naming each, suggesting
the closest parameter and listing the valid ones. `Core/Naming/TypeNameRule` is the single
component-type rule (GUID, exact alias, pinned built-in, exact name optionally qualified by
category); `ComponentRegistry` is now only its adapter. `Core/ParamSpecLookup` takes an index, an
exact name or nickname, or a unique case-insensitive match, and never a partial one.
`Core/InfrastructureIds` decides which objects are infrastructure for every bridge component. The
type and port rules are adapted from Hyperceps; `LICENSE` carries Yibin Yang's copyright line and
the README credits him. `ParamSpecLookupTests` had two tests pinning substring matching; they were
rewritten to the strict contract with the owner's approval, not deleted.

**Decisions:** search actions keep loose matching because they return candidates and select
nothing. The ambiguous-name response moves its code from `error` to `code` so `error` is always
the explanation. The type catalog is rebuilt per call rather than cached, so a plugin loaded
mid-session is seen.

**Verification:** suite green (`prawduct-hook test-status`); Release build clean. The Grasshopper
adapters need a running Rhino and are enqueued as VRF-016, VRF-017 and VRF-018.

## Consumer note
breaking: a tool call with an argument name the tool does not have is now an error, and component-type and port names must be exact (a partial name is refused with suggestions). Write `Curve/Circle`, not `Circle`. Each error says how to write the call.
