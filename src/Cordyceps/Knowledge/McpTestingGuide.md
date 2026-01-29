# Cordyceps MCP Server Testing Guide

**Keywords:** test cordyceps, test mcp, test grasshopper, help test, validate mcp, mcp testing, grasshopper testing, cordyceps validation

## Purpose

This guide helps you systematically test and validate the Cordyceps MCP server. The goal is thoroughness—you should explore capabilities, push boundaries, and verify that both success and failure cases are handled properly.

**Testing philosophy**: Don't just verify that things work—verify that they work *correctly*. Check that outputs make sense, that errors are informative, and that the system behaves predictably.

## How to Test

For each section below:

1. **Explore** - Use `action='help'` on relevant tools to understand available capabilities
2. **Execute** - Try the suggested scenarios, adapting as needed
3. **Verify** - Check that results are correct, not just that calls succeeded
4. **Push boundaries** - Try edge cases, invalid inputs, and combinations
5. **Document** - Note what worked, what failed, and any surprises

Track your results:
```
## [Section Name]
- Tested: [what you tried]
- Worked: [what succeeded]
- Issues: [problems found, with details]
- Notes: [observations, suggestions]
```

---

## Part 1: Connection and Fundamentals

**Goal**: Verify the MCP connection is working and you can discover what's available.

### What to Verify

**Basic communication**: Can you get information about the current Grasshopper document? You should be able to retrieve document info, see what's on the canvas, and understand the current state.

**Tool discovery**: Every unified tool supports `action='help'`. Use this to understand what each tool can do. Verify the help output is useful and accurate.

**Infrastructure protection**: Cordyceps protects itself from accidental modification. Verify that:
- The Cordyceps component doesn't appear in component listings
- You cannot find, modify, or delete Cordyceps infrastructure
- Clearing the document preserves the MCP connection

**Component discovery**: Can you search for Grasshopper components by name? Can you get documentation about a component's inputs, outputs, and purpose? Try searching for common components (Circle, Panel, Slider) and verify the information is useful.

### Success Criteria

- [ ] You can retrieve document state
- [ ] Help is available and informative for all tools
- [ ] Infrastructure protection works (Cordyceps is invisible)
- [ ] Component search and documentation work

---

## Part 2: Grasshopper Core Tasks

**Goal**: Verify you can perform fundamental Grasshopper operations—the building blocks for any definition.

### Canvas Operations

Test your ability to manipulate the Grasshopper canvas:

- **Add components**: Can you add various component types (sliders, panels, geometry components, math operations)? Do they appear at the correct positions?
- **Positioning**: Can you move components? Do positions update correctly?
- **Naming**: Can you rename components for clarity? Can you find them by nickname later?
- **Deletion**: Can you remove components individually and in bulk?
- **Layout validation**: Can you detect overlapping components?

### Wiring

Test your ability to create and manage connections:

- **Basic connections**: Can you connect an output to an input?
- **Bulk wiring**: Can you create multiple connections efficiently?
- **Validation**: Can you check if a connection is valid before making it?
- **Disconnection**: Can you remove connections cleanly?
- **Connection listing**: Can you see all connections on the canvas?

### Values and Configuration

Test your ability to configure input components:

- **Sliders**: Can you set values? Configure ranges (min/max)?
- **Panels**: Can you set text content?
- **Toggles**: Can you set boolean values?
- **Component state**: Can you enable/disable components? Toggle preview visibility?

### Document Operations

Test document-level controls:

- **Solver control**: Can you disable the solver for bulk operations? Re-enable it?
- **Forced recompute**: Can you trigger a solution after making changes?
- **Undo/redo**: Do undo and redo work as expected?
- **Snapshots**: Can you save and restore document states?

### Groups

Test visual organization:

- **Creation**: Can you create named groups with custom colors?
- **Membership**: Can you add/remove components from groups?
- **Movement**: When you move a group, do all members move together?

### Scripts

If script components are important to your workflow, test:

- **Adding script components**: Can you add C# or Python script components?
- **Code management**: Can you get and set script source code?
- **Configuration**: Can you configure script inputs and outputs?

### Inspection and Debugging

Test your ability to understand what's happening:

- **Canvas status**: Can you see which components have errors, warnings, or disconnected inputs?
- **Output inspection**: Can you see what values a component is producing?
- **Data flow tracing**: Can you trace data flow upstream and downstream?
- **Geometry inspection**: Can you get bounding boxes and validity info for geometry?

### Capture

Test visualization:

- **Canvas capture**: Can you capture the Grasshopper canvas as an image?
- **Specific regions**: Can you capture just part of the canvas?

### Success Criteria

- [ ] You can build simple component networks (add, wire, configure)
- [ ] Solver control works for efficient bulk operations
- [ ] You can inspect and debug component state
- [ ] Groups and organization features work
- [ ] Canvas capture produces viewable images

---

## Part 3: Rhino Integration

**Goal**: Verify you can work with Rhino's 3D environment—objects, layers, viewport, and rendering.

### Scene Objects

Test your ability to work with Rhino objects:

- **Baking**: Can you bake Grasshopper geometry to Rhino? Does it appear on the correct layer?
- **Object listing**: Can you list objects in the scene? Filter by layer or type?
- **Selection**: Can you select objects programmatically?
- **Visibility**: Can you hide and show objects?
- **Deletion**: Can you delete baked objects?

### Layers

Test layer operations:

- **Layer listing**: Can you see all layers?
- **Layer filtering**: Can you filter objects by layer?

### Viewport Control

Test your ability to control what you see:

- **Display modes**: Can you list available modes? Switch between them (Wireframe, Shaded, Rendered, Raytraced)?
- **Camera control**: Can you get the current camera position? Set a new camera position and target?
- **Zoom**: Can you zoom to fit all geometry? Zoom to specific objects?

### Viewport Capture

Test your ability to capture the 3D view:

- **Basic capture**: Can you capture the viewport as an image?
- **View selection**: Can you capture from different views (Perspective, Top, Front)?
- **Resolution control**: Can you specify capture dimensions?

### Rendering (Raytraced)

If using Raytraced mode:

- **Status**: Can you check render progress (passes, completion)?
- **Waiting**: Can you wait for a minimum number of render passes before capturing?

### Success Criteria

- [ ] Baking works and objects appear in Rhino
- [ ] You can manipulate object visibility and selection
- [ ] Viewport and camera control work
- [ ] Viewport capture produces viewable images
- [ ] (If tested) Raytraced rendering status and waiting work

---

## Part 4: End-to-End Scenarios

**Goal**: Test realistic workflows that combine multiple capabilities. These scenarios are intentionally ambitious—they test whether you can accomplish real goals, not just call individual functions.

### Scenario A: Parametric Circle Grid

**Challenge**: Create a parametric grid of circles where both the grid dimensions (rows, columns) and circle radius are controlled by sliders.

**What this tests**:
- Adding multiple component types
- Wiring a non-trivial data flow
- Configuring slider ranges appropriately
- Layout and organization
- Verifying the result visually

**Verification**: Capture the canvas and viewport. Adjust slider values and verify the geometry updates correctly.

### Scenario B: Geometry Analysis Pipeline

**Challenge**: Create a definition that takes a curve (via a Curve parameter), measures its length, and displays the result in a panel.

**What this tests**:
- Working with geometry parameters
- Using analysis components
- Displaying computed results
- Understanding data flow

**Verification**: The panel should show a meaningful length value when a curve is provided.

### Scenario C: Organized Definition with Groups

**Challenge**: Build a simple definition (your choice of geometry), then organize it into logical groups—inputs in one group, processing in another, outputs in a third. Use different colors for each group.

**What this tests**:
- Building complete definitions
- Using groups for organization
- Multi-step workflows

**Verification**: Capture the canvas. The groups should be visually distinct and logically organized.

### Scenario D: Bake and Render

**Challenge**: Create simple geometry in Grasshopper (a box or sphere), bake it to Rhino, set up a camera view, and capture a rendered image.

**What this tests**:
- Full Grasshopper-to-Rhino pipeline
- Baking workflow
- Viewport and camera control
- Image capture

**Verification**: The captured image should show the geometry from the specified camera angle.

### Scenario E: Debug a Broken Definition

**Challenge**: Create a definition with an intentional error (a disconnected required input, or a type mismatch). Then use inspection tools to identify the problem.

**What this tests**:
- Error detection capabilities
- Diagnostic tools
- Understanding error states

**Verification**: You should be able to identify exactly which component has the problem and why.

### Scenario F: Complex Geometry Pattern

**Challenge**: Create an array of geometry (linear or radial) where each copy is transformed based on its position in the array.

**What this tests**:
- Data tree understanding (or avoidance)
- Transformation components
- Series and list operations
- More complex data flow

**Verification**: The geometry should show clear variation across the array.

### Success Criteria

- [ ] At least 3 scenarios completed successfully
- [ ] Canvas and viewport captures show correct results
- [ ] You can articulate what worked and what was challenging

---

## Part 5: Error Handling and Edge Cases

**Goal**: Verify the system handles problems gracefully.

### Things to Try

- **Invalid component names**: What happens when you try to add a component that doesn't exist?
- **Invalid IDs**: What happens when you reference a component that doesn't exist?
- **Type mismatches**: What happens when you try an invalid connection?
- **Out-of-range values**: What happens when you set a slider value outside its range?
- **Empty operations**: What happens when you try to bulk-connect with an empty list?
- **Protected components**: What happens when you try to modify Cordyceps infrastructure?

### What to Look For

- Error messages should be informative
- Invalid operations should fail gracefully (not crash)
- The system should remain usable after errors

---

## Test Summary Template

After completing your testing, summarize your findings:

```
## Test Summary

**Date**: [date]
**Sections Completed**: [1, 2, 3, 4, 5]

### What Works Well
- [List capabilities that worked reliably]

### Issues Found
- [Describe any problems, with reproduction steps if possible]

### Suggestions
- [Ideas for improvement based on your testing experience]

### Overall Assessment
[READY / NEEDS WORK / SIGNIFICANT ISSUES]

[Brief narrative about your testing experience and confidence level]
```

---

## Tips for Effective Testing

1. **Use help liberally**: `action='help'` on any tool shows all available actions and parameters

2. **Disable solver for bulk operations**: When adding multiple components or connections, disable the solver first, then re-enable

3. **Verify visually**: Capture the canvas and viewport to confirm things look correct, not just that operations succeeded

4. **Test the happy path first**: Get basic operations working before exploring edge cases

5. **Document as you go**: Note what you tried and what happened—this helps identify patterns

6. **Be creative**: The scenarios in Part 4 are suggestions. If you think of better tests for your use case, try them!
