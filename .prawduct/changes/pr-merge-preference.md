---
schema: 1
type: chore
scope: pr-merge-preference
date: 2026-08-29
title: merge feature PRs automatically once CI and review are clean
---
**Why:** Owner asked for it while merging #80. `PR merge: wait_for_user` meant every feature PR
stopped for a second ask after CI was already green and the independent review had already
returned zero blocking findings — the ask carried no new information, so it was a round-trip that
decided nothing.

**What changed:** `PR merge` in `.prawduct/artifacts/project-preferences.md` is now `automatic`.
`/prawduct:pr` merges a feature->`develop` PR once CI passes and the review is blocking-free.

**Scope limit:** unchanged for the release surface. The `develop`->`main` promotion is not a
`/prawduct:pr` merge at all (the skill's release-promotion guard hands it back), so it stays
manual and owner-run via `scripts/release.sh`. `PR creation` also stays `wait_for_user` — PRs are
still only opened when asked; only the merge step lost its second ask. A blocking finding still
stops the merge.

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
