# Grasshopper Data Trees

## Core Concept

Data trees organize data into branches with paths like `{0;1;2}`. Each branch contains a list of items. Components process data by matching paths between inputs.

## Path Notation

- `{0}` - Branch 0
- `{0;1}` - Branch 1 inside branch 0
- `{0;1;2}` - Three-level nesting

## Access Modes

| Mode | Behavior | Use For |
|------|----------|---------|
| Item | Process one item at a time | Most operations |
| List | Process entire branch at once | Sort, reverse, list operations |
| Tree | Receive full tree structure | Tree manipulation |

## Data Matching Rules

### Same Structure
Both inputs have matching branches and counts → items pair directly.

### Same Path, Different Counts (Longest List)
Two lists at the same path → shorter list repeats last item:
```
A: {0}[1, 2, 3, 4, 5]
B: {0}[a, b]
→ [(1,a), (2,b), (3,b), (4,b), (5,b)]  — 5 outputs
```

### Different Paths (Cross Product)
Items in different branches combine with all items in other branches:
```
A: {0}[1, 2, 3]
B: {0}[a]  {1}[b]  {2}[c]   (grafted)
→ [(1,a), (1,b), (1,c), (2,a), (2,b), (2,c), (3,a), (3,b), (3,c)]  — 9 outputs
```

### The N×M Problem
**Symptom**: You have N items and M items, expect N×M combinations, but get only max(N,M).

**Cause**: Both inputs are flat lists at the same path. Grasshopper uses longest-list matching.

**Fix**: Graft one input to put each item in its own branch. This triggers cross-product behavior.

## When to Graft: Deciding Before You Wire

Before connecting two data sources to a component, determine their **relationship**:

### Parallel (Paired) Data
The lists correspond by index—item₁ goes with item₁, item₂ with item₂.
- Created together or have inherent 1:1 correspondence
- Want N outputs from N items
- **Don't graft**

### Independent Data
The lists have no inherent correspondence—every item should combine with every other.
- Created separately from unrelated sources
- Want N×M outputs from N and M items
- **Graft one input**

### The Decision Question

**"Do these lists have a 1:1 correspondence, or should every item from A interact with every item from B?"**

| Relationship | Expected Output | Action |
|--------------|-----------------|--------|
| Paired by index | N items | Don't graft |
| All combinations | N×M items | Graft one |

*Example*: 8 geometry items + 3 offset values. Are they paired (geometry₁ gets offset₁, geometry₂ gets offset₂...)? Or independent (each geometry gets ALL offsets)? The answer determines whether to graft.

## Tree Operations

| Operation | Effect | When to Use |
|-----------|--------|-------------|
| Graft | Each item → own branch (+1 depth) | **Get all combinations** (N×M instead of max(N,M)) |
| Flatten | Merge all branches → single list | Collect items, discard grouping |
| Simplify | Remove redundant path levels | Clean up `{0;0;0}` → `{0}` |
| Shift Path | Add/remove path levels | Align depths between sources |
| Flip Matrix | Swap rows/columns | Reorganize 2D data |

**Most common need**: Graft one input when you want all combinations of two lists.

## Operation Examples

**Graft** (most common - for cross products):
```
Before: Points{0}[P1,P2,P3]  +  Vectors{0}[V1,V2]
Result: Move outputs 3 items (longest list)

After grafting Vectors: Vectors{0}[V1] {1}[V2]
Result: Move outputs 6 items (3×2 cross product)
```

**Flatten**:
`{0}[A,B] {1}[C,D]` → `{0}[A,B,C,D]`

**Simplify** (use cautiously - can cause collisions):
`{0;0}[A] {0;1}[B] {1;0}[C]` → `{0}[A] {1}[B] {0}[C]` ← collision!

**Shift Path -1**:
`{0;1;2}[A]` → `{1;2}[A]`

## Rules

1. **Don't flatten randomly** - destroys relationships
2. **Use Shift Path over Simplify** - preserves information
3. **Match structures before connecting** - prevents unexpected cross-products
4. **Check structures with Panel** - visualize before connecting

## Common Mistakes

| Symptom | Cause | Fix |
|---------|-------|-----|
| N×M expected, got max(N,M) | Both inputs flat at same path | Graft one input |
| Unexpected combinations | Mismatched tree structures | Align with Graft/Flatten/Path Mapper |
| Lost grouping | Premature flattening | Only flatten when structure irrelevant |
| Wrong item count | Access mode mismatch | Check component expects Item vs List |
| Empty output | Type conversion returned nothing | Verify input types |

## Debugging

1. Add Panel components to see data at each stage
2. Use `gh_inspect(action='outputs', id='...')` to check branch/item counts
3. Use `gh_inspect(action='trace', id='...', direction='upstream')` to find where structure diverges
