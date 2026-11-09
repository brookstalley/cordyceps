---
schema: 1
type: governance
scope: prawduct-hygiene
date: 2026-11-09
title: move the change record onto fragments and the learnings into .claude/rules
---
**Why:** three Prawduct advisories were open against this repo: the single-file change log was
still holding release-pending entries, the learnings were still in `.prawduct/learnings.md`, and
`.gitignore` did not cover the plugin's session files.

**What changed:**
- The release-pending entries in `.prawduct/change-log.md` moved verbatim onto one fragment per
  scope under `.prawduct/changes/`. The log keeps its header comment and archive pointer. Three
  entries that sat under another entry's header in the old log (`gh-script-configure-wires`,
  `gh-document-save`, `solidity-hardening`) are now fragments of their own, titled from their own
  bodies. The two `gh-script-language` entries share one fragment. Each migrated fragment carries a
  `none:` consumer note, because the old log held no consumer notes. With one file per scope,
  `.gitattributes` needs no `merge=union` line for the change record.
- `.prawduct/learnings.md` moved to `.claude/rules/learnings/core.md`. All 15 entries arrived
  unchanged, under the new file's preamble.
- `.gitignore` now covers five Prawduct session files (`.delegate-brief.md`, `.test-report.xml`
  and its `.scope.json`, and the two review-dispatch markers).

No plugin code changed, so `CHANGELOG.md` is untouched.

## Consumer note
none: repo-internal governance bookkeeping; nothing changes for plugin users
