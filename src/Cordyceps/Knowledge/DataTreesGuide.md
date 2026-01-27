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

### Different Item Counts (Longest List)
Shorter list repeats last item:
```
A: [1, 2, 3, 4, 5]
B: [a, b]
→ [(1,a), (2,b), (3,b), (4,b), (5,b)]
```

### Different Branch Counts
Creates cross-product combinations. Often causes unexpected results.

## Tree Operations

| Operation | Effect | When to Use |
|-----------|--------|-------------|
| Flatten | Merge all branches to one list | Need all items together, structure irrelevant |
| Graft | Create branch per item (+1 depth) | Each item needs independent processing |
| Simplify | Remove zero-only path levels | Clean up redundant nesting |
| Shift Path | Add/remove path levels | Align depths between sources |
| Flip Matrix | Swap rows/columns | Reorganize 2D data |

## Operation Examples

**Flatten**:
`{0}[A,B] {1}[C,D]` → `{0}[A,B,C,D]`

**Graft**:
`{0}[A,B,C]` → `{0;0}[A] {0;1}[B] {0;2}[C]`

**Simplify** (warning: can cause collisions):
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
| Unexpected combinations | Mismatched tree structures | Align with Graft/Flatten/Path Mapper |
| Lost grouping | Premature flattening | Only flatten when structure irrelevant |
| Wrong item count | Access mode mismatch | Check component expects Item vs List |
| Empty output | Type conversion returned nothing | Verify input types |

## Debugging

1. Add Panel components to see data at each stage
2. Use `get_component_outputs` to check branch/item counts
3. Use `trace_data_flow(id, "upstream")` to find where structure diverges
