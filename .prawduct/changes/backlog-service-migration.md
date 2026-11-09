---
schema: 1
type: chore
scope: backlog-service-migration
date: 2026-08-29
title: migrate the backlog from markdown to GitHub Issues
---
**Why:** `.prawduct/backlog.md` was a single merge-prone file that only this repo's tooling could
read. Moving it onto GitHub Issues puts the backlog where the reporters already are — the repo
already carried 11 reporter issues that the markdown backlog could not see or link to — and makes
it queryable by the backlog service instead of by reading a 57 KB file into context every session.

**What:** All 41 items migrated to `brookstalley/cordyceps` issues **#35-#75**, verified by
`prawduct-hook backlog verify-migration` at **exit 0** (`missing` / `unaliasable` / `collisions` /
`status_mismatch` / `duplicate_alias` all empty; 41 source items, 41 aliased). Every item kept its
hand-minted `PFX-XXXX` id as an `id:PFX` alias, so existing citations in `docs/` still resolve.
Bodies migrated verbatim; all 41 titles were rewritten to the `area: summary` issue standard and
assigned a `kind:`. `backlog_service_repo: brookstalley/cordyceps` is now set in
`project-state.yaml`, which is the switch that makes the service the live backlog and retires the
markdown-premise advisory probes.

Two owner decisions changed the corpus rather than just moving it. Two `non_atomic` items were
**split before import** — `TST-5N9X` into `TST-4K9P`/`TST-7B2M`/`TST-3F8W`, and `CQ-9W2F` into
`CQ-6H4N`/`CQ-8M3R`/`CQ-1P7T` — with the originals retained as `dropped` (not deleted) so the split
is auditable. Four altitude/dedup clusters the scrub surfaced were deliberately **deferred** to
native triage rather than merged during migration. Issue #28's unresolved net48/Rhino 7 contribution
offer, which had no backlog counterpart at all, was filed as **#76**.

**Not done:** `.prawduct/backlog.md` was **deleted**, by owner decision, rather than kept as
frozen history behind a banner. The cost is recorded and accepted: `verify-migration` requires
`--from <backlog.md>` and so can never be re-run, which is why it was run to exit 0 *before* the
deletion. Recovery is via git history; `CLAUDE.md` carries the signpost that replaces the banner.
Full rationale, the target/scope decisions, and the plugin build that performed the migration
(3.4.1-dev.2) are in `.prawduct/artifacts/migration-scrub-decisions.md`.

**Follow-up:** `backlog counts` reports `untriaged: 0` on this repo while `backlog list --untriaged`
correctly finds the 11 pre-existing reporter issues, folding them into `shipped` instead. Does not
affect the migration (the gate is the authority, not the count arithmetic) but looks like an
upstream prawduct defect. Filed as **#78**; the upstream `/prawduct:report-bug` is still owed.
The PR's own Critic and PR reviews filed two more: **#77** (`CLAUDE.md` is ~35 lines over the
project-content ceiling) and **#79** (the janitor build plan declares no frontmatter `scope:`).

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
