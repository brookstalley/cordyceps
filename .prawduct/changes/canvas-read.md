---
schema: 1
type: feature
scope: canvas-read
date: 2026-11-09
title: read the canvas cheaply, name things the way the canvas does
closes: [brookstalley/cordyceps#104, brookstalley/cordyceps#106]
---
**Why:** orienting on a definition took `gh_canvas list` + `gh_wire list` + `group_list`, a large
response that still lacked values and counts. Seeing a data tree's shape meant
pulling its data. And every object id had to be a full GUID, while the canvas shows nicknames. The
Hyperceps fork (Yibin Yang, MIT) had built an outline, a tree summary and object names; a read of
it against Cordyceps found defects in each (counts printed as current when the solver was off,
overlapping groups dropped, problems that disagreed with `status`) and in Cordyceps itself
(`list group=` took the first substring match; member arrays skipped unknown ids silently). The owner ruled on 2026-11-09: names are strict and breaking with no
lenient mode, `tree` and `outline` are actions not tools, and the Hyperceps credit stays accurate.

**What changed:** a host-free canvas model under `Core/Canvas/`. `CanvasReader` is the one
Grasshopper adapter; it reads the document once per call on the UI thread into a `CanvasSnapshot`.
`ObjectResolver` is the one name rule (full GUID; 8-35 hex/hyphen GUID prefix with 8+ hex digits;
unique exact case-sensitive nickname; group name in the Groups scope), with `DisplayNames` choosing
the names outline prints, each checked to resolve back. `ObjectHealth` is the status rule, now
shared by `gh_inspect status` and outline. `ToolHelpers` routes every object and group id on
`gh_canvas`, `gh_wire`, `gh_script` and `gh_inspect` through the resolver; `Core/IdBatch` resolves
many-id calls before the first mutation. New `gh_canvas(action='outline')` (`OutlineRequest`,
`OutlineView`) and `gh_inspect(action='tree')` (`Core/DataTrees/`). New guide
`gh://docs/canvas-outline`. The outline, the tree summary and the name rule are adapted from
Hyperceps with header attributions; README credits updated, `LICENSE` unchanged.

**Decisions:** names are scoped and ids are not: an object action matches nicknames of non-group
objects only, and a group action matches group names only. A group's GUID or prefix reaches the
actions that act on groups (`delete`, `move`, `rename`, `info`, `bounds`, `zoom`, member arrays,
`outline id=`); every other object action refuses a group.
Default nicknames resolve when unique on every action except those that remove something
(`delete`, `group_delete`, `gh_wire disconnect` and `clear`), which refuse a default nickname and
the default group name `Group` even when unique; the error prints the id to pass and the rename
call that makes a name work (owner ruling, second set). Error
payloads are text only, with candidates inline. Member arrays are all-or-nothing; `delete`, `move`,
`preview`, `enable` and bulk `connect` keep per-entry results; a repeated object is acted on once
in `delete`, `preview` and `enable` and is a failed entry in `move`. Responses that echo an id echo the
resolved GUID. Tree's `object` is the nickname as a label and `id` the GUID as the handle. Outline
counts are qualified (`stale`, `disabled`), it states wires it could not read (`unread`), and its
problem list is capped at 10. `status` reads the same objects as outline's problems (components and
parameter objects, by `ObjectHealth.IsSubject`), lists them all under `components`, and no longer
lists phantom objects (#106). Outline lines head `NAME · Type`, and a nickname is printed only when
`OutlineGrammar.IsPrintable` says it can be cut back out of a line; otherwise, as for a default
nickname or a group named `Group`, the id is printed (#104). `gh_inspect` `limit` defaults to 0 meaning the action's default.

**Not done here:** batch edits, sweeps, the edit transaction and undo (#91), solve policy, moving
`status`, `trace`, `list`, `group_list` and `gh_wire list` onto the snapshot, stored values on
unwired inputs and group colours in outline.

**Verification:** host-free rules unit-tested (resolver ambiguity matrix and display-name
round-trip, snapshot, health, golden outline outputs, tree summary); Release build clean. The
Grasshopper adapters need a running Rhino and are enqueued as VRF-019, VRF-020 and VRF-021.

## Consumer note
breaking: object ids now also accept an 8+ hex GUID prefix or a unique case-sensitive nickname, and group actions a group's exact name; an ambiguous name acts on nothing and lists the ids. `group_move` and `list group=` no longer match case-insensitively or by substring; member arrays of `group_create`/`group_add`/`group_remove` fail the call on any unresolved id instead of skipping it; `gh_wire list` with an unknown id is an error; several non-GUID error texts changed; responses echo the resolved full GUID; a repeated id in `delete`/`preview`/`enable` is one action with a note; `move` fails a second entry for the same object; actions that work only on components and parameters refuse a group's id (several returned an empty success before); `gh_inspect log limit=0` now returns the default 100 entries; `delete`, `group_delete` and `gh_wire disconnect`/`clear` refuse a default nickname or the group name `Group`; `gh_inspect status` lists parameter objects under `components` and counts them in `summary`; outline lines start `NAME · Type`, and an unprintable nickname is printed as its id. Also: new `gh_canvas(action='outline')` and `gh_inspect(action='tree')`.
