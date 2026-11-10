# Canvas Outline

`gh_canvas(action='outline')` describes a definition in a few compact lines: the groups and how
data flows between them, the controls and their values, item counts, and problems. Use it first
when you open a definition you did not build, instead of `list` + `gh_wire(action='list')` +
`group_list`.

Outline is for orientation. Its `problems` read the same objects as `gh_inspect(action='status')`
(components and parameter objects such as sliders, panels and free parameters) and classify them
with the same rule, but outline caps the list at 10 and `status` lists every one. Outline does not
replace `status`.

## Three views

```
gh_canvas(action='outline')                                  # overview
gh_canvas(action='outline', group='INPUTS')                  # one group's detail
gh_canvas(action='outline', group='["INPUTS", "MATHS"]')     # several groups
gh_canvas(action='outline', id='COUNT', depth=3)             # the wiring around one object
```

| Argument | Takes | Rules |
|----------|-------|-------|
| `group` | One group name or id, or a JSON array of them | Group names match exactly (case-sensitive). A text that starts with `[` must parse as a JSON array of strings, so a group named `[WIP] facade` is passed as `group='["[WIP] facade"]'` |
| `id` | One object: GUID, 8+ hex GUID prefix, or unique nickname | A group's id shows that group, like `group=` |
| `depth` | 1-10, default 2 | Only with `id`, and only when `id` names an object (a group is shown whole) |

Giving both `id` and `group` is an error. So is `depth` without `id`, `depth` outside 1-10, and
`depth` with a group's id. Nothing is clamped or guessed.

## Reading a line

Every object line, control line and problem line starts **`NAME · Type`**: the name is everything
before the first ` · `, and the type follows it, so a multi-word name is never confused with a
multi-word type. A group line starts with the group's name. The name is one **you can pass back as
an id**, to any tool that takes that kind of thing:

- an object's nickname when it is unique, not the type default, and printable (below); otherwise
  its short id (the shortest GUID prefix of 8+ characters that no other object shares);
- a group's name when it is unique, not the default name `Group`, and printable; otherwise its
  short id.

A name is never clipped or decorated. A nickname is **printable** only when it can be cut cleanly
out of any line: it is not empty, has no whitespace at either end, no line break or other control
character, does not start with `[` or `+`, and contains none of the sequences outline prints next
to a name: ` · `, `, `, `; `, ` ← `, ` → `, ` (`, ` [`, ` = `. Nor may one of those form where the
name meets what is printed beside it, so a name ending in `,` or `;`, or starting with `(` or
`= `, is not printable either. A group name also contains no `]`.
So an object nicknamed `Width (m)` is printed by its id. The nickname still works as an id where it
is unique; outline just does not print it. (A group name that starts with `[` is passed inside a
JSON array, as in the table above.)

| Mark | Meaning |
|------|---------|
| ` · ` | Separates fields |
| `[A, B]` | The groups the object sits in directly. Brackets mean nothing else |
| `←` | "fed from" |
| `→` | "feeds" |
| `PORT ← SOURCE` | An input and what feeds it. `PORT` is the port's nickname, or its name, or its 0-based index when both are shared, and it works as a `param`/`targetParam` spec |
| `SOURCE (PORT)` | Which output of the source, shown only when the source has more than one output |
| `PORT (graft)` | The input's modifiers: `graft`, `flatten`, `simplify`, `reverse` |
| `NAME (COUNT)` / `NAME (PORT: COUNT)` | Items on an output; `PORT:` appears only when the object has several outputs |
| `00000007 Circle` | Inside a list, a name is printed alone; a name that is only an id is followed by the object's type after a space (an id has no spaces) |
| `+N more` | A list was capped. On a top-level field (`controls`, `groups`, `problems`, `disabled`, `networks`, `in`, `out`, `fedFrom`, `feeds`) it is the last entry and `more` repeats the number. Inside one line (`in ←`, `inputs:`, `ends:`, `contains:`, `out:` and similar) it ends the text and is not counted in `more`. The `objects` lists of group detail and around never carry it: see Caps |
| `…` | A value, message or panel text was clipped (values at 30 characters, messages at 100, panel text at 30) |

## Counts: `stale` and `disabled`

A count prints as a bare number only when the solver is on and the object computed in the current
solution. Otherwise it reads `N stale`. A locked (disabled) object reads `disabled`, with no number.
Don't act on a stale 0: it can be a count from an earlier solution, or no solution at all.

The overview always has a `solver` field. It reads `on`, or one of:

- `off: this document's solver is disabled`
- `off: Grasshopper's solver is disabled`
- `off: this document's solver is disabled and Grasshopper's solver is disabled`

Any of these can end `; the last solution was aborted` (as can `on`, which then reads
`on; the last solution was aborted`). Whenever it is not plain `on`, the response adds
`note: "counts are from the last solution and may be out of date"`. The group and around views
carry `solver` and `note` only when the solver is not plain `on`.

## The overview

From a canvas with four groups (two of them overlapping), an error, a warning and a locked object:

```json
{
  "success": true,
  "document": "test.gh",
  "summary": "9 objects · 4 groups · 1 error · 1 warning · 1 disconnected · 1 disabled · 0 not solved · 1 preview off",
  "solver": "on",
  "controls": [
    "COUNT · Number Slider = 4 (0..10) [INPUTS]"
  ],
  "groups": [
    "INPUTS · 2 objects · out: COUNT (1) → MATHS, OUTPUT; NUMS (1) → MATHS",
    "MATHS · 3 objects · 1 error · contains: INNER (1 object) · shares 1 object with OUTPUT · in ← NUMS, COUNT · out: RESULT (4) → OUTPUT",
    "OUTPUT · 2 objects · 1 warning · shares 1 object with MATHS · in ← 00000003 Series, COUNT"
  ],
  "ungrouped": "3 objects · inputs: NOTE = \"read me\" · ends: 00000007 Circle (0), OLD (disabled)",
  "problems": [
    "SUM · Addition [MATHS] · error: Data conversion failed from Text to Number (+1 more)",
    "00000006 · Split List [OUTPUT] · warning: Index out of range",
    "00000007 · Circle · disconnected: missing R"
  ],
  "disabled": [
    "OLD"
  ],
  "next": "group='NAME' shows how one group is wired inside (a JSON list for several); id='NAME' shows what feeds one object and what it feeds (depth=N wires each way, default 2)"
}
```

(The ids here are a test canvas's; real short ids are 8+ hex characters of a real GUID.)

| Field | Content |
|-------|---------|
| `summary` | Always present, always the same eight counts in this order: `objects · groups · errors · warnings · disconnected · disabled · not solved · preview off`. An object in several groups is counted once |
| `solver`, `note` | See above |
| `controls` | Sliders, toggles and value lists: `NAME · Type = VALUE[ (min..max)][ [GROUPS]]`, left to right. Values are written in invariant culture (`0.5`, never `0,5`) |
| `groups` | One line per top-level group, upstream groups first: `GROUP · N objects[ · E errors][ · W warnings][ · D disconnected][ · contains: CHILD (n objects), …][ · shares K objects with OTHER, …][ · in ← NAME, …][ · out: NAME (COUNT) → GROUP, …]`. `out:` entries are separated by `; `; a recipient in no group is named directly |
| `ungrouped` | Objects in no group: `N objects · inputs: … · ends: … · feeds: GROUP, …`. An input is a control, or an unwired panel with text, that nothing feeds; an end is an object whose outputs feed nothing |
| `networks`, `unconnected` | Instead of `groups`/`ungrouped` on a canvas with no groups: one line per set of wired-together objects, largest first, `network k · N objects · inputs: NAME = VALUE, … · named: … · ends: NAME (PORT: COUNT, …)` (just `network ·` when there is one; every output of an end is counted); then `unconnected`, `N objects: NAME, …`, for objects with no wire |
| `problems` | Errors, then warnings, then disconnected, each left to right: `NAME · Type[ [GROUPS]] · error: MSG[ (+K more)]`, `… · warning: …`, `… · disconnected: missing PORT, …` |
| `disabled` | Locked objects |
| `more` | How many entries each capped field left out; absent when nothing was capped |
| `unread` | Only when Cordyceps could not read some wires (in all three views): `N wire(s) could not be read, so some inputs may show as unfed; gh_inspect(action='log') names them`. Treat an unfed input as unknown, not as unwired, when this is present |
| `next` | What to call next. When `problems` is capped it also says to run `gh_inspect(action='status')`, which lists every object's status, parameter objects included |

`controls`, `groups`, `ungrouped`, `problems` and `disabled` are left out when they would be empty (`networks` and `unconnected` likewise on a canvas with no groups). `summary`, `solver` and `next` are always present.

`problems` classifies with the rule `status` uses, over the same objects: an error or warning on an
object; DISCONNECTED for a component with a required input that has no wire and no data. A
parameter object has no inputs to miss, so it is listed only for an error or warning (in `status`
it reads ERROR, WARNING or OK). The summary's error, warning and disconnected counts therefore
match `status`'s `summary`.

## Overlapping and nested groups

An object can sit in several groups. It is tagged with all of them (`RESULT · Number [INNER, OUTPUT]`),
every group it sits in counts it, and `shares K objects with OTHER` on a group line explains group
counts that add up to more than the summary. A group inside another group appears in its parent's
`contains:` and has no line of its own in the overview; `group='CHILD'` shows it, and its summary
reads `N objects · inside PARENT`. Group order follows the wires between groups, then position
(left, then top); groups that feed each other in a cycle are kept together.

## Group detail

```json
{
  "success": true,
  "groups": [
    {
      "group": "MATHS",
      "id": "00001003",
      "summary": "3 objects · 1 error",
      "in": [
        "NUMS [INPUTS]",
        "COUNT [INPUTS]"
      ],
      "objects": [
        "00000003 · Series · S ← NUMS [INPUTS] · C ← COUNT [INPUTS]",
        "SUM · Addition · A ← 00000003 Series · error: Data conversion failed from Text to Number (+1 more)",
        "INNER · group · 1 object · in ← 00000003 Series · out: RESULT (4) → OUTPUT"
      ],
      "out": [
        "RESULT (4) → OUTPUT"
      ]
    }
  ]
}
```

- `in`: what feeds the group from outside, with the sources' groups. `out`: what leaves it, and where to.
- `objects`, upstream first. An object line is
  `NAME · Type[ = VALUE][ [GROUPS]][ · disabled][ · preview off][ · PORT[ (mods)] ← SOURCE[ (PORT)][ [GROUPS]], …][ · error: MSG | warning: MSG | disconnected: missing PORT, …]`.
  Unwired inputs without modifiers are left out. A parameter object's input has no port label
  (`← SOURCE`). The tags leave out the group being shown.
- A child group is one line: `GROUP · group · N objects · in ← … · out: …` (the name first, then
  what it is, as on an object line). A direct member that is
  also in a child group keeps its own line and is counted in the child's.
- Several groups in one call share one budget of 150 object lines; `more.objects` says how many were left out.

## Around one object

```json
{
  "success": true,
  "around": "RESULT",
  "depth": 1,
  "objects": [
    "00000003 · Series [MATHS] · S ← NUMS [INPUTS] · C ← COUNT [INPUTS]",
    "RESULT · Number [INNER, OUTPUT] · ← 00000003 Series",
    "00000006 · Split List [OUTPUT] · L ← RESULT · i (graft) ← COUNT [INPUTS] · warning: Index out of range"
  ],
  "fedFrom": [
    "NUMS [INPUTS]",
    "COUNT [INPUTS]"
  ],
  "feeds": [
    "SUM [MATHS]"
  ]
}
```

`objects` holds every object within `depth` wires upstream or downstream, upstream first, at most 60.
`around` names the centre; its own line carries no marker. `fedFrom` and `feeds` name the objects
just past the depth edge.

## Caps

| Field | Cap |
|-------|-----|
| `controls` | 20 |
| `groups` (overview lines) | 60 |
| `problems` | 10 |
| `disabled` | 20 |
| `networks` | 10 |
| Group detail object lines (shared across the call) | 150 |
| Group detail `in` / `out` | 16 each |
| Around `objects` | 60 |
| Names inside one field (`in ←`, `inputs:`, `ends:`, `contains:`, `fedFrom`, `feeds`, sources on one input) | 8 |
| `out:` entries on one group line / destinations per entry | 6 / 4 |

Two kinds of cap. A capped top-level list (`controls`, `groups`, `problems`, `disabled`, `networks`,
`in`, `out`, `fedFrom`, `feeds`) ends with an entry `+N more` and `more` records the same N under
that field's name. The `objects` arrays of group detail and around do not: every entry is a real
object line, and what was left out is reported only as `more.objects`. A `+N more` that ends the
text inside one line (the name caps) is not counted in `more` at all.

## What outline does not show

- Stored values on unwired inputs (values set by right-click "Set Number" and similar).
- Group colours (`group_list` reports them).
- Panel text on a wired panel (only unwired panels show their text).
- Plugin "wireless" connections (send/receive components that pass data without a Grasshopper
  wire). Outline follows wires only, so such objects look unconnected.
- Cordyceps' own bridge component and the objects wired to it.
- Every problem: `problems` stops at 10. When `more.problems` is set, run `gh_inspect(action='status')`, which lists every object's status, parameter objects included.

## Using the names

Paste any line's name (the text before its first ` · `), any `[GROUP]` tag, any `→ GROUP`
destination, or a group detail's `id` back into a call: object names into object actions (`info`,
`delete`, `gh_wire`, `gh_inspect`), group names into `group=` and the `group_*` actions. Port
tokens before `←` work as `param` and `targetParam`. A name that becomes ambiguous later (someone
adds a second `COUNT`) stops resolving: the error lists both ids, and nothing is acted on.

Outline never prints a default nickname or the default group name, because the actions that remove
things refuse them: `delete`, `group_delete`, `gh_wire(action='disconnect')` and
`gh_wire(action='clear')` do not resolve a default nickname (`Circle`, `Pt`) or the group name
`Group`, even when only one object carries it. The error prints the id to pass, or rename the object
first (`gh_canvas(action='rename')`, or `group_rename` for a group) and pass the new name. Every
other action still accepts a unique default nickname.

To see one data tree's shape, follow up with `gh_inspect(action='tree', id='NAME')`.
