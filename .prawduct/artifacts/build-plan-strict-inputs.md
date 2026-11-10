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
  tool.** Recorded as a norm in `.prawduct/artifacts/project-preferences.md` (Architecture
  Patterns and the enforcement table).
- Previously accepted spellings, kept or removed (the judgement the Requirements Confidence
  section defers to the owner): kept — GUID; every alias, as an exact whole-text rewrite; the
  pinned built-ins; exact name ignoring case; a name qualified by category; a name followed by its
  category in parentheses. Added — a name qualified by category and subcategory. Removed — the
  shortest-name-containing-the-text fallback on add; the second registry rule that mapped bare
  words such as point, curve, line, circle and number to parameter types and then fell back to a
  substring; the first-match lookup behind component docs and the component resource URI; the
  substring step for port names.
- Release number: two entries are marked breaking, so the next release is a major version by
  semantic versioning. The owner sets the number at release time.
- Attribution: `LICENSE` gains `Copyright (c) 2026 Yibin Yang`; README credits Hyperceps for what
  is adapted here and for the designs shaping the follow-on waves, without claiming features that
  have not shipped.

## Chunks

### Chunk 01: every Cordyceps component is protected infrastructure
Type: code (bugfix). Delegate branch `worktree-agent-a8a05648eb838e8e6`.
- [x] `src/Cordyceps/Core/InfrastructureIds.cs` holds the host-free decision; the infrastructure-id function in `src/Cordyceps/Core/ToolHelpers.cs` is its adapter.
- [x] `src/Cordyceps.Tests/InfrastructureIdsTests.cs`: zero, one (unchanged), two bridges (the regression), shared neighbour.
- Done when: merged into this branch, filtered tests green, live check queued.

### Chunk 02: unknown tool arguments are an error
Type: code (contract). Delegate branch `worktree-agent-a7a5a7153c1928744`.
- [x] `src/Cordyceps/Core/ToolArgumentBinder.cs`, called from `src/Cordyceps/McpServer.cs`: a
      tool call refuses any argument that is not a parameter of the tool — every unknown name
      reported at once, a "did you mean" where one is close, the valid names listed; the tool is
      not invoked. A non-object arguments value is an error. All-known calls behave as before.
- [x] `src/Cordyceps.Tests/ToolArgumentBinderTests.cs` covers each branch; existing MCP
      error-contract tests unchanged.
- [x] Sweep of every documented example for argument names that are not parameters (none found
      among call-form examples).
- Done when: merged, sweep findings fixed at integration, docs updated.

### Chunk 03: strict component-type and port names, with attribution
Type: code (contract). Delegate branch `worktree-agent-a201885374a61102f`.
- [x] `src/Cordyceps/Core/Naming/TypeNameRule.cs` is the one type rule: GUID; exact alias
      rewrite; exact name, optionally qualified by category or by category and subcategory,
      ignoring case among non-obsolete components; anything else an error with candidates or
      suggestions. `src/Cordyceps/Core/ComponentRegistry.cs` is reduced to its adapter.
- [x] `src/Cordyceps/Core/ParamSpecLookup.cs` is the one port rule: index; exact name or
      nickname; unique case-insensitive match; never partial.
- [x] `src/Cordyceps.Tests/ParamSpecLookupTests.cs` rewritten to the strict contract (two tests
      changed expectation); `src/Cordyceps.Tests/TypeNameRuleTests.cs` added.
- [x] `LICENSE`, `README.md` credits, header comments on adapted files.
- [x] Documentation audit across help metadata, guides and prompts; every example still works
      under the strict rules (names corrected against a live install; items 9, 11 and 12 of
      VRF-018 in `.prawduct/operator-verification.md` queue the run on a build of this branch).
- Done when: merged, server instructions updated by the coordinator from the delegate's text.

### Integration (coordinator)
- [x] Merge 01-03 (no conflicts).
- [x] `CHANGELOG.md` entries, the change fragment, server-instruction text for 03, common-errors entry for 02.
- [x] Operator-verification entries VRF-016, VRF-017, VRF-018 in `.prawduct/operator-verification.md`.
- [x] Combined suite green with evidence recorded; plugin Release build clean.
- [x] Cumulative Critic findings dispositioned; reflection.
