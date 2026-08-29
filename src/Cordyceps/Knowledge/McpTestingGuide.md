# MCP Server Testing

Use `action='help'` on any tool to discover capabilities. For each area: try operations, verify results are correct (not just successful), test edge cases.

## Part 1: Connection

- Document info works
- `action='help'` returns info for all 7 tools
- Cordyceps component is protected (invisible, can't modify/delete)
- Component search and docs work

## Part 2: Grasshopper Core

| Area | Test |
|------|------|
| Canvas | Add/move/rename/delete components, validate overlaps |
| Wiring | Connect/disconnect, bulk wiring, validate before connecting |
| Values | Set slider/panel/toggle, configure ranges, enable/disable |
| Document | Solver control, snapshots save/restore, capture canvas |
| Groups | Create with colors, add/remove members, move |
| Scripts | Add C#/Python, get/set code, configure parameters |
| Inspection | Check status, inspect outputs, trace data flow |

## Part 3: Rhino Integration

| Area | Test |
|------|------|
| Objects | Bake geometry, list/select/hide/show/delete |
| Layers | List, filter by layer, create/delete |
| Viewport | Display modes, camera position/target, zoom |
| Capture | Viewport to image, view and resolution options |
| Rendering | Raytraced progress, wait for passes |

## Part 4: End-to-End

1. **Circle grid**: Sliders for rows/columns/radius → verify geometry updates
2. **Bake + render**: GH geometry → bake → camera → capture
3. **Organized definition**: Groups with colors → capture canvas
4. **Debug broken**: Create error → use inspection to identify

## Part 5: Error Handling

Test graceful failure: invalid names, bad IDs, invalid connections, out-of-range values, protected component modification. Errors should be informative; system should stay usable.

A failed tool call returns a normal result with the MCP `isError` flag set to `true` and a body containing `"success": false` and an `"error"` message (responses also carry a `status` member — match on the fields you care about rather than the whole object) — this holds whether the tool returns the failure or throws internally. Reserve JSON-RPC protocol errors (e.g. code `-32603`) for request-level problems like an unknown tool or a missing required parameter, not for tool-execution failures.

## Summary Template

```
## Test Summary
Date: [date]
Sections: [1,2,3,4,5]

### Works Well
- [list]

### Issues
- [with reproduction steps]

### Assessment
[READY / NEEDS WORK / SIGNIFICANT ISSUES]
```
