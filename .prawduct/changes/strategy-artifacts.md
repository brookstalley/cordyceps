---
schema: 1
type: docs
scope: strategy-artifacts
date: 2026-11-11
title: seven strategy artifacts written from source, and the doctor's repairs
closes: [brookstalley/cordyceps#36]
---
**Why:** the owner ruled that a project thousands of people rely on needs its design written down
(2026-11-11, `.prawduct/owner-rulings.md`). Coverage tracking listed strategy-class artifacts as
missing, and `/prawduct:doctor` had five repairs open.

**What changed:**
- New under `.prawduct/artifacts/`: `architecture.md`, `data-model.md`, `security-model.md`,
  `nonfunctional-requirements.md`, `api-contract.md` (closes #36), `operational-spec.md` and
  `observability-strategy.md`. Each was written against source, records its gaps, and ends with
  norm candidates that are not ratified. Ratification is deferred by the owner.
- Corrected descriptions: the transport is stateless Streamable HTTP with no SSE
  (`docs/architecture.md`), and `boundary-patterns.md` no longer names `DeprecationRegistry` as the
  API contract's evolution mechanism (it covers Grasshopper components only).
- `project-state.yaml` records `api_versioning_approach` (owner deferral) and
  `api_error_model_approach` (as built, not a ruling).
- Doctor repairs: a session-start hook in `.claude/settings.json` that warns when Prawduct is not
  installed; the governance anchor moved from `CLAUDE.md` into `AGENTS.md`, which `CLAUDE.md`
  imports; `.prawduct/.pr-body.md` gitignored; the param-spec and script-rebuild plans' `## Status`
  blocks imported as chunk facts and removed.
- `project-preferences.md` records the owner's standing consent for anonymous governance stats.

Found while writing, not fixed here: snapshots are global per Rhino process, so `revert` can
install one definition's snapshot into another; and five request-handling `catch (Exception)`
blocks in `McpServer.cs` carry no waiver comment.

## Consumer note
none: design documentation and repo governance; no plugin behaviour changes
