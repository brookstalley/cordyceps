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

41 items — 19 open, 22 closed (**19 shipped, 3 dropped**). The three dropped are
`GHD-3K6F`, which was already dropped before the migration, plus the two split
originals `TST-5N9X` and `CQ-9W2F` retained under decision (a).
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
- Recovery reads the file from the deletion commit's **parent** — the deletion
  commit itself does not contain it, so a bare `git show <cutover-commit>:<path>`
  fails. The working form is
  `git show "$(git rev-list -1 HEAD -- .prawduct/backlog.md)^:.prawduct/backlog.md"`
  — real, but not
  discoverable. The MG2 export (`backlog export --to <dir>`) dumps the **target
  repo**, is independent of the file, and under `all` contains all 41 items.

**The unwind is now two parts, not three.** The runbook's reversal is: unset the
scalar, close the migrated issues by `id:` alias, and revert the frozen-history
banner. With the source file deleted there is no banner to revert and no markdown
to restore in place — a reversal would have to recover the file from git history
first (see the recovery command above). This is part of the accepted cost of the
deletion, not a separate decision.

**No code path breaks.** Every reader is either gated on `backlog_service_repo`
(`briefing.py:982` sits after the post-cutover branch and already guards
`is_file()`; `release_readiness._markdown_backlog_unavailable_reason`;
`backlog_probes.post_cutover` at lines 149/195/231/280; `norm_probes:827`) or
degrades non-raising (`norm_probes._read_text` catches `OSError`). `dead-why` and
`stalled-transition` switch to the backlog cache rather than retiring.
`lib/backlog/legacy.py` is the shared plugin's parser and is untouched by one
repo's cutover.

The signpost the banner would have provided is replaced by a note in `CLAUDE.md`.
**No `reflections.md` edit was needed:** that file's `CQ-9W2F` at
line 557 is a backlog *item id*, not a path citation, and it still resolves via the
`id:PFX` alias. The repo-wide sweep found zero dangling `.prawduct/backlog.md` path
references, so the deletion broke no live citation.

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

## PFX → issue mapping (the in-tree completeness baseline)

`verify-migration` requires `--from <backlog.md>` and that file is deleted, so the gate
that certified this migration can never be re-run. This table is what replaces it: the
full mapping as it stood at cutover, so completeness is checkable from the repo alone
rather than being a prose claim. It is a snapshot of cutover state, not a live view —
statuses move on GitHub from here, and the tracker is the authority when they disagree.

Regenerate the current equivalent with:

```bash
gh issue list --repo brookstalley/cordyceps --state all --limit 200 \
  --json number,labels,title,state,stateReason
```

| PFX id | issue | status at cutover | title |
|---|---|---|---|
| CQ-1P7T | #44 | open | code-quality: finish the three ToolHelpers dedup follow-ups |
| CQ-2X8B | #71 | shipped | code-quality: consolidate tool-class duplication |
| CQ-5J9N | #72 | shipped | code-quality: broad-catch and silent-swallow sweep |
| CQ-6H4N | #42 | open | code-quality: flatten the vestigial Tools/Unified subdirectory |
| CQ-7T4P | #67 | shipped | code-quality: gh_inspect returns success with empty params |
| CQ-8M3R | #43 | open | code-quality: rename Knowledge/Prompts to avoid Prompts/ clash |
| CQ-9W2F | #55 | dropped | code-quality: repo structure cosmetics (split into three items) |
| DOC-4Q7N | #36 | open | documentation: author api-contract.md for the MCP tool surface |
| DOC-8M3T | #69 | shipped | documentation: GetServerInstructions() omits 11 live actions |
| GHC-2N8K | #35 | open | gh-canvas: share one slider-apply path between add and config |
| GHC-6P2M | #48 | open | code-quality: unify parameter resolution by name-or-index |
| GHC-7X4B | #63 | shipped | gh-canvas: add silently drops slider min/max/value/decimals |
| GHC-8V3T | #61 | shipped | gh-canvas: annotate via groups instead of renaming components |
| GHD-3K6F | #73 | dropped | gh-document: finish or formally cut undo/redo |
| GHD-5R7Q | #46 | open | document-resolution: tools retarget on canvas tab switch |
| GHD-6M2J | #59 | shipped | gh-document: snapshot store is unbounded for the process life |
| GHD-8P4N | #65 | shipped | gh-document: save cannot overwrite an existing .gh file |
| GHS-3W9N | #64 | shipped | gh-script: configure destroys all wires instead of preserving |
| GHS-4D8M | #62 | shipped | gh-script: set/configure wipes LanguageSpec and returns success |
| GHS-5B7R | #52 | open | gh-script: report compile diagnostics from set and configure |
| GHS-6QN4 | #53 | open | gh-script: warn when RunScript is defined but never invoked |
| GHS-7K2P | #75 | shipped | gh-script: set drops the script component language directive |
| GHS-9K3T | #49 | open | code-quality: gh_script info omits modifiers and optional flags |
| MCP-3D8V | #57 | shipped | mcp-server: Stop() drain runs on the UI thread it waits for |
| MCP-4R2K | #68 | shipped | mcp-server: honor the MCP error contract at the boundary |
| MCP-4T8V | #47 | open | liveness: SolverState holds one snapshot slot for many servers |
| MCP-5T7W | #58 | shipped | mcp-server: define DrainWithin timeout-coincident-with-fault |
| MCP-7D2N | #45 | open | mcp-server: guard Start() after Dispose() via ServerState |
| MCP-9F3Q | #56 | shipped | mcp-server: add a ServerState enum as lifecycle source of truth |
| REL-4K7D | #51 | open | release-tooling: release.sh has no regression guard |
| REL-6H4X | #50 | open | release-tooling: release.sh has no pre-release command |
| RSC-2H9K | #66 | shipped | rhino-scene: native place-raster-image / PictureFrame action |
| RSC-6K1W | #60 | shipped | rhino-scene: wrap multi-step mutations in undo records |
| TST-2R5H | #37 | open | tooling: scripted MCP smoke-test harness for live Rhino |
| TST-3F8W | #41 | open | tooling: decide and document the oldest-8.x SDK pin policy |
| TST-4K9P | #39 | open | tooling: batch-bump the xUnit test stack within the v2 line |
| TST-5N9X | #54 | dropped | tooling: dependency/toolchain refresh (split into three items) |
| TST-6W7H | #70 | shipped | tooling: link RequestValidator and helpers into the test project |
| TST-7B2M | #40 | open | tooling: fix two stale csproj comments (net7, Rhino 8.21+) |
| TST-8B3D | #38 | open | tooling: burn down the operator-verification queue VRF-001..007 |
| TST-9Q4M | #74 | shipped | tooling: wire .NET/xUnit test evidence into the Prawduct gate |

41 items. 19 open · 19 shipped · 3 dropped (`GHD-3K6F` plus the two split originals).
