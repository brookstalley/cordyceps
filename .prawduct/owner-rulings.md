# Owner Rulings

<!-- The owner's own words, each time they decided something about this product. Copy this file
     to `.prawduct/owner-rulings.md` the first time the owner rules, and commit it with the
     work it governs (methodology/building.md "When the Owner Rules").

     Why it exists: a ruling given in conversation is gone by the next session, and the harness's
     auto-memory is per account, so the next person, or the same person on another account,
     never sees it. This file is the one place a later session looks for what the owner said.

     What goes here: a decision, a correction, or a constraint or preference about the product,
     in the owner's words, with the date. Write it before replying to them, and cite it in the
     reply, so they can see it was taken down as they meant it. Your own inferences do not go
     here; they stay marked as inferred where you made them, until the owner confirms one.

     What does not: the decision's working-out. Each entry says where the decision landed (the
     plan's Goals, a norm in a `## Direction` section or `project-preferences.md`, a design
     artifact, a backlog item) and the governing text lives there. A ruling on the edge of a
     norm still goes in that norm's Rulings field as well; this entry keeps the words that
     settled it.

     A later ruling that reverses an earlier one is a new entry that says which one it replaces.
     Leave the earlier entry in place: what the owner once said, and when it changed, is the
     record.

     When work spans repos, keep the rulings in the repo that owns the decision, and have the
     other repo cite that file rather than copy it.

     Newest first. Plain prose; nothing parses this file. -->

**2026-11-11 (transaction wave, PRs).** "yes please PR and merge once ready" — open the
solve-policy PR, then the edit-transaction PR, each merged (merge commit) once `build-test` passes
and the independent PR review is clean.

**2026-11-11 (transaction wave, later).** On the inferences in both plans, put as two numbered
lists (solve-policy: schedule rather than solve inside the call; `recompute`/`solver`/`revert` wait
up to `timeout` and fail unless the solve completed; no agent cancel. Edit-transaction: per-id
successes commit as one undo step and an unexpected failure rolls back the whole call; the solver
paused during each edit; whole-object undo where no dedicated event exists, with lossy third-party
objects accepted; `delete`/`group_delete`/`clear` schedule one solve and return before it runs;
`solver`, `revert`, `bake` and the view actions not recorded; edits never wait for their solve;
the `undo` field is the Grasshopper undo-list name): "yes all confirmed". Landed in the Goals of
`.prawduct/artifacts/build-plan-solve-policy.md` (Inferences 1-4) and
`.prawduct/artifacts/build-plan-edit-transaction.md` (Inferences 1-6), now owner-confirmed.

**2026-11-11 (transaction wave).** On #91's Windows spike: "yes we can't test on windows, and
cannot block the release. Please get to as high confidence as you can, but we will not block on
that." On agent-callable `gh_document undo`/`redo`: "yes leave undo/redo off for agents for now."
On moving `.prawduct/change-log.md` onto change fragments: "yes move to fragments." Landed in #91
(the Windows acceptance box becomes non-blocking) and in the edit-transaction plan's Goals once
drafted. The fragment move needed no work: c5710df had already moved every pending entry, and
`prawduct-hook migrate-change-log` (dry run, 2026-11-11) reports nothing to do; `change-log.md`
holds shipped history only.

**2026-11-11 (later).** On the script-rebuild plan's inferences: "agreed" — `zoomable` on a
script component rebuilds and reports rather than refusing (#84), and the default-nickname refusal
applies only when a call would actually remove a port, a rename counting as a removal. On
`group_remove`: "agreed" — it stays outside the destructive rule. Replaces the open part of the
#107 entry below. Landed in `.prawduct/artifacts/build-plan-script-rebuild.md` § Goals.

**2026-11-11 (later).** On outline dropping unprintable or shared nicknames: "yes file" — a
`labels` map in the outline response (handle → nickname, only for objects whose nickname was not
printed). Landed as a backlog item.

**2026-11-11 (later).** "PR and merge both" (`chore/records-catchup`, then `feature/script-rebuild`)
and "delete both" (the `fix/configure-rebuild-decision` branch and its worktree, now that its code
is merged).

**2026-11-11.** On #107 (removing a port with a default-nicknamed id): "yes I think that's
destructive." Landed in `.prawduct/artifacts/build-plan-script-rebuild.md` § Goals and #107.
The question as put also named `group_remove`; the agent recommended leaving that one out
(nothing is lost, `group_add` reverses it) and asked the owner to confirm.

**2026-11-11.** On outline printing a nickname such as `Width (m)` as its id: "I think that's
acceptable." The agent raised one concern: the name then appears nowhere in outline. The owner
has not answered that yet.

**2026-11-11.** On tree labelling by nickname while outline prints a short id: "tree vs outline
seems a bit weird. Does this need more thought?" Open; the agent's reading is in the handoff notes.

**2026-11-11.** "yes trim if possible" (CLAUDE.md, which is over the ~150-line guideline).

**2026-11-11.** Release timing: release the next major "after transaction wave", not before it.

**2026-11-11.** "yes mark released and be sure we're on the latest prawduct (should be serving
from ../prawduct-live ) and using changelog fragments." Landed: the 13 shipped-but-untagged
fragments cut to the releases that carried them (v1.4.10, v1.4.11, v1.4.12, v1.5.0); the
prawduct-live checkout was advanced to origin/develop.

**2026-11-11.** On the orphaned `fix/configure-rebuild-decision` branch: "we should take that old
branch, be sure it's still valid and needed, and include it." Landed in
`.prawduct/artifacts/build-plan-script-rebuild.md`.
