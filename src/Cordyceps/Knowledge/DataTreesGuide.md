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
modifiers are left unchanged, and a call with none of them reads instead of writing. `param` is a
name or 0-based index. `gh_canvas(action='info', id='...')` reports `modifiers` for every param —
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
4. Check with Panel or `gh_inspect(action='outputs')`

## Debugging

1. `gh_inspect(action='outputs', id='...')` — branch/item counts
2. `gh_inspect(action='trace', id='...', direction='upstream')` — find where structure diverges
3. Add Panel components to visualize
