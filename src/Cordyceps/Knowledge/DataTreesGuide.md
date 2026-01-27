# Grasshopper Data Trees Guide

## What Are Data Trees?

Data trees are Grasshopper's hierarchical data structure for organizing collections of data. Unlike simple lists, data trees can represent complex, nested relationships using **paths**.

Think of a data tree like a file system:
- A path like `{0;1;2}` is similar to a folder path like `/0/1/2/`
- Each branch (path) contains a list of items
- Components process data by matching branches

## Why Data Trees Exist

When you create multiple circles from multiple points, each circle needs to "know" which point created it. Data trees preserve this relationship through matching paths.

**Example:**
```
Input Points:        Output Circles:
{0} [P1, P2, P3]  →  {0} [C1, C2, C3]   // 3 points → 3 circles
{1} [P4, P5]     →  {1} [C4, C5]       // 2 points → 2 circles
```

The path `{0}` links points 1-3 to circles 1-3. The path `{1}` links points 4-5 to circles 4-5.

## Path Notation

Paths use semicolon-separated integers in curly braces:
- `{0}` - Single-level path, branch 0
- `{0;0}` - Two-level path, first branch of first branch
- `{0;1;2}` - Three-level path

**Reading paths:**
- `{0}` = "the first branch"
- `{1;3}` = "the fourth sub-branch of the second branch"
- `{2;0;5}` = "the sixth item path in a three-level hierarchy"

## Access Modes

Every component parameter has an **access mode** that determines how it consumes data:

### Item Access
Processes one item at a time. If given a list, runs once per item.
```
Input: [A, B, C]
Processing: Process(A), Process(B), Process(C)
```

### List Access
Processes entire lists at once. Maintains branch structure.
```
Input: {0}[A, B]  {1}[C, D, E]
Processing: Process([A,B]), Process([C,D,E])
```

### Tree Access
Receives the entire tree structure. Used for tree manipulation components.
```
Input: Full tree with all branches
Processing: Process(entire_tree)
```

## Data Matching Rules

When two inputs have different tree structures, Grasshopper uses these matching rules:

### Same Structure (Ideal)
Both inputs have identical branch counts and item counts:
```
A: {0}[1,2,3]  {1}[4,5,6]
B: {0}[a,b,c]  {1}[d,e,f]
Result: {0}[(1,a),(2,b),(3,c)]  {1}[(4,d),(5,e),(6,f)]
```

### Different Item Counts (Longest List)
Shorter list repeats its last item:
```
A: {0}[1, 2, 3, 4, 5]
B: {0}[a, b]
Result: {0}[(1,a), (2,b), (3,b), (4,b), (5,b)]
```

### Different Branch Counts (Cross Reference)
Creates combinations of all branches:
```
A: {0}[1]  {1}[2]
B: {0}[a]
Result: {0;0}[(1,a)]  {1;0}[(2,a)]  // B's branch 0 matches both A branches
```

**Warning:** Mismatched structures often produce unexpected results. Always visualize your data!

## The Six Rules for Data Trees

### 1. Never Flatten Chaotically
Don't randomly apply Flatten/Graft/Simplify hoping for results. With complex structures, these operations destroy relationships.

### 2. Use Shift Path Instead of Simplify
`Shift Path` lets you adjust path depth while preserving information. `Simplify` permanently removes path levels.

### 3. Apply Operations at Parameters
Right-click on component inputs/outputs to apply:
- **Flatten** (⇒) - Collapse all branches to one
- **Graft** ({}) - Create a branch for each item
- **Simplify** (∅) - Remove redundant path levels

These show visual indicators on the parameter.

### 4. Use Path Mapper for Complex Cases
The `Path Mapper` component can rearrange any tree structure. It's powerful but requires understanding path expressions.

### 5. Match Tree Structures
The cleanest workflows have inputs with matching tree structures. Use Graft, Shift Path, or Path Mapper to align them before connecting.

### 6. Visualize Your Data
Use `Panel` components to see data. Use `Param Viewer` with "Draw Tree" mode to visualize structure.

## Common Operations

### Flatten
Collapses all branches into a single list. **Destroys** branch structure.
```
Before: {0}[A,B] {1}[C,D] {2}[E]
After:  {0}[A,B,C,D,E]
```
**Use when:** You need all items in one list and don't care about grouping.

### Graft
Creates a new branch for each item. **Increases** path depth.
```
Before: {0}[A,B,C]
After:  {0;0}[A] {0;1}[B] {0;2}[C]
```
**Use when:** Each item needs to be processed independently (1:N operations).

### Simplify
Removes path levels that are all zeros. Can cause path collisions.
```
Before: {0;0}[A] {0;1}[B] {1;0}[C]
After:  {0}[A] {1}[B] {0}[C]  // COLLISION! Two branches now have path {0}
```
**Use when:** Path has redundant depth from previous operations.

### Shift Path
Moves path indices without flattening:
```
Shift -1: {0;1;2}[A] → {1;2}[A]    // Remove first index
Shift +1: {0;1}[A]   → {0;0;1}[A]  // Add zero at start
```
**Use when:** Aligning path depths between data sources.

### Flip Matrix
Swaps rows and columns for 2D data:
```
Before: {0}[A,B,C] {1}[D,E,F]    // 2 rows, 3 columns
After:  {0}[A,D] {1}[B,E] {2}[C,F]  // 3 rows, 2 columns
```
**Use when:** Data organized in rows but you need columns (or vice versa).

## Common Mistakes

### Mistake 1: Mismatched Input Structures
**Symptom:** Unexpected combinations or missing outputs
**Fix:** Check both inputs with Panel, align structures with Graft/Flatten/Path Mapper

### Mistake 2: Premature Flattening
**Symptom:** Lost grouping, can't reconstruct relationships
**Fix:** Only flatten at the end if truly needed; prefer Shift Path

### Mistake 3: Ignoring Access Modes
**Symptom:** List operations on single items, or item operations getting lists
**Fix:** Check component documentation for expected access mode

### Mistake 4: Not Visualizing Data
**Symptom:** Debugging blind, can't understand what's happening
**Fix:** Add Panel components liberally, use Param Viewer

## Debugging Data Trees

1. **Add Panels everywhere** - Show data at each stage
2. **Use Param Viewer** - Right-click → "Draw Tree" shows structure
3. **Check component status** - Orange = warning, Red = error
4. **Trace upstream** - Find where structure diverges from expectation
5. **Isolate the problem** - Disconnect components to test individually

## Quick Reference

| Operation | Effect | Depth Change |
|-----------|--------|--------------|
| Flatten | Merge all to one branch | Reduces to 1 |
| Graft | One branch per item | +1 level |
| Simplify | Remove zero indices | Varies |
| Shift Path | Move path indices | +/- N levels |
| Path Mapper | Custom rearrangement | Any |
| Flip Matrix | Swap rows/columns | Same |

| Access Mode | Processes | Use For |
|-------------|-----------|---------|
| Item | One at a time | Most operations |
| List | Whole lists | List operations (sort, reverse) |
| Tree | Full structure | Tree manipulation |
