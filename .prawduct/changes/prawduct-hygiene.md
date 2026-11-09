---
schema: 1
type: governance
scope: prawduct-hygiene
date: 2026-11-09
title: move the change record onto fragments and the learnings into .claude/rules/learnings
---
**Why:** three Prawduct advisories were open against this repo: the single-file change log was
still holding entries with no `release=` tag, the learnings were still in `.prawduct/learnings.md`, and
`.gitignore` did not cover the plugin's session files.

**What changed:**
- The entries in `.prawduct/change-log.md` with no `release=` tag moved verbatim onto one fragment
  per scope under `.prawduct/changes/`. The tooling reads every such fragment as release-pending,
  but only four hold unreleased work: `param-spec`, `testament-onboard`, `pr-merge-preference` and
  `backlog-service-migration`. The other 13 shipped in v1.4.10 through v1.5.0 (the first release
  tag containing each entry's commit) and never got the tag in the old log. Leave those out of the next cut, or cut
  them with their real versions, which is the owner's call. The log keeps its header comment and archive pointer. Three
  entries that sat under another entry's header in the old log (`gh-script-configure-wires`,
  `gh-document-save`, `solidity-hardening`) are now fragments of their own, titled from their own
  bodies. The two `gh-script-language` entries share one fragment. Each migrated fragment carries a
  `none:` consumer note, because the old log held no consumer notes. With one file per scope,
  `.gitattributes` needs no `merge=union` line for the change record.
- `.prawduct/learnings.md` moved to `.claude/rules/learnings/`, and all 15 rules kept their text.
  Ten are in `core.md`, which loads in every session; they sit under an `## Unsorted` heading below
  the plugin's standard preamble. Five rules apply only to certain paths, so they moved to area
  files that load when those paths are read: `release.md` (the `.gha` build-output rule and the
  three-place version rule) and `tests.md` (host-free linked code, disabled test parallelization,
  and concurrency tests that must really exercise concurrency). This keeps `core.md` under its
  12KB cap. The rules have not been compacted to the one-line format the preamble describes. That
  needs `prawduct-hook learnings-compact` and the owner's approval for anything it drops.
- `docs/release-process.md` and `CLAUDE.md` now describe the release-time cut of the fragments
  (`prawduct-hook cut-change-log`) instead of hand-tagging change-log entries with `release=`. The
  header comment in `.prawduct/change-log.md` is replaced with the plugin template's current one,
  which says that a repo using fragments adds nothing to this file.
- `.gitignore` now covers five Prawduct session files (`.delegate-brief.md`, `.test-report.xml`
  and its `.scope.json`, and the two review-dispatch markers).

No plugin code changed, so `CHANGELOG.md` is untouched.

## Consumer note
none: repo-internal governance bookkeeping; nothing changes for plugin users
