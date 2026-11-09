---
schema: 1
type: chore
scope: gate-soundness
date: 2026-06-20
title: Wire .NET/xUnit test evidence into the Prawduct gate (TST-9Q4M)
---
**Why:** `prawduct-hook test-evidence record` defaulted to pytest and could not run this
C#/xUnit repo, so no `.test-evidence.json` was ever produced and the freshness/Critic/PR
gates were unsound (every code chunk warned "no test evidence"). Added the
`JunitXml.TestLogger` package to `Cordyceps.Tests` and declared `test_command` in
project-state.yaml so the hook runs the real xUnit suite and records exact counts.
Verified end-to-end: `test-evidence record` → 53 passed / 0 failed @ HEAD; `test-status`
→ current. No user-facing change (dev tooling), so no root CHANGELOG entry.

Legacy tags: chunks=01; status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
