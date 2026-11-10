# Data Trees

Data trees organize data into branches with paths like `{0;1;2}`. Components match paths between inputs.

## Path Notation

`{0}` = branch 0, `{0;1}` = nested, `{0;1;2}` = three levels

## Access Modes

| Mode | Behavior | Use |
|------|----------|-----|
| Item | One item at a time | Most operations |
| List | Entire branch | Sort, reverse, list ops |
| Tree | Full structure | Tree manipulation |

## Data Matching

**Same structure**: Items pair directly.

**Same path, different counts** (Longest List):
```
A: {0}[1,2,3,4,5]  B: {0}[a,b]
→ [(1,a),(2,b),(3,b),(4,b),(5,b)]  — 5 outputs, last item repeats
```

**Different paths** (Cross Product):
```
A: {0}[1,2,3]  B: {0}[a] {1}[b] {2}[c]
→ 9 outputs (3×3 combinations)
```

## The N×M Problem

**Symptom**: Have N and M items, expect N×M, get max(N,M).

**Cause**: Both inputs flat at same path → longest-list matching.

**Fix**: Graft one input → each item gets own branch → cross-product.

## When to Graft

Before connecting, ask: **"Do these lists have 1:1 correspondence, or should every A combine with every B?"**

| Relationship | Expected | Action |
|--------------|----------|--------|
| Paired by index | N items | Don't graft |
| All combinations | N×M items | Graft one |

*Example*: 8 geometries + 3 offsets. Paired (geo₁↔offset₁)? Don't graft. Independent (each geo gets ALL offsets)? Graft offsets.

## Tree Operations

| Operation | Effect | When |
|-----------|--------|------|
| **Graft** | Each item → own branch | Get N×M combinations |
| Flatten | All branches → single list | Collect, discard grouping |
| Simplify | Remove redundant levels | Clean up (use cautiously) |
| Shift Path | Add/remove levels | Align depths |
| Flip Matrix | Swap rows/columns | Reorganize 2D |

## Modifiers on the Port Itself

Graft, Flatten, Simplify and Reverse are usually set on the port rather than by inserting a
component — that is the idiomatic Grasshopper form and keeps the canvas readable:

```
gh_canvas(action='modifier', id='<component>', side='input', param='B', mapping='graft')
gh_canvas(action='modifier', id='<component>', side='input', param='B')   # read current state
```

`mapping` is `none` | `flatten` | `graft`; `simplify` and `reverse` are true/false. Omitted
modifiers are left unchanged, and a call with none of them reads instead of writing. `param` is the
port's exact name or nickname, or its 0-based index (partial names are refused with a suggestion). `gh_canvas(action='info', id='...')` reports `modifiers` for every param —
that's how you detect an existing Graft you didn't set.

## Example

```
Before: Points{0}[P1,P2,P3] + Vectors{0}[V1,V2]
Move → 3 outputs (longest list)

After grafting Vectors: {0}[V1] {1}[V2]
Move → 6 outputs (3×2 cross product)
```

## Rules

1. Don't flatten randomly — destroys relationships
2. Use Shift Path over Simplify — preserves info
3. Match structures before connecting
4. Check the shape with `gh_inspect(action='tree')` before pulling data with `gh_inspect(action='outputs')` or a Panel

## Seeing a Tree's Shape

`gh_inspect(action='tree')` returns the structure of a data tree without its data: branch paths,
items per branch, depth, nulls and invalid items. It reads what is already on the canvas and
triggers no solve.

```
gh_inspect(action='tree', id='abc12345')                          # every output of one object
gh_inspect(action='tree', id='abc12345', param='Result')          # one output
gh_inspect(action='tree', id='abc12345', side='input')            # every input, as the component receives it
gh_inspect(action='tree', ids='["abc12345", {"id": "def67890", "param": "Result"}]')
```

- Give exactly one of `id` or `ids` (a JSON array of 1-50 entries, each an id string or
  `{"id": "...", "param": "..."}`). Ids take a GUID, an 8+ hex GUID prefix, or a unique nickname.
- `param` (only with `id`; per entry with `ids`, where it may also be an integer index): a port by
  name, nickname or 0-based index. Without it, every port on the side is read.
- `side`: `output` (default) or `input`. A parameter object (slider, panel, free parameter) has one
  tree, so `param` and `side` do not apply; its tree reads `side: "parameter"`.
- `limit`: path groups per tree, 1-500, default 20. Out of range is an error.

Each tree is `{target, object, id, port, side, type, branches, items, depth, perBranch, paths, …}`.
`object` is the nickname as the canvas shows it (a label: two objects can share it); `id` is the
full GUID to pass to a follow-up call. Optional fields appear only when they apply: `nulls`,
`invalid` (non-null items whose `IsValid` is false), `emptyBranches`, `morePathGroups`, `notes`,
`solved: false` with a `note` saying why (locked, failed, still computing, or not computed),
and `message` (the owner's first warning or error, clipped at 160 characters, with `(+K more)`).
`depth` and `perBranch` are a number, or `{min, max}` when they vary. A `solver` field at the top
appears only when the solver is off.

`paths` are runs: `{0;0}..{0;9}: 1 each` is ten consecutive branches of one item each; `{0;3}: 4` is
one branch of four. `items` counts nulls.

Notes it can add:

| Note | Meaning |
|------|---------|
| `one item per branch (grafted shape)` | Every branch holds one item |
| `paths have different depths (1 to 2); branches from different sources won't line up` | Mixed path depths |
| `the first 2 path indices are the same in every branch, so they add depth without separating anything; Shift Paths is the usual way to remove leading indices (see the data trees guide)` | Redundant leading levels (needs 2+ branches and depth 3+) |
| `inputs don't line up: A has 3 branches, B has 12 branches` | With `side='input'` and no `param`: two inputs with several branches each disagree in branch count |
| `inputs have the same number of branches (2) but different paths: at branch 1, A has {1} and B has {5}` | Same count, different paths |
| `1 empty branch`, `1 null item`, `3 invalid items (not null, but IsValid is false)` | Counts of each |

An input with a single branch is broadcast against anything, so it never triggers the alignment note.

A target that fails (an unknown or ambiguous name, a group, a bad `param`) is listed in `errors` as
`ids[i]: …` (or `id: …`) and `success` is false, but the trees that did resolve are still returned.

## Debugging

1. `gh_inspect(action='tree', id='...', side='input')` — the shape each input receives, and whether the inputs line up
2. `gh_inspect(action='tree', id='...')` — the shape each output produces
3. `gh_inspect(action='trace', id='...', direction='upstream')` — find where structure diverges
4. `gh_inspect(action='outputs', id='...')` — the values themselves, once the shape is right
5. Add Panel components to visualize
