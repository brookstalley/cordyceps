---
artifact: build-plan
version: 1
scope: param-spec
depends_on: []
last_validated: 2026-09-07
---

# Build Plan — one rule for reading a parameter spec

Branch: `fix/param-spec-lookup` (off `develop`)
Scope: Coldtea task **BRO-3** (the Coldtea tracker, not a backlog id) — an out-of-range parameter
index falls through to a substring name match and wires the wrong port. Its backlog handle is
**issue #48 / `GHC-6P2M`**, which filed the same defect as a duplicated-resolver divergence.
Two resolvers answered the same question and disagreed on the case that matters; the modifier
tool's semantics (an index is an index, and a miss carries a reason) are the ones kept, lifted
into `Core` and shared.
Critic mode: cumulative (single chunk; the whole diff is one review).

## Requirements Confidence

**High.** The defect, the intended semantics, and the out-of-scope list all arrived stated in the
task (`BRO-3`), and the "correct" behaviour was already running in this repo — the modifier tool's
resolver — rather than being designed here. The three requirements-shaped judgements taken during
the build, each recorded below rather than assumed: keeping `availableOutputs`/`availableInputs`,
refusing an empty spec instead of silently meaning port 0, and refusing a numeric spec too large
for an `Int32`. Nothing in the task settles those; they follow from `boundary-patterns.md` and
from the learnings rule against conflating "unparseable" with "empty".

Unverified: the live half — that no wire is *created* — needs a running Rhino (VRF-015).

## Context / decisions

- Baseline: suite green at branch point (`prawduct-hook test-status`); plugin Release build 0
  warnings; branched from `develop` at f07eb79.
- The rule went into a new `src/Cordyceps/Core/ParamSpecLookup.cs` rather than into
  `src/Cordyceps/Core/ParamSyncPlan.cs` as the task plan guessed — `ParamSyncPlan` is the
  LCS-based param *reshaping* plan, an unrelated concept, and every other pure decision in
  `Core/` owns its own file.
- `availableOutputs`/`availableInputs` on `gh_wire(action='connect')` failures are **kept**, not
  folded into the new reason string. `boundary-patterns.md` names the tool/action contract's
  response shape as a surface that evolves additively; dropping a field an agent may read is a
  silent removal, and the redundancy costs nothing.
- The `IGH_Param` early return — a bare parameter object is returned as the target, discarding both
  the spec and the side — is left alone in **both** tools (`GhWireTool.GetParameter` and
  `ResolveModifierParam`); the task puts it out of scope. What is lost there is only the error, not
  the wire: a floating param has one port and is legitimately either end of a connection, so
  `sourceParam='7'` returns `success: true` against the port the caller would have got anyway.
  Filed as **issue #81** so the deferral has a handle rather than living only in this plan, at
  `stage: requirements` because the current behaviour is documented as intentional in two
  user-facing places and refusing a bogus spec would be a contract change. The agent-facing docs now
  say the index guarantee is a *component* guarantee, so nothing shipped overstates it.
- A numeric spec that overflows `Int32` is refused as an out-of-range index rather than searched
  for as a name. `int.TryParse` answers "does this fit in an Int32", which is narrower than "is
  this written as a number", and the gap reopens the very defect being fixed at the far end of the
  range.

## Chunks

### Chunk 1: the shared rule, and both tools pointed at it

- [x] `src/Cordyceps/Core/ParamSpecLookup.cs`: a numeric spec resolves by index only; an
      out-of-range index (including one too large for an `Int32`) returns a reason naming the index
      and the valid range; a name resolves exactly (name or nickname) then by substring; every miss
      carries a reason listing the available names.
- [x] `src/Cordyceps.Tests/ParamSpecLookupTests.cs` covers each branch, including the anchor case
      (an out-of-range index against a list containing a digit-bearing name), the overflow case,
      the ambiguous-substring tie-break, and culture handling.
- [x] `src/Cordyceps/Tools/Unified/GhWireTool.cs` and
      `src/Cordyceps/Tools/Unified/GhCanvasTool.Modifiers.cs` both call it; the wire tool returns
      the rule's reason at all six call sites instead of composing its own.
- [x] Documentation audit: `src/Cordyceps/McpServer.cs` server instructions, `gh_wire` action tips
      and notes, `src/Cordyceps/Knowledge/CommonErrorsGuide.md`, `CHANGELOG.md`.

**Verification:**

- [x] `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release` — suite green; totals via
      `prawduct-hook test-status`, not copied here.
- [x] `dotnet build src/Cordyceps/Cordyceps.csproj -c Release` — 0 warnings.
- [ ] Live check in Rhino: on a component with two inputs, `gh_wire(action='connect',
      targetParam='2')` must report the range and create no wire. Enqueued as VRF-015 in
      `.prawduct/operator-verification.md` — it needs a running Rhino.

## Status

- [x] Chunk 1
