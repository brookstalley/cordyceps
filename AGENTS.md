<!-- PRAWDUCT:ANCHOR — governance pointer managed by the prawduct plugin; keep it small and version-free. -->

## Governance (Prawduct)

This repo is governed by **Prawduct**, through your agent host's integration.

**Check first: is it loaded?** If not, or if you can't tell,
**governance is OFF** — no Stop gate, no Critic, nothing below enforced. Tell
the user before any work; don't proceed as if governed or imitate its gates from
its files.

- **Claude Code:** loaded if its session banner appeared; if not, run
  `claude plugin install prawduct@prawduct` and restart.

**Then, before writing any code, STOP and read the build cycle**
(Claude Code: `/prawduct:methodology building`). Skipping it is the #1 governance
failure.

Hardest rules:

- **Tests are contracts** — fix the code, never weaken a test.
- **No "pre-existing" exception** — fix what you find, or flag why you can't.
  The fix half is bounded to BLOCKING; below it, the flag is the whole answer.
- **Never silently drop a requirement** — say so explicitly.
- **Run the Critic after medium+ work** — never write findings yourself; the
  independence is the value. Rigor is stage-keyed: a mid-build review blocks
  only on what would ship broken, the review at the merge boundary runs
  everything and is never skipped, and unsure defaults to the cheaper mid-build
  review.

**Enforcement is structural — while the integration is loaded:** its Stop hook
**blocks** at session end if code changed against an active build plan with no
Critic findings.
