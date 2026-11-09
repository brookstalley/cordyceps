---
artifact: build-plan
version: 1
scope: strict-inputs
branch: fix/strict-inputs
depends_on: []
partition: 01-03 delegated, isolated worktrees — disjoint files (ToolHelpers / McpServer / registry+ports+docs); the coordinator owns CHANGELOG, server-instruction text for 03, fragments and the combined run
last_validated: 2026-11-09
---

# Build Plan — a call does what it says, or says why not

Branch: `fix/strict-inputs` (off `develop` at 53ea43b)
Critic mode: cumulative (three small chunks shipping as one PR; the whole diff is one review).

## Goals

- **Response taken:** proceed. The owner answered the open questions on 2026-11-09: strict names in
  the existing tools as a breaking change with no lenient mode ("agents calling us get the help and
  everything, few will have memories of the past"); file the bugs found during the Hyperceps
  review and fix them now; credit Yibin Yang generously.
- **Product:** an agent's call either acts on exactly what it named or fails with enough to correct
  the call in one turn. Three defects break that today, each verified in source: a misspelled
  argument name is dropped silently; a near-miss component name creates the first substring match;
  a partial port name wires a port nobody named. A fourth, unrelated to naming, rides along because
  it is small and was found in the same review: only the first Cordyceps component on a canvas is
  protected infrastructure.
- **Architecture:** each rule is one host-free class under `Core/` with unit tests and a thin
  Grasshopper adapter. The type and port rules are adapted from Hyperceps (MIT, Yibin Yang) and
  live in a neutral home, so the later object-name resolver and batch editing can share them.
- **Out of scope:** object-name resolution (nicknames, GUID prefixes, refs), the canvas model, the
  edit transaction and undo, and every new action (batch, outline, tree, sweep). Those are the
  adoption program in the handoff notes and get their own per-wave plans.

## Requirements Confidence

**High** for chunks 01 and 02 (a defect with a stated correct behaviour). **Medium-high** for 03:
the rules are stated and a working reference exists, but which currently accepted spellings are
"deterministic, keep" versus "loose, remove" is a judgement the delegate makes and reports; the
coordinator confirms that list at integration and shows it to the owner in the PR.

Unverified until a live Rhino session: every Grasshopper-side adapter (operator verification).

## Context / decisions

- `ParamSpecLookupTests` pin substring port matching. Changing that contract is owner-approved
  (decision 1, 2026-11-09); the tests are rewritten to the strict contract, not deleted.
- Search actions (`gh_canvas search`/`find`) keep loose matching: they return candidates, they do
  not select a target.
- Norm stated by the owner mid-build (2026-11-09), binding on the follow-on waves, not on this
  plan (which adds no action): **new capabilities arrive as actions on the existing tools — few
  tools, many actions — and batch editing is integrated into that surface, not added as an eighth
  tool.** To be recorded as a `project-preferences.md` row when the first wave that adds an action
  is planned.
- Attribution: `LICENSE` gains `Copyright (c) 2026 Yibin Yang`; README credits Hyperceps for what
  is adapted here and for the designs shaping the follow-on waves, without claiming features that
  have not shipped.

## Chunks

### Chunk 01: every Cordyceps component is protected infrastructure
Type: code (bugfix). Delegate branch `worktree-agent-a8a05648eb838e8e6`.
- [ ] `Core/InfrastructureIds.cs` (host-free decision) + `ToolHelpers.GetCordycepsInfrastructureIds` as adapter.
- [ ] `InfrastructureIdsTests`: zero, one (unchanged), two bridges (the regression), shared neighbour.
- Done when: merged into this branch, filtered tests green, live check queued.

### Chunk 02: unknown tool arguments are an error
Type: code (contract). Delegate branch `worktree-agent-a7a5a7153c1928744`.
- [ ] `tools/call` refuses any argument that is not a parameter of the tool: every unknown name
      reported at once, a "did you mean" where one is close, the valid names listed; the tool is
      not invoked. Non-object `arguments` is an error. All-known calls behave as before.
- [ ] Host-free tests for each branch; existing MCP error-contract tests unchanged.
- [ ] Sweep of every documented example for argument names that are not parameters.
- Done when: merged, sweep findings fixed at integration, docs updated.

### Chunk 03: strict component-type and port names, with attribution
Type: code (contract). Delegate branch `worktree-agent-a201885374a61102f`.
- [ ] One type rule: GUID; exact alias rewrite; exact name / `Category/Name` /
      `Category/Subcategory/Name` ignoring case among non-obsolete components; anything else an
      error with candidates or suggestions. The two rules in `ComponentRegistry` collapse into it.
- [ ] One port rule: index; exact name or nickname; unique case-insensitive match; never partial.
- [ ] `ParamSpecLookupTests` rewritten to the strict contract; every changed expectation listed.
- [ ] `LICENSE`, README credits, header comments on adapted files.
- [ ] Documentation audit across help metadata, guides and prompts; every example still works.
- Done when: merged, server instructions updated by the coordinator from the delegate's text.

### Integration (coordinator)
- [ ] Merge 01-03; resolve `Cordyceps.Tests.csproj` link-line conflicts.
- [ ] `CHANGELOG.md` entries, change fragment, `GetServerInstructions()` text for 03, common-errors entry for 02.
- [ ] Operator-verification entries for each live check.
- [ ] Combined suite (`testament run --json`), evidence recorded; plugin Release build clean.
- [ ] `/prawduct:critic cumulative`; findings dispositioned; reflection.
