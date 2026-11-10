---
artifact: build-plan
version: 1
scope: canvas-read
branch: feat/canvas-read
depends_on: []
governed_by: [.prawduct/artifacts/project-preferences.md, .prawduct/artifacts/boundary-patterns.md]
partition: coordinator prep commit first; then 01 and 03 delegated in parallel; then 02 and 04 delegated in parallel (04 also waits for 03); isolated worktrees with disjoint files per the ownership lists; the coordinator owns 05 (CHANGELOG, server instructions, guides, README, CLAUDE.md, verification queue, fragment) and the combined run
last_validated: 2026-11-09
---

# Build Plan — read the canvas cheaply, name things the way the canvas does

Branch: `feat/canvas-read` (off `develop` at 975912d). One PR.
Critic mode: per-chunk reviews (inferred for a five-chunk plan), with Chunk 01 overridden to
`final` because 02 and 04 build on it; Chunk 05 is `cumulative-final`.

## Advisory: how this plan departs from the first sketch, and what I would cut

- **The resolver moves into Chunk 01.** Outline must look up `group=` and `id=` with the same rule as
  every other tool (read-probe report 1.6 defect 5). If the resolver lived in 02, 04 could not start
  until 02 finished, so 02 and 04 would not really run in parallel. 01 now delivers the snapshot,
  its reader and the host-free resolver. 02 is the wiring: 29 choke-point call sites, 9 direct
  sites, help text.
- **A prep commit before any delegate starts.** 02 and 04 would both edit
  `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`: the action pipe-list on line 321, the parameter
  list at 322-363, the dispatch at 448-473 and the ToolInfo dictionary. Each of 01-04 also adds
  link lines to the one `ItemGroup` in `src/Cordyceps.Tests/Cordyceps.Tests.csproj` (lines 24-57).
  The coordinator commits both before dispatch, so the delegates edit disjoint lines (see "Prep").
- **03 owns every line of `src/Cordyceps/Tools/Unified/GhInspectTool.cs` except the body of
  `ActionStatus`.** That includes the `id` parameter description, which would otherwise be 02's,
  because 03 changes the lines next to it (adds `ids`, `param`, `side`, changes `limit`).
- **One status rule, not two.** Outline has to report DISCONNECTED (report 1.6 defect 3). Writing a
  second copy of the rule in `GhInspectTool.cs:212-239` would let outline's `problems` drift away
  from `status`. 01 makes the rule host-free (`ObjectHealth`) and 04 points `ActionStatus` at it.
  This changes only how `status` classifies each object. It does not move `status` onto the
  snapshot, so the "no migration this wave" inference still holds.
- **A scope I would cut if 04 runs long:** the `id=` "around" view. Overview and group detail cover
  canvases that have groups. The networks fallback covers canvases without groups. "Around" can
  become 04b without changing anything else.

## Goals

- **Response taken:** proceed. Owner rulings, 2026-11-09 (not reopened here): (1) names are
  strict, breaking changes are allowed, there is no lenient mode, and nothing is guessed: an
  ambiguous or unknown name is an error that lists the candidates. (2) Few tools, many actions:
  `tree` and `outline` are actions, not tools. (3) Verdicts: tree is adopted with small fixes;
  outline is rebuilt on a shared canvas model; object names come from one host-free resolver over
  that model (GUID, GUID prefix of 8+ hex, unique nickname, group name), wired through the existing
  choke points. Batch refs are not part of this wave. (4) Hyperceps credit stays accurate.
  (5) Out of scope: edit transaction and undo (#91), solve policy and bounded solve, batch, sweep,
  the control-value helper, agent guidance and benchmark, and Lichen/decorators.
- **Product:** an agent can orient on a definition in one call, at about a tenth of today's tokens.
  Today orienting takes `list` + `gh_wire list` + `group_list` (about 50 KB for a 100-object
  canvas), and that still lacks values and counts (read-probe report 1.1). The agent can also see a
  data tree's shape without pulling data. It can name objects the way the canvas shows them: an id,
  a short id, a unique nickname, or a group name. Every name the outline prints works as an id in
  every tool that takes that kind of thing (object names on object actions, group names on group actions).
- **Architecture:** a host-free canvas snapshot under `src/Cordyceps/Core/Canvas/`, read from the
  document once per call on the UI thread by one adapter. The rules, the views and their tests are
  all host-free. Later waves must be able to (a) migrate `status`, `trace`, `list`, `group_list`
  and `gh_wire list` onto the snapshot as projections, and (b) let batch overlay pending creates,
  deletes and renames without changing the resolver's rules. (b) is met by having the resolver
  read the snapshot through a narrow index interface (see C1), which batch can decorate.
- **Constraints:** all document reads go through `ExecuteOnUiThread`, after a pre-marshal liveness
  check (`SolverState.Shared.Derive().Ui == Blocked` → `StatusEnvelope.BusyResult`), the same way
  `status` does it (`GhInspectTool.cs:187-189`). Every new host-free file is linked explicitly into
  the test project; the explicit per-file list is kept and no wildcard is added (cross-cutting
  report 3, "potential regressions"). Every machine-facing number and value is formatted with the
  invariant culture (learnings: `int.TryParse`). Validate, then mutate: every id in a call is
  resolved against one snapshot before the first mutation (learnings: validate-then-mutate).
- **Tradeoffs accepted:**
  - Error payloads are text only. Candidates and ids are inline in the message, with no structured
    `code`/`candidates` fields as `add` has; adding them would change the `out string error`
    plumbing at 29 sites. Revisit when batch needs machine-readable resolution errors (backlog).
  - Outline shows no stored values on unwired inputs. Reading them needs reflection on
    `PersistentData` (report 1.2 "Stored values", unverified for all types), and no typed
    `IGH_PersistentParam` member appears in the SDK XML (only `GH_PersistentParam<T>.PersistentDataCount`).
  - Group colour is not printed on outline lines (report 1.3; `group_list` still reports it).
- **Inferences** (made on the owner's behalf, not owner rulings):
  - *Inferred:* names are scoped, ids are not. A full GUID or a GUID prefix names exactly one
    document object whatever its kind, so object-taking actions keep accepting group GUIDs as they
    do today (`delete`, `move`, `rename`, `info`, `bounds` and `zoom` all work on a group's GUID
    through the choke point; `list` prints groups). A *name* resolves only in its own scope. On an
    object action, a nickname matches only non-group objects. On a group action, a name matches
    only group names, and a GUID or prefix must land on a group. There is no cross-scope fallback:
    Hyperceps' `delete id=PAIRS` deleting a group is the silent scope change in batch-contract
    report 3. Two places genuinely take either kind. The member arrays of
    `group_create`/`group_add`/`group_remove` (groups nest by GUID) and outline `id=` (a group's id
    shows that group) use the Objects scope, which reaches groups by id only.
  - *Inferred:* nickname and group-name matching is exact, ordinal and case-sensitive, and the input
    is not trimmed. A near miss gets a "Did you mean" suggestion, never a match. `find`, `search`
    and `list typeFilter=` keep their loose matching because they return candidates and select
    nothing (same reasoning as strict-inputs).
  - *Inferred:* default nicknames ("Circle", "Pt") are nicknames and resolve when unique, on
    destructive actions too. Owner ruling 1 covers strict names everywhere, and the ambiguity error
    is safe. This is the one item flagged for the owner below.
  - *Inferred:* moving `status`, `trace`, `list`, `group_list` and `gh_wire list` onto the snapshot
    is not in this wave. The exception is `status`'s per-object classification (see above), because
    outline cannot be honest about DISCONNECTED without sharing it.
  - *Inferred:* when a response echoes the id the caller passed, it now echoes the resolved full
    GUID (D format), so a caller who passed a name gets the handle back. Callers who passed a GUID
    see no change except canonical formatting.
- **Owner's call, flagged:** whether destructive actions (`delete`, `gh_wire clear`, `group_delete`)
  accept a *default* nickname. Built as inferred (yes) and shown to the owner in the PR; the
  alternative, refusing default nicknames on destructive actions only, makes the rule depend on
  the action. Reversing it is one condition in the resolver plus its tests.
- **Release:** several entries are breaking (the Changed list in Chunk 05), so this is a major version under semantic
  versioning. The owner sets the number at release.
- **Level:** High for 01, 02 and 03. Medium for 04: the line grammar is a design judgement, pinned
  by golden tests and shown to the owner in the PR. The runtime facts marked unverified below go
  to the operator queue (05).

## Requirements Confidence

**High** for 01-03: each is either a rule with stated precedence and errors or a lifted component
with stated fixes. **Medium** for 04's wording: positions and identifier rules are fixed here, and
the prose of notes is the delegate's to choose and pin. The coordinator copies the final grammar
into the guide.

Unverified until a live Rhino session (VRF entries in 05): whether an input parameter's
`VolatileData` holds the post-modifier tree (tree `side='input'`); the `Phase` and
`VolatileDataCount` of a locked object; which of `GH_Document.Enabled` and
`GH_Document.EnableSolutions` the canvas solver-lock button toggles (Cordyceps' `solver` action
sets `doc.Enabled`, `GhDocumentTool.cs:373`); whether default-nickname detection by
`proxy.Desc.NickName` matches every installed type; and whether group nesting appears as a group
id inside `GH_Group.ObjectIDs` (the SDK only says "all object IDs contained in this group").

Settled from the SDK XML (`~/.nuget/packages/grasshopper/8.0.23304.9001/lib/net48/Grasshopper.xml`):
`IGH_Structure.Path(int)`, `Branch(int)` and `PathCount`; `IGH_Goo.IsValid`; `IGH_ActiveObject.Phase`
and `Locked`; `IGH_PreviewObject.Hidden`; `IGH_Param.Optional`, `SourceCount`, `VolatileDataCount`,
`DataMapping`, `Simplify` and `Reverse`; `GH_Document.SolutionState` (`GH_ProcessStep.Aborted`
exists); `GH_SolutionPhase.Computed` ("skipped in the next solution unless it expires"), so an
unexpired object stays Computed across later solutions.

## Governing norms

- project-preferences "few tools, many actions": **conforms** (two actions, no tool).
- project-preferences documentation audit: **conforms** (05 owns it; each action chunk owns its
  `ActionInfo` and pipe-list entry).
- project-preferences UI-thread rule: **conforms** (reader called inside `ExecuteOnUiThread` only).
- project-preferences `{success,...}` JSON, catch at boundary: **conforms**.
- boundary-patterns "MCP Tool / Action Contract — evolve additively, never silent renames":
  `[DECISION: breaking changes listed in Chunk 05 ship as CHANGELOG "Breaking" entries, not silently |
  owner ruling 1 (2026-11-09) chose strict names as a breaking change with no lenient mode; the
  norm's why (agents' workflows break) is met by announcing each change and by errors that say how
  to correct the call | owner can veto]`.
- boundary-patterns "Grasshopper host API — verify before relying": **conforms** (SDK settled list
  above; the rest queued live).

## Today's behaviour this plan changes (verified in source)

Non-GUID ids are rejected today, each site with its own text:

| Site | Today, non-GUID text | Today, other |
|---|---|---|
| 29 choke-point calls (`ToolHelpers.TryGetUnprotectedComponent*`, `ToolHelpers.cs:365-415` via `ParseHelpers.TryParseGuid`, `ParseHelpers.cs:27-43`) | `Invalid component ID format` | missing/infra GUID: `Component not found: {guid}` |
| `move` entries (`GhCanvasTool.cs:710-726`) | `Invalid or missing id` | infra GUID: `Protected: required for MCP server` (leaks existence) |
| `gh_wire connect` bulk (`GhWireTool.cs:166-197`) | `Invalid source GUID` / `Invalid target GUID` | infra: `Protected: required for MCP server` (leaks) |
| `gh_wire list` (`GhWireTool.cs:310-311`) | `Invalid componentId format` | unknown GUID: empty list, no error |
| `group_add` id (`GhCanvasTool.Groups.cs:131-138`) | `Invalid group id format: {id}` | |
| member arrays of `group_create`/`group_add`/`group_remove` (`Groups.cs:29,105,182` via `ParseHelpers.TryParseGuidArray`) | `Invalid GUID in array: {s}` | unknown or infra GUIDs **silently skipped**; `group_create` creates the group first (`Groups.cs:43`) |
| `group_move` (`Groups.cs:316-333`) | none: falls to name match, **case-insensitive, NickName or Name, first match wins** | infra GUID: `Protected: required for MCP server. Cannot modify` (leaks) |
| `list group=` (`GhCanvasTool.cs:1217-1235`) | none: **substring, first match wins** | |

Tests pinning these texts: `src/Cordyceps.Tests/ParseHelpersTests.cs` lines 37 and 69 pin
`Invalid component ID format` and `Invalid GUID in array: nope`, but only on `ParseHelpers` itself.
`ParseHelpers` stays unchanged (the Rhino tools still call `TryParseGuidArray`), so those tests stay
as they are and keep passing. `RequestValidatorTests.cs:75,85` pin `RequestValidator` text, and
`RequestValidator` has no production caller (backlog). No test, guide, README row or prompt
matches the other texts. The guides' `Component not found` rows
(`Knowledge/CommonErrorsGuide.md:7`, `Knowledge/BestPracticesGuide.md:65`, `README.md:174`) stay
true for a full GUID.

`ToolHelpers.TryGetComponent` and `TryGetComponentWithDoc` (`ToolHelpers.cs:169-207`) have no
callers. 02 deletes them rather than wiring dead code.

Action names `outline` (gh_canvas) and `tree` (gh_inspect) are free: neither appears in any dispatch
switch (`GhCanvasTool.cs:448-473`, `GhInspectTool.cs:154-167`), `ActionInfo`, guide, README or
CLAUDE.md. Surfaces that must name each new action: the tool's `ActionInfo` and the
`[McpServerTool, Description("… Actions: …")]` pipe-list (action chunk), the dispatch switch,
`McpServer.GetServerInstructions()` tool lines (`McpServer.cs:648,652`), `README.md` tool table
(lines 148, 152), `CLAUDE.md` tool list (GhCanvasTool and GhInspectTool lines), Knowledge guides
(GettingStarted line 14, DataTrees "Rules" and "Debugging", the new outline guide), the prompt
markdown under `src/Cordyceps/Knowledge/Prompts/` (DebugDataMismatch, OptimizeCanvas), and
`ResourceRegistry` for the new guide. `PromptRegistry.cs` names no inspect actions itself (it serves
the prompt markdown).

## Contracts (binding on 01-04; 05 transcribes them into the guides)

### C1. Object-name resolution

`ObjectResolver.Resolve(index, text, scope)` → `Resolved(entry, matchedBy)` or `Failed(message)`.
Scope is `Objects` or `Groups`. `matchedBy` is one of `Guid`, `GuidPrefix`, `Nickname`, `GroupName`;
the enum is open so batch can add `Ref`. Rules, in order:

1. Null, empty or all-whitespace text → `Component ID is required` (today's text).
2. Text shaped as a full GUID (anything `Guid.TryParse` accepts; braces stay accepted, as
   `ParseHelpersTests.cs:26` pins) is read **only** as a GUID. No other reading is tried.
   - Objects scope: a non-infrastructure object with that id resolves; anything else gives
     `Component not found: {guid}` (today's text, the same for infrastructure).
   - Groups scope: a group resolves. A non-group object gives `Object is not a group: {text}`
     (today's text). Missing or infrastructure gives `Group not found: {text}`.
3. Otherwise every applicable reading is collected:
   - **Nickname / group name**: exact, ordinal. Objects scope matches nicknames of non-group,
     non-infrastructure objects, default nicknames included. Groups scope matches group nicknames
     (`GH_Group.NickName`; `group_create` sets Name and NickName to the same text). Empty nicknames
     never match.
   - **GUID prefix**: 8 to 35 characters, every one a hex digit or a hyphen, with at least 8 hex
     digits. It matches case-insensitively against the lowercase D form of every non-infrastructure
     object id, groups included, in both scopes, so a printed short id is valid everywhere. A hyphen
     in the wrong place simply matches nothing.
4. Outcomes:
   - A nickname or group name carried by 2+ entries → `'{text}' is the nickname of N objects: {id}
     {Type}[, …]; name one by its id` (group form: `… the name of N groups …`). Up to 10 candidates
     are listed, then `and K more`. Each candidate id is its display id (C1.6).
   - A prefix matched by 2+ objects → `GUID prefix '{text}' matches N objects: …; give more characters`.
   - Nickname and prefix name different objects → `'{text}' names two objects: nickname → {id}
     {Type}; GUID prefix → {id} {Type}. Use a longer GUID prefix`.
   - Both name the same object, or only one reading exists → resolved.
   - Groups scope, a prefix lands on a non-group → `Object is not a group: {text}`.
   - Nothing → Objects scope: `No object named '{text}'.`; Groups scope: `No group named '{text}'.`.
     Then, in this order and only when it applies:
     - ` Did you mean '{closest}'?` (`Core/Naming/NameSuggestions.Closest` over the scope's names);
     - Objects scope, the text is a group's name: ` '{text}' is a group; group actions take its
       name, other actions its id ({id}).`;
     - Groups scope, the text is an object's nickname: ` '{text}' is an object's nickname, not a
       group.`;
     - hex-only text of 1-7 digits: ` A GUID prefix needs at least 8 hex digits.`
5. Infrastructure (`InfrastructureIds`, the bridge and its neighbours) is absent from every reading,
   so a nickname or prefix of the bridge is "No object named". Nothing leaks.
6. **Display names** (host-free, beside the resolver; used by outline and by error candidate lists):
   - An object's **short id** is the shortest prefix of its D form, at least 8 characters, that no
     other non-infrastructure object shares (adapted from Hyperceps `GuidPrefixIndex`).
   - **DisplayId** is the short id when `Resolve(shortId, Objects)` returns that object, else the
     full GUID (this covers a nickname that equals someone's prefix).
   - An object's **DisplayName** is its nickname when the nickname is not the type default and
     `Resolve(nickname, Objects)` returns it, else DisplayId.
   - A group's **DisplayName** is its nickname when `Resolve(nickname, Groups)` returns it, else its
     DisplayId.
   - Invariant, tested: every DisplayName and DisplayId resolves back to its own entry in its scope.
7. **Many ids in one call** (`ids`, `moves`, connections, member arrays) resolve against one index
   built once, before the first mutation. Two entries that resolve to the same object are reported:
   the later entry carries `note: 'same object as entry i'` and the object is acted on once.
8. The index is read through a narrow interface: lookup by GUID, entries by nickname, entries by
   group name, entries by prefix, infrastructure set. The snapshot implements it; batch's overlay
   later decorates it. The resolver never sees Grasshopper types.

### C2. `gh_canvas(action='outline')`

Parameters: `id` (an object, Objects scope; a group's id shows that group), `group` (one group name
or id, or a JSON array of them, Groups scope), and `depth` (only with `id`; 1-10; default 2).
`id` together with `group` is an error. `depth` without `id`, or outside 1-10, is an error, not a
clamp. A JSON `group` that does not parse is an error. Envelope fields are fixed; values are
compact lines.

**Identifier rule (report 1.6 defects 4 and 5):** the first token of every object line is the
object's DisplayName, and the first token of every group line is the group's DisplayName. Neither
is ever clipped, prefixed or decorated. Port tokens are the port's nickname when
`ParamSpecLookup` resolves that nickname to the same port on that side, else its name. Values,
messages and panel text may be clipped, with `…`, at 30, 100 and 30 characters. A golden test
asserts that every first token and every port token round-trips through the resolver and
`ParamSpecLookup`.

**Separators:** ` · ` between fields; `[A, B]` lists group memberships (all direct groups, in
document order; brackets are used for nothing else); `←` reads "fed from"; `→` reads "feeds";
`(…)` after a source on an input clause names the source's output port, and appears only when the
source has more than one output; `(…)` after an input port holds its modifiers (`graft`, `flatten`,
`simplify`, `reverse`); `(…)` after a NAME in an `out:` or `ends:` entry holds `PORT: COUNT`, or only
`COUNT` when the object has one output.
List fields end `+N more` when capped.

**Counts are qualified (report 1.6 defect 1).** An output count prints as `N` only when the solver
is on and the owner's `Phase` is Computed. It prints `N stale` when either is false, and
`disabled` (no number) when the owner is locked.

**Overview** (no `id`, no `group`) has these fields:
- `success`, `document`
- `summary`: the same eight fields, in this order, every time: `N objects · G groups · E errors ·
  W warnings · D disconnected · L disabled · S not solved · P preview off`. Objects are counted
  once even when they sit in several groups.
- `solver`: one of
  - `on`
  - `off: this document's solver is disabled`
  - `off: Grasshopper's solver is disabled`
  - either of those followed by `; the last solution was aborted`.

  When it is not `on`, add `note: "counts are from the last solution and may be out of date"`.
- `controls`: sliders, toggles and value lists. `NAME Type = VALUE[ (min..max)][ [GROUPS]]`, in
  invariant culture, at most 20.
- `groups` (when the canvas has groups): one line per top-level group, in data-flow order, at most
  60: `GROUP · N objects[ · E errors][ · W warnings][ · D disconnected][ · contains: CHILD (n
  objects), …][ · shares K objects with OTHER, …][ · in ← NAME, …][ · out: NAME (PORT: COUNT) →
  GROUP, …]`.
- `ungrouped`: `N objects · inputs: … · ends: … · feeds: GROUP, …`.
- `networks` and `unconnected` (when the canvas has no groups): weakly connected components, at most
  10, each `network[ k] · N objects · inputs: NAME = VALUE, … · named: … · ends: NAME (PORT: COUNT),
  …`. The count is given for every output of an end, not only the first (report 1.6 defect 6).
- `problems`: at most 10, in the order errors, warnings, disconnected, then left-to-right. Each is
  `NAME Type[ [GROUPS]] error: MSG[ (+K more)]`, `… warning: …`, or `… disconnected: missing PORT,
  …`. The rule is `ObjectHealth`, the one `status` uses, applied to components; parameter objects are
  also listed when they carry an error or warning (they have no inputs, so never DISCONNECTED). All messages at the highest level are counted, not only the first (defect 6).
- `disabled`: the names of locked objects, at most 20.
- `more`: an object with the number left out of each capped field.
- `next`: one sentence on `group=` and `id=`.

**Overlapping groups (report 1.6 defect 2).** Membership is a multimap from object to groups.
Nesting is a multimap from group to parents (a group is a child of every group whose `ObjectIDs`
contains its id). Effective members are found recursively, with a seen-set. Group order uses an
edge set: for each wire u→v, every top-level group of u gets an edge to every top-level group of v
that differs from it. The order is Kahn's algorithm with a priority queue, tie-broken left then
top; groups left in a cycle are appended by position. That costs O((V+E) log V), against the
O(n² log n) in report 1.2. Each `→ GROUP` destination lists every top-level group of the
recipient. A group line's `shares K objects with` explains counts that add up to more than the
summary.

**Group detail** (`group=`): `{success, groups: [{group, id, summary, in: [...], objects: [lines],
out: [...]}], more}`.
- An object line is `NAME Type[ [GROUPS]][ · disabled][ · preview off][ · PORT[ (mods)] ← SOURCE[
  (PORT)][ [GROUPS]], …][ · error: MSG | warning: MSG | disconnected: missing PORT, …]`.
- A child group is one line: `GROUP (group) · N objects · in ← … · out: …`.
- Lines run upstream first, with the same ordering rule as the group order.
- At most 150 object lines across all requested groups, after which `more` gives the count left out.
- A direct member that is also in a child group gets its own line and is counted in the child's line.

**Around** (`id=`): `{success, around: NAME, depth, objects: [lines; the centre prefixed "* "],
fedFrom: [names just past the depth edge], feeds: [...], more}`, at most 60 objects.

**What outline does not claim:** its help and guides say it is for orientation and that it lists
problems with the same rule as `status`, capped at 10. When `more.problems` is set, run `status`
for the full list. Outline never says it replaces `status` (report 1.6 defect 3; Hyperceps'
CHANGELOG line 33). Plugin "wireless" connections are invisible, and the guide says so (report 1.2).

### C3. `gh_inspect(action='tree')`

Parameters:
- Exactly one of `id` (one object) or `ids` (a JSON array of 1-50 entries, each an id string or
  `{"id": "...", "param": "..."}` with no other keys).
- Optional `param`, only with `id`: a port spec, resolved with `ParamSpecLookup` on the chosen side.
- Optional `side`: `output` (default) or `input`. Any other value is an error.
- Optional `limit`: path groups per tree, 1-500, default 20. An explicit value is honoured exactly,
  including 100 (report 2.6 #1). Out of range is an error.

Targets resolve through the `ToolHelpers` choke point, so names work once 02 lands, GUIDs before.
A group is an error (`… is a group; tree reads components and parameters`). A parameter object has
one tree, so `param` and `side` are ignored for it, matching the documented rule for wire specs on
parameter objects (`McpServer.cs`, "A free-floating parameter object …").

Result: `{success, solver?, trees: [...], errors?: ["ids[i]: …"]}`. `success` is false when any
target failed; the trees that resolved are still returned.
- Each tree: `{target, object (DisplayName once 02 lands; until then the GUID), id (full GUID),
  port ("Name", or "NickName (Name)" when they differ), side, type, branches, items, nulls?,
  invalid?, emptyBranches?, depth, perBranch, paths[], morePathGroups?, notes?[], solved?: false,
  message?, note?}`.
- `depth` and `perBranch` are a number, or `{min, max}` when they vary.
- `paths` are runs: `{0;0}..{0;9}: 1 each` or `{0;3}: 4`.
- `items` includes nulls. `invalid` counts non-null goo with `IsValid == false` (report 2.6 #4).
- `solver` appears only when the solver is off, with the same words as C2. `note` on a tree whose
  owner is not Computed states why in those words rather than asking "Is the solver on?".
  `message` is the first message at the highest level ≥ Warning, clipped at 160 characters, with
  `(+K more)`.

Notes, fixed from Hyperceps:
- The shared-leading-index note needs at least 2 branches and depth > 2 (report 2.2, the single
  `{0;0;0}` false positive). Its wording must not contradict `Knowledge/DataTreesGuide.md` rule 2
  ("Use Shift Path over Simplify") and must not claim what Simplify would remove (the port flag's
  `GH_SimplificationMode` is unverified).
- The mixed-depth note likewise leaves out "Simplify may help".
- New, for `side='input'` with no `param` (report 2.6 #3): when two or more inputs of the component
  each have more than one branch and their branch counts differ, add the note `inputs don't line
  up: A has 3 branches, B has 12`. When the counts match but the path lists differ, add a note
  that names both.

## Prep (coordinator, one commit on `feat/canvas-read` before any dispatch)

- Commit this plan.
- `src/Cordyceps.Tests/Cordyceps.Tests.csproj`: four comment anchors inside the linked-files
  `ItemGroup` — `<!-- canvas-read 01 -->`, `<!-- canvas-read 02 -->`, `<!-- canvas-read 03 -->`,
  `<!-- canvas-read 04 -->` — each separated from the next by at least one existing link line, so
  each chunk inserts directly after its own anchor and no two insertions touch adjacent lines.
- `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`: add `outline` to the `[Description]` pipe-list,
  add the `depth` parameter (`int depth = 0`, where 0 means not given; "Wires each way for outline id= (1-10, default 2)"),
  its `BuildParams` entry, the dispatch case `"outline" => ActionOutline(id, group, depth)`, and
  `["outline"] = OutlineActionInfo()` in the ToolInfo dictionary. A method call is used because
  static field initialisers in different partial files have no defined order.
- new `src/Cordyceps/Tools/Unified/GhCanvasTool.Outline.cs`: a stub `ActionOutline` returning
  `ErrorResponse("outline is not built yet")` and a placeholder `OutlineActionInfo()`. 04 owns this
  file from dispatch on.
- Release build only, to confirm the prep compiles.

## Chunks

### Chunk 01: canvas snapshot, its reader, and the one name rule
Type: code (feature). Critic mode: final (keystone: 02 and 04 build on it).

Deliverables:
- [ ] new `src/Cordyceps/Core/Canvas/CanvasSnapshot.cs`: immutable, host-free records.
  - **Snapshot:** document display name; solver flags (document enabled; global solutions enabled;
    last solution aborted).
  - **Nodes:** every active non-group object, excluding infrastructure: `Guid` id; kind (Component,
    Parameter, Slider, Toggle, ValueList, Panel, Other); type name; nickname and whether it is the
    type default; pivot X, Y; locked; preview hidden (null when not a preview object); phase
    (Computed, NotComputed, Failed); highest message level; every Warning and Error message; for
    controls, value text (invariant) and slider range; panel text capped at 200 characters with a
    truncated flag.
  - **Ports**, for inputs and outputs: name, nickname, optional, modifiers, raw `SourceCount`,
    volatile count, and sources as (node id, output index; -1 for a parameter object) with
    infrastructure sources removed.
  - **Groups**, excluding infrastructure: id, nickname, pivot, direct object members, direct child
    groups.
  - **Infrastructure:** the set of infrastructure ids.
  - **Derived:** wires; upstream and downstream; group membership multimap; parent multimap;
    effective members (recursive, cycle-safe); top-level groups.
- [ ] new `src/Cordyceps/Core/Canvas/ObjectResolver.cs`: C1 rules 1-8, the index interface, display
  names, and error texts. Header attribution (design adapted from Hyperceps `BatchCanvas.Resolve`).
- [ ] new `src/Cordyceps/Core/Canvas/GuidPrefixIndex.cs`: adapted from Hyperceps BatchNames.cs,
  with a header attribution.
- [ ] new `src/Cordyceps/Core/Canvas/ObjectHealth.cs`: host-free classification equal to the rule
  in `src/Cordyceps/Tools/Unified/GhInspectTool.cs` lines 212-239: Error level → ERROR; Warning
  level → WARNING; otherwise any input with optional false, no source and no volatile data →
  DISCONNECTED (listing their names); else OK. Remarks are ignored. For a parameter object only the
  ERROR and WARNING levels apply.
- [ ] new `src/Cordyceps/Core/Canvas/CanvasReader.cs`: the one Grasshopper adapter.
  - `Read(doc, CanvasDetail.Names | Full)`. Names fills ids, kinds, nicknames, default flags,
    groups and infrastructure only, for 02's per-call lookups. Full fills everything, for outline.
  - Uses `ToolHelpers.GetActiveObjects` and `GetCordycepsInfrastructureIds`.
  - Must be called on the UI thread, which the doc comment states.
  - Default-nickname detection compares against the proxy's `Desc.NickName` and the object's
    `Name` (report 1.2).
  - Not linked into tests.
- [ ] new `src/Cordyceps.Tests/CanvasSnapshotTests.cs`: membership multimap with sibling overlap;
  nesting with two parents; a group cycle that terminates; effective members; upstream and
  downstream with wires recorded on one end only; top-level groups.
- [ ] new `src/Cordyceps.Tests/ObjectResolverTests.cs`, the ambiguity matrix:
  - **Full GUID:** present; braces; uppercase; absent, which gives exactly
    `Component not found: {guid}`; infrastructure, which gives the same text; a nickname that equals
    another object's GUID text is ignored.
  - **Nicknames:** unique; case-differs, which is not found and suggests the right case; shared by
    2 (both listed with ids and types); shared by 12 (10 listed, then `and 2 more`); a unique
    default nickname resolves; an infrastructure object's nickname is not found with the same text
    as an unknown name; untrimmed `" COUNT"` is not found and suggests `COUNT`.
  - **Prefixes:** 8 hex; uppercase; hyphen in a D position; hyphen misplaced; 7 hex, which gets the
    8-digit hint; shared by 2; shared with an infrastructure object only, which resolves the user
    object.
  - **Hex nicknames:** a hex nickname naming A while its prefix reading names B gives the
    two-readings error; when both readings name the same object it resolves.
  - **Objects scope:** a group's name gives not-found plus the group hint (no fallback); a group's
    GUID and a group's prefix both resolve.
  - **Groups scope:** a unique name; case differs; two groups with one name; a component's GUID
    and a component's prefix both give `Object is not a group`; an object nickname gets the hint;
    an unnamed group is reachable by id only; empty or whitespace text gives
    `Component ID is required`.
  - **Display:** a property test over a generated canvas with shared nicknames, hex nicknames,
    shared prefixes and unnamed groups: every DisplayName and DisplayId round-trips.
  - **Many ids:** duplicates noted; every entry resolved against one index.
- [ ] new `src/Cordyceps.Tests/GuidPrefixIndexTests.cs`: shortest unique, the minimum of 8, neighbours
  on both sides, `StartingWith` case-insensitive.
- [ ] new `src/Cordyceps.Tests/ObjectHealthTests.cs`: each status, warning outranking disconnected,
  optional inputs ignored, an input wired with no data is not disconnected (it has a source).
- [ ] `src/Cordyceps.Tests/Cordyceps.Tests.csproj`: link lines after the `canvas-read 01` anchor
  for every file in the new `src/Cordyceps/Core/Canvas/` folder except the reader, with `Link` values under
  `Canvas`.

What proves it: `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release --filter
"FullyQualifiedName~CanvasSnapshotTests|FullyQualifiedName~ObjectResolverTests|FullyQualifiedName~GuidPrefixIndexTests|FullyQualifiedName~ObjectHealthTests"`
plus `dotnet build src/Cordyceps/Cordyceps.csproj -c Release`. No full suite.

Owns: everything in the new `src/Cordyceps/Core/Canvas/` folder, its four test files, and the 01 anchor block.
Must not touch: any file under `src/Cordyceps/Tools/`, `src/Cordyceps/Core/ToolHelpers.cs`,
`src/Cordyceps/McpServer.cs`, `CHANGELOG.md`, `README.md`, Knowledge guides.
Depends on: Prep.
Live check, queued in 05: the Names and Full reads against a real canvas (default-nickname flags,
nesting, phase of a locked object).
Report back: anything in C1 that turned out unworkable, as a proposed amendment. Do not change the
contract silently.

### Chunk 02: names everywhere — wire the resolver through every id
Type: code (contract). Breaking.

Deliverables:
- [ ] `src/Cordyceps/Core/ToolHelpers.cs`:
  - `TryGetUnprotectedComponent` and `TryGetUnprotectedComponentWithDoc` keep their signatures and
    their exact behaviour for full-GUID text (the same `IsProtectedId` + `FindObject` path, the same
    texts). Any other text builds a Names-level snapshot and resolves under Objects scope.
  - New overloads take a per-call `ObjectLookup` (lazy; it builds the snapshot at the first
    non-GUID id and computes infrastructure once) for loops.
  - New `TryGetUnprotectedGroup` (Groups scope, returns a `GH_Group`).
  - New `TryResolveIdArray` (Objects scope, all-or-nothing, C1.7).
  - The dead `TryGetComponent` and `TryGetComponentWithDoc` are deleted.
- [ ] Group actions use `TryGetUnprotectedGroup`:
  - `src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs` `group_delete` (line 82),
    `group_remove` id (176), `group_rename` (257), `group_color` (282).
  - The `group_add` id (131-138; today `Invalid group id format`).
  - `group_move` (316-333). The case-insensitive, first-match name lookup is removed, and the
    infrastructure "Protected" text becomes `Group not found`.
- [ ] Member arrays of `group_create`, `group_add` and `group_remove` (`src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs` lines 29, 105, 182) use
  `TryResolveIdArray`. Any unresolved entry is an error naming it, and nothing is created or changed;
  today unknown and infrastructure ids are silently skipped and `group_create` creates the group
  first. In `group_remove`, an entry that resolves but is not a member is listed in a new
  `notMembers` field.
- [ ] `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`:
  - `move` entries (710-726): every entry resolved before any move. Infrastructure becomes
    not-found (no "Protected" text).
  - `delete` `ids` (622), and `preview` and `enable` `ids` (`src/Cordyceps/Tools/Unified/GhCanvasTool.Values.cs`
    213, 264): one `ObjectLookup` per call, every id resolved before the first mutation (for delete,
    this means before `RemoveObject`; the per-id result shape is kept).
  - `list group=` (`FindGroup`, 1217-1235) uses the Groups scope. The substring, first-match rule is
    removed (the site Hyperceps missed, cross-cutting report 1).
- [ ] `src/Cordyceps/Tools/Unified/GhWireTool.cs`: bulk connect source and target (166-197) resolve
  through one lookup; infrastructure is not found, and failures read `Source: …` or `Target: …` with
  the resolver text. `gh_wire list` (310) resolves its filter, so an unknown id is now an error, not
  an empty list.
- [ ] Response echoes: where a success response echoes the caller's id (`rename`, `bounds`,
  `group_delete` `deleted`, `group_color` `id`, `delete` `deleted`, `deletedIds` and per-entry `id`),
  it echoes the resolved full GUID. Failure entries keep the caller's text.
- [ ] Help: `[Description]` texts for `id`, `ids`, `group`, `moves` (gh_canvas); `sourceId`,
  `targetId`, `id`, `componentId`, `connections` (gh_wire); `id` (gh_script). The wording: "Object:
  GUID, 8+ hex GUID prefix, or unique nickname". gh_canvas `id` also serves the group_* actions, so
  its text adds "on group_* actions: a group name or id". Kept: delete takes its partial per-id
  results (the `ActionDelete` pattern) while member arrays are all-or-nothing, because a group
  half-built from a typo is a mutation the caller did not ask for.
  `ActionInfo` tips on `delete` (names allowed; an ambiguous name deletes nothing and lists the
  candidates), on every `group_*` action and on `list` (`group=` exact), plus one Notes line per
  tool stating the rule.
- [ ] Host-free logic introduced here goes in the new `src/Cordyceps/Core/Canvas/` folder only through a request to
  the coordinator (see "Must not touch"). If a new pure helper is needed (for example the
  per-entry duplicate note), put it in new `src/Cordyceps/Core/IdBatch.cs` with
  new `src/Cordyceps.Tests/IdBatchTests.cs`, linked after the `canvas-read 02` anchor.

What proves it: `dotnet test … --filter "FullyQualifiedName~ObjectResolverTests|FullyQualifiedName~ParseHelpersTests|FullyQualifiedName~IdBatchTests"`
(the last only if created) plus a Release build. The wiring is host code: the per-site behaviour
above is listed as numbered live checks in the chunk report, and 05 turns them into a VRF entry.
Run a consumer trace by behaviour, not only by helper callers (learnings: "tracing consumers …
misses paths that bypass it"): grep `Guid.TryParse`, `TryParseGuid`, `FindObject(` and
`ObjectIDs` under `src/Cordyceps/Tools/Unified/` and report every Grasshopper-object site with its
disposition. Rhino-object sites are left alone.

Owns: `src/Cordyceps/Core/ToolHelpers.cs`; every GhCanvasTool partial file under `src/Cordyceps/Tools/Unified/`
except the outline partial; `src/Cordyceps/Tools/Unified/GhWireTool.cs`;
`src/Cordyceps/Tools/Unified/GhScriptTool.cs`; the 02 anchor block.
Must not touch: `src/Cordyceps/Tools/Unified/GhInspectTool.cs` (03 owns it, including its `id`
description), `src/Cordyceps/Tools/Unified/GhCanvasTool.Outline.cs`, the files under
`src/Cordyceps/Core/Canvas/` (a needed change is sent to the coordinator, who lands it on the branch
for 02 and 04 to rebase onto), `CHANGELOG.md`, `README.md`, `src/Cordyceps/McpServer.cs`, Knowledge guides.
Depends on: 01 merged.
Visual change: yes (error text agents read). The live check is queued.

### Chunk 03: gh_inspect(action='tree') on a shared data-tree reader
Type: code (feature). Independent of 01.

Deliverables:
- [ ] new `src/Cordyceps/Core/DataTrees/TreeSummary.cs`: lifted from Hyperceps Core/TreeSummary.cs
  with a header attribution, the C3 note fixes, `invalid` counting, the input-alignment comparison,
  and no silent clamp (the range check moves to the action).
- [ ] new `src/Cordyceps/Core/DataTrees/DataTreeReader.cs`: host adapter from `IGH_Structure` to
  branches (path, count, nulls, invalid), so `outputs`, sweep and outline can use it in later waves.
  Not linked into tests.
- [ ] new `src/Cordyceps/Tools/Unified/GhInspectTool.Tree.cs`: the action per C3. Liveness check
  before marshalling. Every target parsed and resolved before any read.
- [ ] `src/Cordyceps/Tools/Unified/GhInspectTool.cs`:
  - Make the class `partial`; add `ActionInfo["tree"]`, the pipe-list entry and dispatch.
  - New parameters `ids`, `param`, `side`.
  - `id` description in names form (C1 wording; true once 02 lands, in the same PR).
  - **`limit` sentinel fix (report 2.6 #1):** the default becomes `0`, meaning "this action's
    default", in the signature (line 132) and in `BuildParams` (line 144). `log` maps 0 to 100. A
    negative `limit` is an error for `log` (today it reaches `GetRange` with a negative count,
    line 398) and for `tree`.
- [ ] new `src/Cordyceps.Tests/TreeSummaryTests.cs`: the 9 Hyperceps tests, plus:
  - a single `{0;0;0}` branch gives no shared-index note;
  - two branches sharing leading indices give the note, worded per C3;
  - mixed depth without "Simplify";
  - invalid counted;
  - input alignment with differing counts, with equal counts but different paths, and with one
    input of a single branch (no note);
  - the run cap at exactly 100 is honoured.
- [ ] `src/Cordyceps.Tests/Cordyceps.Tests.csproj`: the link for the new `src/Cordyceps/Core/DataTrees/TreeSummary.cs` after the 03 anchor.

What proves it: `dotnet test … --filter "FullyQualifiedName~TreeSummaryTests"` plus a Release build.
Owns: the files above and the 03 anchor block. The body of `ActionStatus` in `src/Cordyceps/Tools/Unified/GhInspectTool.cs`
belongs to 04; do not edit it.
Must not touch: `src/Cordyceps/Core/ToolHelpers.cs`, `src/Cordyceps/Core/Canvas/`, gh_canvas files,
`CHANGELOG.md`, `README.md`, `src/Cordyceps/McpServer.cs`, Knowledge guides.
Depends on: Prep.
Visual change: yes. Queued live checks:
- Partition, Graft and Flatten shapes;
- `side='input'` on a component whose input is grafted (does the input tree show the graft?);
- the solver turned off by both routes;
- `limit=100`.

### Chunk 04: gh_canvas(action='outline') on the canvas snapshot
Type: code (feature).

Deliverables:
- [ ] new `src/Cordyceps/Core/Canvas/OutlineView.cs`: host-free renderer of C2 over the snapshot,
  adapted from Hyperceps Core/Outline/CanvasOutline.cs, with a header attribution. It fixes report
  1.6 defects 1-6 and 8 as C2 states them, and has no Lichen or THALLUS code (owner ruling 5).
  `OutlineModel` is not lifted; the snapshot replaces it.
- [ ] `src/Cordyceps/Tools/Unified/GhCanvasTool.Outline.cs` (from the prep stub):
  - Liveness check before marshalling.
  - Validates the parameters per C2 before reading.
  - `CanvasReader.Read(doc, Full)`, then `ObjectResolver` for `id` and `group`, then `OutlineView`.
  - `OutlineActionInfo()`: description, optional `id`, `group` and `depth`, an example, and tips that
    state the C2 caps, the stale marker and the relation to `status`.
- [ ] `src/Cordyceps/Tools/Unified/GhInspectTool.cs`: the body of `ActionStatus` only. Its
  classification calls `ObjectHealth`, and its output is byte-identical for the same canvas (the
  same fields, statuses and message joins).
- [ ] new `src/Cordyceps.Tests/CanvasOutlineTests.cs`: the 12 Hyperceps cases ported to the snapshot,
  plus:
  - overlapping sibling groups (both tags, the `shares` field, a summary that counts once, edges
    from both groups);
  - a group-level cycle;
  - a multi-output end;
  - solver off and an unsolved owner (`stale`) and a locked owner (`disabled`);
  - an aborted last solution;
  - DISCONNECTED in `problems`;
  - every capped field showing `+N more` and `more`;
  - golden strings for one overview, one group detail and one around;
  - the identifier round-trip over every first token and port token;
  - a long group name (60 characters) printed whole;
  - a group detail of 2,000 members ordered without timing out (asserted by a bound on comparisons
    or an elapsed bound loose enough not to flake on the 2-core CI runner; no wall-clock test that
    can flake, per project-preferences "Parallelization").
- [ ] `src/Cordyceps.Tests/Cordyceps.Tests.csproj`: the link for the new `src/Cordyceps/Core/Canvas/OutlineView.cs` after the 04 anchor.

What proves it: `dotnet test … --filter "FullyQualifiedName~CanvasOutlineTests|FullyQualifiedName~ObjectHealthTests"`
plus a Release build.
Owns: the new `src/Cordyceps/Core/Canvas/OutlineView.cs`, `src/Cordyceps/Tools/Unified/GhCanvasTool.Outline.cs`,
`ActionStatus` in `src/Cordyceps/Tools/Unified/GhInspectTool.cs`, its test file, the 04 anchor.
Must not touch: 01's files under `src/Cordyceps/Core/Canvas/` (changes go through the coordinator, as
in 02), `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`, `src/Cordyceps/Core/ToolHelpers.cs`,
`CHANGELOG.md`, `README.md`, `src/Cordyceps/McpServer.cs`, Knowledge guides.
Depends on: 01 merged, and 03 merged (both edit `src/Cordyceps/Tools/Unified/GhInspectTool.cs`, in disjoint regions).
Visual change: yes. Queued live checks:
- the overview of a real grouped definition, a messy ungrouped one and a cluster document;
- a token count against `list` + `gh_wire list` + `group_list` on the same file;
- every printed name pasted back into `info`;
- the solver off.

The delegate's report gives the final grammar as implemented (the golden strings), for 05 to
transcribe.

### Chunk 05: documentation, verification queue, integration
Type: cumulative-final. Coordinator.

- [ ] Merge 01 and 03, then 02 and 04. Combined suite green with evidence recorded via
  `prawduct-hook test-evidence record`; plugin Release build clean.
- [ ] `CHANGELOG.md` under `## [Unreleased]`.
  - Added: `outline`; `tree`; names accepted wherever an object or group is taken.
  - Changed, marked breaking:
    - `group_move` and `list group=` match group names exactly (case and whole name);
    - member arrays fail the call on any unresolved id instead of skipping it;
    - `gh_wire list` with an unknown id is an error;
    - non-GUID error texts changed;
    - response echoes carry the resolved GUID;
    - `gh_inspect` `limit` defaults to 0 (the action's default).
  - Fixed: infrastructure existence leaked by `move`, bulk connect and `group_move`; a negative
    `log` limit threw.
- [ ] `src/Cordyceps/McpServer.cs` `GetServerInstructions()`:
  - `outline` and `tree` in the tool lines;
  - a "Names" key point (C1 in four lines: GUID, 8+ hex prefix, unique case-sensitive nickname,
    group names on group actions only; ambiguous names act on nothing and list candidates);
  - outline as the first read for orientation;
  - the "VERIFY PERIODICALLY … status" line kept;
  - the gh://docs/canvas-outline resource listed.
  - The "do NOT rename components" guidance stays: agent conventions are a later wave (owner
    ruling 5).
- [ ] Knowledge:
  - new `src/Cordyceps/Knowledge/CanvasOutlineGuide.md`: the C2 grammar transcribed from 04's
    golden strings, the caps, the stale marker, overlapping groups, what it does not show;
  - `src/Cordyceps/Knowledge/DataTreesGuide.md`: `tree` in "Rules" and "Debugging";
  - `src/Cordyceps/Knowledge/GettingStartedGuide.md`: outline first, and ids accept names;
  - `src/Cordyceps/Knowledge/CommonErrorsGuide.md`: rows for an ambiguous nickname, a too-short
    prefix, a group name on an object action, `Object is not a group`, and stale counts;
  - `src/Cordyceps/Knowledge/BestPracticesGuide.md` (line 65);
  - the prompt markdown `src/Cordyceps/Knowledge/Prompts/DebugDataMismatch.md` and
    `src/Cordyceps/Knowledge/Prompts/OptimizeCanvas.md`.
- [ ] `src/Cordyceps/Resources/ResourceRegistry.cs` and `src/Cordyceps/Cordyceps.csproj`: register and
  embed the new guide.
- [ ] `README.md`:
  - tool table rows for gh_canvas and gh_inspect;
  - the credits paragraph (line 188): the canvas outline, the data-tree summary and the object-name
    rule are adapted from Hyperceps, while the batch edits and sweeps still "shape where Cordyceps
    goes next";
  - the footer (line 192): "Naming rules, canvas outline and data-tree summary adapted from
    Hyperceps". `LICENSE` is unchanged; it already carries the copyright line.
- [ ] `CLAUDE.md`: GhCanvasTool and GhInspectTool lines name `outline` and `tree`; one Core paragraph
  for the new `src/Cordyceps/Core/Canvas/` folder (snapshot, reader, resolver; "change name resolution in the resolver, not at a
  call site").
- [ ] `.prawduct/operator-verification.md`: VRF-019 (02's numbered site checks plus 01's reads),
  VRF-020 (03), VRF-021 (04), each with the unverified runtime facts listed under Requirements
  Confidence.
- [ ] new `.prawduct/changes/canvas-read.md`: the fragment, with `closes:` for issues filed for the
  three defects found in source (silent member skips; infrastructure leaks in move, bulk connect
  and group_move; the negative log limit) if the coordinator files them.
- [ ] Backlog items for the deliberately deferred work (see the report).
- [ ] Cumulative Critic dispositioned; reflection.
