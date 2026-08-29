# Migration scrub decisions — markdown backlog → GitHub Issues

Owner-confirmed decisions for the one-time cutover of `.prawduct/backlog.md` onto
GitHub Issues via the prawduct backlog service (`prawduct-hook backlog <op>`).
Recorded per the migration-scrub runbook (MG4), which requires the target, the
archive-scope choice with its accepted cost, and the plugin build that ran it to
be auditable outside a transcript.

**Date:** 2026-08-29
**Operator:** Brooks Talley (brooks@tangentry.com)
**Branch:** `chore/backlog-migrate-github-issues`

## Environment (runbook precondition — mandatory)

| | |
|---|---|
| `prawduct-hook` version | **3.4.1-dev.2** |
| `--plugin-dir` | `/Users/brookstalley/source/prawduct/plugin` |
| hook resolved from | `/Users/brookstalley/source/prawduct/plugin/bin/prawduct-hook` |

Recorded because a migration later found incomplete cannot be diagnosed without
knowing which build performed it (the `samsung-frame-art-loader` precedent: 7 of 9
items stranded, cutover already recorded, no build reference anywhere in the repo).

## Step 0 — target repo

**Target: `brookstalley/cordyceps`** — owner-confirmed 2026-08-29.

Not inferred from the git remote. The product migrates into its own repo; the
repo already carries 11 issues and 22 PRs, so the target was **not** empty before
the import and `counts` arithmetic must subtract `untriaged` before comparing.

## Step 1b — id validity

Clean. All 41 items carry well-formed `[PFX-XXXX]` markers; both runbook greps
returned zero hits. No strikethrough items, no legacy metadata-less items, no
renames needed before import.

## Corpus

41 items — 19 open, 22 closed (20 shipped, 2 dropped... see the split below).
This is **not** the pre-scrub count of 35; the non-atomic split below is what
changes it.

## Decision (a) — non-atomic split, applied BEFORE import

Two items were flagged `non_atomic` by the scrub and split by owner decision,
accepting that this breaks one-to-one mapping with the pre-scrub backlog:

| original | split into |
|---|---|
| `TST-5N9X` Dependency/toolchain refresh | `TST-4K9P` test-stack bump · `TST-7B2M` stale csproj comments · `TST-3F8W` SDK pin policy |
| `CQ-9W2F` Repo structure cosmetics | `CQ-6H4N` Tools/Unified flatten · `CQ-8M3R` Knowledge/Prompts naming · `CQ-1P7T` the three dedup follow-ups |

**The originals are retained as `status: dropped`, not deleted**, and migrate as
closed issues. The runbook forbids removing a source item with no corresponding
closed issue (a silent drop); recording them as dropped is what makes the split
auditable. Both keep their bodies **verbatim** plus a split note.

Two deliberate stage deviations from the originals, flagged in the item bodies
rather than applied silently:

- `TST-3F8W` → `stage: requirements` (original: `ready`). It is a decision to
  record, not a change to make.
- `CQ-1P7T` → `stage: ready` (original: `requirements`). Follow-ups (a)-(c) each
  arrive with a concrete fix shape.

**Nothing was dropped in the carve.** `CQ-9W2F` carried a *fourth* thread — the
`releases/Cordyceps.gha` tracked-binary bullet, **RESOLVED 2026-08-25** on
`fix/release-artifact-provenance` (commit `94572ed`). It is completed work, so no
live item was minted for it; its full record, including the "history still carries
the 56 old blobs (~27.4 MiB)" caveat, survives verbatim in the archived original.

## Decision — deferred altitude/dedup clusters

The scrub surfaced four shared-root-cause candidates. The owner **DEFERRED all
four**: they migrate as separate items and get triaged natively on GitHub at
step 7 or later. Deliberately cross-linked via `related:`, deliberately NOT merged.

1. `REL-6H4X` + `REL-4K7D` — pre-release command / regression guard
2. `GHS-5B7R` + `GHS-6QN4` — both descoped from issue #33; set/configure returns
   success on a script that will not run
3. `MCP-7D2N` + `MCP-4T8V` — server-state modeling gaps
4. `GHC-2N8K` ⊂ `CQ-1P7T` — a fourth instance of the same
   extract-the-duplicated-helper pattern

No stale items were proposed for drop: the oldest open item was 66 days old, and
nothing crossed the 90-day bar.

## Decision (b) — pre-existing reporter issues #14 / #15

Two archived items duplicate real reporter issues: `GHS-4D8M` carries
`refs: issue #15` and `GHD-8P4N` carries `refs: issue #14`.

**Resolution: comment-only cross-reference.** Both migrate as their own closed
issues; a comment on each new issue points at the reporter issue.

**Rejected: hand-adding `id:PFX` labels to #14/#15.** This deadlocks, and the
reason is a split-brain between two lookups verified in the plugin source:

- `import` keys idempotency on the **`id:PFX` label** —
  `migrate._find_by_key` → `list_issues(labels=[key_label])` (`migrate.py:1185`, `1232`)
- `verify-migration` keys coverage on the **body block `id_aliases`** —
  `core.iter_alias_issues` (`core.py:1112`)

So a hand-added label makes the import **skip the create**, leaving the issue with
no `prawduct` body block; the gate then reports the item as `missing` and exits 4;
and the prescribed remedy ("re-run the import") skips again on the same label — a
permanent no-op loop. The only escape would be hand-writing a `prawduct` block,
which the backlog skill forbids (adapter-owned; a hand-written one is merged away)
and which `update --body` cannot do (it re-appends the original block).

**Also rejected: `merge --into` redirect.** Unverified whether the adapter accepts
an `--into` target that is untriaged (no prawduct block or labels). Not worth the
risk for a cosmetic redirect.

## Decision (c) — archive scope: `all`

**`--archive-scope all`** — the full archive imports as closed issues.

Cost accepted, stated plainly:

- **Every archived item is two writes**, not one: a create, then a status
  reconcile to closed (the create path carries no initial-state field). Both are
  metered against the 900-points/minute window.
- 41 creates + 22 closes ≈ 63 metered writes. Sized on **wall clock** (serial `gh`
  round-trip latency), not on rate-limit risk.
- The archive is **reachable, not visible by default**: `list` defaults to
  `state=open`, and so does add-time dedup. Seeing archived items takes an explicit
  `--state closed` / `--state all`.
- Two redundant closed issues are minted alongside reporter issues #14/#15
  (see decision (b)).

`open` was considered and rejected: the owner intends to **delete**
`.prawduct/backlog.md` after cutover, which is only coherent under `all`. Under
`open` the 22 skipped items would exist in neither the tracker nor a live file —
recoverable only by someone who knew to run `git show`.

Public visibility was raised as an argument for `open` and explicitly dismissed by
the owner: "We develop transparently and publicly." Bodies migrate **verbatim**,
no redaction pass.

## Decision — delete `.prawduct/backlog.md` after cutover

**Confirmed.** No frozen-history banner, no stub file.

What this costs, verified against the plugin source before the decision was applied:

- **`verify-migration` becomes permanently unrunnable** — it requires
  `--from <backlog.md>`. The completeness gate can never be re-run. It was
  therefore run to **exit 0 before** the deletion, and that is the one ordering
  constraint the deletion is subject to.
- `import` also becomes unrunnable. This is desirable rather than costly: the
  runbook forbids re-running it after dispositions are applied (it would reopen
  every disposed item), and the `open`-scope backfill path that needs the file
  does not apply under `all`.
- Recovery is `git show <cutover-commit>:.prawduct/backlog.md` — real, but not
  discoverable. The MG2 export (`backlog export --to <dir>`) dumps the **target
  repo**, is independent of the file, and under `all` contains all 41 items.

**No code path breaks.** Every reader is either gated on `backlog_service_repo`
(`briefing.py:982` sits after the post-cutover branch and already guards
`is_file()`; `release_readiness._markdown_backlog_unavailable_reason`;
`backlog_probes.post_cutover` at lines 149/195/231/280; `norm_probes:827`) or
degrades non-raising (`norm_probes._read_text` catches `OSError`). `dead-why` and
`stalled-transition` switch to the backlog cache rather than retiring.
`lib/backlog/legacy.py` is the shared plugin's parser and is untouched by one
repo's cutover.

The signpost the banner would have provided is replaced by a note in `CLAUDE.md`
(decision (c)), plus a fix to the stale citation at `reflections.md:557`.

## Decision (d) — issue #28 (Rhino 7 / net48)

Filed **natively after cutover**, not part of the import: `stage: requirements`,
`kind: spike`, `area: build`, framed as accept / decline / defer the contributor's
net48 offer. It is the only closed reporter issue with no backlog counterpart, and
it records an outstanding *offer of work* plus a source audit — unresolved intent,
not shipped work.

## Restructure plan (MG6 / issue-standard §5)

Plan applied at create: `plan-all.json`, 41 entries — a `title` and a `kind` for
every item. Bodies were **not** restructured (`sections` omitted) because the owner
chose verbatim migration.

All 41 titles were rewritten to the `area: summary` shape. The raw corpus had 16
titles over the 72-char budget; `normalize_title` prepends the `area:` prefix,
which pushes **5 more** over (`GHD-5R7Q` 70→91, `GHC-7X4B` 68→79, `MCP-3D8V`
62→74, `RSC-2H9K` 61→74, `TST-6W7H` 64→73), so 21 rewrites were mandatory and all
41 were authored rather than leaving any to chance.

`restructure-preview` reported **`preflight_blocking: 0`** before the run — the
signal that the import would not refuse. The 205 lint findings are advisory and
expected: 177 `missing-section` (the direct consequence of verbatim bodies), 15
`body-too-long` (prawduct bodies are long-form analysis), 13 `bug-missing-env`.
Verbatim bodies and a clean body-lint are mutually exclusive; the owner chose
verbatim.

Kind distribution: bug 13 · task 15 · chore 7 · feature 4 · spike 2.
