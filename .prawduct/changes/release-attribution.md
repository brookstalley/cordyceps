---
schema: 1
type: chore
scope: release-attribution
date: 2026-06-20
title: Drop attribution trailer from release commits
---
**Why:** `scripts/release.sh` `git_commit_and_tag` hard-coded a `🤖 Generated with …` +
`Co-Authored-By: Claude …` trailer on every `Release vX.Y.Z` commit, contradicting the
project's `Commit attribution: none` preference. Removed both lines so release commits carry a
plain `Release vX.Y.Z` message. Release tooling only; no shipped-plugin change.

Legacy tags: status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
