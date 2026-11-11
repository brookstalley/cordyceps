---
schema: 1
type: governance
scope: records-catchup
date: 2026-11-11
title: mark shipped work released, archive finished plans, trim CLAUDE.md
closes: [brookstalley/cordyceps#79]
---
**Why:** the owner ruled on 2026-11-11 (`.prawduct/owner-rulings.md`) to mark released the 13
change fragments whose work shipped in v1.4.10 through v1.5.0 but never carried a release tag, and to
bring CLAUDE.md under its ~150-line guideline.

**What changed:**
- `prawduct-hook cut-change-log` moved the 13 fragments into `.prawduct/change-log-archive/2026-06.md`
  at the first final release whose tag contains each entry's commit: v1.4.10 (nine), v1.4.11
  (`gh-document-save`), v1.4.12 (`gh-canvas-slider-add`, `gh-script-configure-wires`), and v1.5.0
  (`gitflow-release-refactor`, first carried by v1.5.0-rc.1). Still pending: `param-spec`,
  `testament-onboard`, `pr-merge-preference`, `backlog-service-migration`, `prawduct-hygiene`,
  `strict-inputs`, `canvas-read`, and this one.
- Two plans that shipped in v1.5.0 were archived by `plan-backfill`. `build-plan-reliability.md` had six
  status boxes nobody ticked after PR #26 merged. `build-plan-janitor-2026-07-02.md` had no `scope:`
  frontmatter and no `## Status` section (backlog #79). Both were ticked, with a note that the live
  checks VRF-009 and VRF-010 stay pending in the operator queue.
- CLAUDE.md drops from 204 to under 150 lines: the component and tool inventory moved to
  `docs/architecture.md`, and the binding rules it carried stay in CLAUDE.md as a short list.
- `.prawduct/owner-rulings.md` is new and records the 2026-11-11 rulings.

## Consumer note
none: governance records only; no product behaviour changed.
