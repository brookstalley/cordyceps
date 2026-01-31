# Cordyceps MCP Server Testing Guide

**Keywords:** test cordyceps, test mcp, test grasshopper, validate mcp

## How to Test

Use `action='help'` on any tool to discover capabilities. For each area: try operations, verify results are correct (not just successful), test edge cases.

---

## Part 1: Connection and Fundamentals

**Verify:**
- Document info retrieval works
- `action='help'` returns useful info for all 7 tools
- Cordyceps component is invisible (not in listings, can't be modified/deleted)
- Component search and documentation work

---

## Part 2: Grasshopper Core

**Canvas**: Add/move/rename/delete components, validate layout overlaps

**Wiring**: Connect/disconnect, bulk wiring, validate before connecting, list connections

**Values**: Set slider/panel/toggle values, configure slider ranges, enable/disable components

**Document**: Solver control (disable/enable), snapshots (save/restore), capture canvas

**Groups**: Create with colors, add/remove members, move groups

**Scripts**: Add C#/Python scripts, get/set code, configure parameters

**Inspection**: Check status for errors, inspect outputs, trace data flow

---

## Part 3: Rhino Integration

**Objects**: Bake geometry, list/select/hide/show/delete objects

**Layers**: List layers, filter objects by layer, create/delete layers

**Viewport**: Switch display modes, control camera position/target, zoom to fit

**Capture**: Capture viewport to image, specify view and resolution

**Rendering**: Check raytraced progress, wait for render passes

---

## Part 4: End-to-End Scenarios

### A: Parametric Circle Grid
Create grid of circles with sliders for rows, columns, radius. Verify geometry updates when sliders change.

### B: Bake and Render
Create geometry in Grasshopper → bake to Rhino → set camera → capture rendered image.

### C: Organized Definition
Build a definition with groups (inputs, processing, outputs) using different colors. Capture canvas to verify.

### D: Debug Broken Definition
Create definition with intentional error. Use inspection tools to identify the problem.

---

## Part 5: Error Handling

Test graceful failure:
- Invalid component names
- Non-existent IDs
- Invalid connections
- Out-of-range values
- Protected component modification

Errors should be informative and system should remain usable.

---

## Test Summary Template

```
## Test Summary
**Date**: [date]
**Sections Completed**: [1, 2, 3, 4, 5]

### What Works Well
- [capabilities that worked]

### Issues Found
- [problems with reproduction steps]

### Overall Assessment
[READY / NEEDS WORK / SIGNIFICANT ISSUES]
```
