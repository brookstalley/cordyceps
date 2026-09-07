---
artifact: build-plan
version: 1
scope: param-spec
depends_on: []
last_validated: 2026-09-07
---

# Build Plan — one rule for reading a parameter spec

Branch: `fix/param-spec-lookup` (off `develop`)
Scope: BRO-3 — an out-of-range parameter index falls through to a substring name
match and wires the wrong port. Two resolvers answered the same question and
disagreed on the case that matters; the modifier tool's semantics (an index is an
index, and a miss carries a reason) are the ones kept, lifted into `Core` and
shared.
Critic mode: chunk (single chunk; the whole diff is one review).

## Context / decisions

- Baseline: 579/579 tests green; plugin Release build 0 warnings; branched from
  `develop` at f07eb79.
- The rule went into a new `Core/ParamSpecLookup.cs` rather than into
  `Core/ParamSyncPlan.cs` as the task plan guessed — `ParamSyncPlan` is the
  LCS-based param *reshaping* plan, an unrelated concept, and every other pure
  decision in `Core/` owns its own file.
- `availableOutputs`/`availableInputs` on `gh_wire(action='connect')` failures are
  **kept**, not folded into the new reason string. `boundary-patterns.md` names the
  tool/action contract's response shape as a surface that evolves additively;
  dropping a field an agent may read is a silent removal, and the redundancy costs
  nothing.
- The wire tool's `IGH_Param` early return (a bare parameter object ignores the
  spec and the side) is left alone — the task puts it out of scope.

## Chunks

### Chunk 1: the shared rule, and both tools pointed at it

- [x] `Core/ParamSpecLookup.cs`: a numeric spec resolves by index only; an
      out-of-range index returns a reason naming the index and the valid range; a
      name resolves exactly (name or nickname) then by substring; every miss
      carries a reason listing the available names.
- [x] `Cordyceps.Tests/ParamSpecLookupTests.cs` covers each branch, including the
      anchor case (an out-of-range index against a list containing a digit-bearing
      name) and culture handling.
- [x] `GhWireTool` and `GhCanvasTool.Modifiers` both call it; the wire tool returns
      the rule's reason at all six call sites instead of composing its own.
- [x] Documentation audit: server instructions, `gh_wire` action tips and notes,
      `Knowledge/CommonErrorsGuide.md`, `CHANGELOG.md`.

**Verification:**

- [x] `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release` — 594/594.
- [x] `dotnet build src/Cordyceps/Cordyceps.csproj -c Release` — 0 warnings.
- [ ] Live check in Rhino: on a component with two inputs, `gh_wire(action='connect',
      targetParam='2')` must report the range and create no wire. Enqueued in
      `operator-verification.md` — it needs a running Rhino.

## Status

- [x] Chunk 1
