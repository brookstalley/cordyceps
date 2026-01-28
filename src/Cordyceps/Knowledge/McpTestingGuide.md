# Cordyceps MCP Server Test Instructions

This document provides instructions for testing the Cordyceps MCP server for Grasshopper. It is written for an LLM-based MCP client to follow systematically.

**Keywords:** test cordyceps, test mcp, test grasshopper, help test, validate mcp, mcp testing, grasshopper testing, cordyceps validation

## Overview

Cordyceps is an MCP server that allows programmatic control of Grasshopper, the visual programming environment for Rhino 3D. These tests verify that all features work correctly and that the infrastructure protection mechanisms prevent accidental self-destruction.

## Before You Begin

### Prerequisites
- Rhino 8 with Grasshopper open
- Cordyceps component placed on the canvas
- MCP connection established (you're reading this, so it's working)

### Test Tracking

As you perform each test section, track results in this format:

```
## Test Results: [Section Name]
- Status: PASS / FAIL / PARTIAL
- Tests Run: X
- Tests Passed: X
- Issues Found:
  - [Description of any issues]
- Improvement Suggestions:
  - [Any ideas for better behavior]
```

### Error Reporting Guidelines

When you encounter an error:
1. Note the exact operation attempted
2. Record the error message verbatim
3. Note whether the error was expected (e.g., intentional invalid input) or unexpected
4. If the error seems like a bug, describe what you expected to happen
5. Note if the error message was helpful or confusing

### Friction Tracking (Successful But Difficult Operations)

Even when operations succeed, track any friction you experienced:

```
## Friction Log Entry
- Operation: [What you were trying to do]
- Difficulty: LOW / MEDIUM / HIGH
- What Made It Hard:
  - [e.g., "Had to make 3 calls when 1 should suffice"]
  - [e.g., "Parameter name was confusing"]
  - [e.g., "Couldn't find the right component name"]
- Feature Request: [One sentence describing improvement]
  - Example: "Add a 'connect_slider_to_input' convenience method"
  - Example: "Include common aliases in component search"
  - Example: "Return suggested connections when wiring fails"
```

Track friction for:
- Operations requiring multiple steps that feel like they should be one step
- Confusing parameter names or unclear documentation
- Having to guess at correct values or formats
- Needing to call inspection tools to figure out what to do next
- Any time you wished a tool existed that doesn't

---

## Test Section 1: Basic Connectivity and Document Info

**Goal:** Verify basic communication with Grasshopper is working.

### Tests to Perform

1. **Get Document Info**
   - Request information about the current Grasshopper document
   - Verify you receive: file path (or "unsaved"), object counts, solver status
   - Note if the response is clear and useful

2. **Get Canvas Status**
   - Request the status of all components on the canvas
   - Verify you receive component statuses (OK, ERROR, WARNING, DISCONNECTED)
   - Confirm the Cordyceps component itself is NOT listed (infrastructure protection)

3. **Get All Components**
   - Request a list of all components
   - Verify the Cordyceps component and its connected panels/sliders are NOT included
   - If other components exist, verify their information is complete

4. **Get All Groups**
   - Request a list of all groups
   - Verify any "Cordyceps MCP" group is NOT listed (infrastructure protection)
   - If other groups exist, verify their information is accurate

### Expected Behaviors
- All requests should return valid JSON with a `success` field
- Infrastructure components should be completely invisible
- Error messages should be clear and actionable

---

## Test Section 2: Component Search and Discovery

**Goal:** Verify you can find and understand available Grasshopper components.

### Tests to Perform

1. **Search for Common Components**
   - Search for "Circle" - should find multiple circle-related components
   - Search for "Panel" - should find the Panel component
   - Search for "Slider" - should find Number Slider
   - Search for "Addition" - should find the math addition component

2. **Get Component Documentation**
   - Request documentation for "Circle" component
   - Verify you receive: inputs, outputs, descriptions, category
   - Request documentation for "C# Script" component
   - Verify script components show their special parameters

3. **Get Component Parameters**
   - Request parameter info for a component type before adding it
   - Verify input/output names, types, and optionality are clear

4. **Search for Non-Existent Component**
   - Search for "XyzNotARealComponent123"
   - Verify you receive an appropriate "not found" response

### Expected Behaviors
- Search should be case-insensitive
- Partial matches should work
- Results should include category information for disambiguation

---

## Test Section 3: Adding and Managing Components

**Goal:** Verify you can add, position, rename, and remove components.

### Tests to Perform

1. **Add a Number Slider**
   - Add a Number Slider at position (100, 100)
   - Verify success and note the returned component ID
   - Optionally give it a nickname like "TestSlider"

2. **Add a Circle Component**
   - Add a Circle component at position (300, 100)
   - Verify success and note the returned component ID

3. **Add a Panel**
   - Add a Panel at position (500, 100)
   - Verify success

4. **Get Component Info**
   - Request detailed info for the Circle component
   - Verify inputs (Plane, Radius) and output (Circle) are listed
   - Verify position matches where you placed it

5. **Move a Component**
   - Move the Circle component to position (300, 200)
   - Verify the new position in the response

6. **Rename a Component**
   - Rename the Circle component to "MyCircle"
   - Verify the nickname change

7. **Get Component Bounds**
   - Request bounds for a component
   - Verify you receive x, y, width, height, right, bottom

8. **Delete a Component**
   - Delete the Panel you created
   - Verify it's removed from the canvas

9. **Bulk Move Components**
   - Create 3 components and move them all at once
   - Verify all moves succeeded

### Expected Behaviors
- Components should appear at specified positions
- Component IDs should be valid GUIDs
- Deleted components should disappear immediately
- Layout validation should detect overlapping components

---

## Test Section 4: Wiring Components Together

**Goal:** Verify you can connect and disconnect component parameters.

### Tests to Perform

1. **Simple Connection**
   - Connect the Number Slider output to the Circle's Radius input
   - Verify the connection was created

2. **Get Connections**
   - Request all connections on the canvas
   - Verify your new connection appears in the list
   - Verify NO connections to/from Cordyceps infrastructure appear

3. **Validate Connection Before Making It**
   - Use validate_connection to check if Circle output can connect to a Panel
   - Verify you get compatibility information

4. **Suggest Connections**
   - Ask for connection suggestions from the Circle's output
   - Verify suggestions include compatible input types

5. **Disconnect Components**
   - Disconnect the slider from the circle
   - Verify the connection is removed

6. **Clear Component Inputs**
   - Reconnect the slider, then clear all inputs on the Circle
   - Verify all inputs are disconnected

7. **Bulk Connect**
   - Create a small network: Slider -> Circle -> Panel
   - Make multiple connections in one operation
   - Verify all connections succeeded

### Expected Behaviors
- Connections should be validated before creation
- Invalid connections should fail with helpful messages
- Connection info should include parameter names and indices

---

## Test Section 5: Setting Values

**Goal:** Verify you can set values on sliders, panels, and parameters.

### Tests to Perform

1. **Set Slider Value**
   - Set a Number Slider to value 5.0
   - Verify the value was set (check component outputs if possible)

2. **Set Slider Range**
   - Set a slider's range using format "0<5<10" (min<default<max)
   - Verify minimum, maximum, and current value all changed

3. **Set Panel Value**
   - Set a Panel's text to "Hello Grasshopper"
   - Verify the text appears

4. **Toggle Component Preview**
   - Toggle preview off for a component
   - Toggle it back on
   - Verify the state changes

5. **Toggle Component Enabled**
   - Disable a component (lock it)
   - Re-enable it
   - Verify the state changes

6. **Configure Value List**
   - Add a Value List component
   - Configure it with named options: [{name: "Option A", value: "0"}, {name: "Option B", value: "1"}]
   - Select a specific option
   - Verify configuration

### Expected Behaviors
- Slider values should clamp to min/max range
- Invalid value formats should return clear errors
- State changes should take effect immediately

---

## Test Section 6: Groups

**Goal:** Verify you can create and manage visual groups.

### Tests to Perform

1. **Create a Group**
   - Create a new group named "Test Group"
   - Optionally set a color

2. **Add Components to Group**
   - Add 2-3 components to the group
   - Verify the membership

3. **Get Components in Group**
   - Request all components in "Test Group"
   - Verify the correct components are listed

4. **Move Group**
   - Move the entire group by an offset (e.g., dx=100, dy=50)
   - Verify all member components moved together

5. **Rename Group**
   - Rename the group to "Renamed Group"
   - Verify the change

6. **Set Group Color**
   - Change the group color (e.g., to "#FF5500" or "Orange")
   - Verify the color changed

7. **Remove Components from Group**
   - Remove one component from the group
   - Verify it's no longer a member

8. **Delete Group**
   - Delete the group
   - Verify components are NOT deleted (only the group itself)

### Expected Behaviors
- Groups should visually contain their member components
- Moving a group should move all members
- Deleting a group should preserve its contents

---

## Test Section 7: Script Components

**Goal:** Verify you can work with C# and Python script components.

### Tests to Perform

1. **Add a C# Script Component**
   - Add a C# Script component to the canvas
   - Note: search for "C# Script" or use the category-qualified name

2. **Set Script Code**
   - Set simple code that outputs a value:
     ```csharp
     A = x * 2;
     ```
   - Where `x` is an input and `A` is an output

3. **Get Script Code**
   - Retrieve the code you just set
   - Verify it matches what you sent

4. **Get Script Info**
   - Request detailed script info
   - Verify you receive: source code, input parameters with types, output parameters

5. **Configure Script Component**
   - Configure inputs and outputs explicitly:
     - Input: `x` (type: double)
     - Output: `A` (type: double)
   - Set the full source code
   - Verify the configuration took effect

6. **Test Script Execution**
   - Connect a slider to the script input
   - Check if the script runs without errors (get canvas status)
   - If possible, verify output values

### Expected Behaviors
- Script code should persist after setting
- Parameter configuration should update the component's interface
- Compile errors should appear in canvas status

---

## Test Section 8: Inspection and Debugging

**Goal:** Verify debugging and inspection tools work correctly.

### Tests to Perform

1. **Get Disconnected Inputs**
   - Request all disconnected (required) inputs across the canvas
   - Verify components with missing required inputs are listed

2. **Get Components by Type**
   - Filter components by type (e.g., "Script", "Slider")
   - Verify only matching components are returned

3. **Trace Data Flow Upstream**
   - Pick a component with inputs connected
   - Trace upstream to find all source components
   - Verify the trace is accurate

4. **Trace Data Flow Downstream**
   - Trace downstream from a slider
   - Verify all recipient components are found

5. **Get Component Outputs**
   - Request output values from a component with data
   - Verify you receive preview data

6. **Get Geometry Info**
   - If you have geometry-producing components, request geometry info
   - Verify you receive bounding boxes, validity, etc.

7. **Get Debug Reports**
   - If any script components have `out` or `Report` outputs with data, verify they're captured

8. **Get/Clear Debug Log**
   - Get recent debug log entries
   - Clear the log
   - Verify it's empty after clearing

### Expected Behaviors
- Inspection tools should not modify the document
- Data previews should be truncated for large datasets
- Trace should handle circular references gracefully

---

## Test Section 9: Document Operations

**Goal:** Verify document-level operations work correctly.

### Tests to Perform

1. **Clear Document**
   - Add several test components
   - Clear the document
   - Verify: test components are removed, Cordyceps infrastructure remains
   - Check that the MCP connection still works after clearing

2. **Save Document**
   - Save the document to a test location (e.g., temp folder)
   - Verify the file was created
   - Verify the file path in document info

3. **Solver Control**
   - Disable the solver
   - Verify components don't recompute
   - Re-enable the solver
   - Trigger a manual recompute

4. **Create Snapshot**
   - Create a named snapshot of the current state
   - Make some changes
   - Revert to the snapshot
   - Verify the state was restored

### Expected Behaviors
- Clear should NEVER remove Cordyceps infrastructure
- Save should work with .gh and .ghx extensions
- Solver disable should prevent computation

---

## Test Section 10: Capture and Visualization

**Goal:** Verify canvas and viewport capture work correctly.

### Tests to Perform

1. **Capture Canvas**
   - Capture the Grasshopper canvas to an image
   - Verify the file was created
   - View the image to confirm it shows the definition

2. **Capture Viewport**
   - If there's geometry preview, capture the Rhino viewport
   - Verify the file was created
   - View the image to confirm it shows geometry

3. **Capture Canvas Region**
   - Capture a specific region by coordinates
   - Verify only that region is captured

4. **Get Available Views**
   - List available Rhino views
   - Verify standard views are listed (Perspective, Top, Front, etc.)

### Expected Behaviors
- Images should be valid PNG/JPG/BMP files
- Capture should include component names and wires
- Viewport capture should show Grasshopper preview geometry

---

## Test Section 11: Infrastructure Protection (Critical)

**Goal:** Verify you CANNOT accidentally destroy the MCP connection.

### Tests to Perform

1. **Cordyceps Invisibility**
   - Get all components - verify Cordyceps is NOT listed
   - Search for "Cordyceps" by nickname - verify it's NOT found
   - Get all connections - verify NO connections to/from Cordyceps appear
   - Get all groups - verify "Cordyceps MCP" group is NOT listed

2. **Cannot Delete Cordyceps**
   - If you somehow obtained the Cordyceps component ID, attempt to delete it
   - Verify you receive "Component not found" error
   - Verify the MCP connection still works

3. **Cannot Modify Cordyceps**
   - Attempt to move, rename, or modify the Cordyceps component
   - All operations should fail with "not found"

4. **Cannot Delete Cordyceps Group**
   - If you obtained the group ID, attempt to delete it
   - Should fail with "not found"

5. **Cannot Disconnect Cordyceps**
   - Attempt to disconnect wires from Cordyceps inputs
   - Should fail with "not found"

6. **Clear Document Preserves Infrastructure**
   - Clear the document
   - Verify you can still communicate with Cordyceps
   - Verify the infrastructure group and connections remain

### Expected Behaviors
- ALL attempts to interact with infrastructure should fail silently ("not found")
- The LLM should never see infrastructure component IDs
- Document operations should preserve infrastructure

---

## Test Section 12: Complex Scenarios

**Goal:** Verify real-world usage patterns work correctly.

### Scenario A: Build a Circle Pattern from Scratch

1. Clear the document (if desired)
2. Disable the solver (for faster construction)
3. Add components:
   - Number Slider for count (0 < 10 < 50)
   - Number Slider for radius (0 < 5 < 20)
   - Range component
   - Circle component
4. Connect:
   - Count slider -> Range (Steps)
   - Range -> Circle (Plane - may need Point component)
   - Radius slider -> Circle (Radius)
5. Enable the solver
6. Verify the canvas status shows all components OK
7. Capture the viewport to see the circles

### Scenario B: Debug a Broken Definition

1. Create a definition with an intentional error:
   - Add a Division component
   - Connect a slider set to 0 to the denominator
2. Check canvas status - should show ERROR on Division
3. Identify the error message
4. Fix the issue by changing the slider value
5. Verify the error is resolved

### Scenario C: Parametric Model with Groups

1. Create a "Parameters" group with input sliders
2. Create a "Processing" group with computational components
3. Create an "Output" group with visualization components
4. Wire everything together
5. Verify data flows through all groups
6. Save the document
7. Capture both canvas and viewport

### Expected Behaviors
- Complex definitions should build without issues
- Groups help organize large definitions
- Debugging should identify root causes

---

## Test Section 13: Error Handling

**Goal:** Verify error messages are helpful and operations fail gracefully.

### Tests to Perform

1. **Invalid Component ID**
   - Use a made-up GUID for get_component_info
   - Verify error message mentions "not found"

2. **Invalid Component Type**
   - Try to add "NotARealComponent"
   - Verify error message suggests alternatives

3. **Invalid Connection**
   - Try to connect incompatible types (e.g., Mesh output to Integer input)
   - Verify validation catches this before attempting

4. **Invalid Slider Range**
   - Set slider range with min > max (e.g., "10<5<0")
   - Verify error explains the issue

5. **Invalid JSON**
   - For bulk operations, send malformed JSON
   - Verify error message identifies the JSON issue

### Expected Behaviors
- Errors should never crash the MCP server
- Error messages should explain what went wrong
- Suggestions for fixing should be provided when possible

---

## End of Testing: Summary Instructions

**IMPORTANT:** After completing all test sections, you MUST compile a comprehensive summary. Do not skip this step.

### Step 1: Compile Section Results

For each of the 13 test sections, record:
- Section name and number
- Status: PASS (all tests worked) / PARTIAL (some issues) / FAIL (major problems)
- Count of individual tests run
- Brief notes on any issues

### Step 2: Compile Friction Log

Review all the "successful but difficult" operations you noted and:
- Group similar friction points together
- Prioritize by frequency (how often did this come up?)
- Prioritize by severity (how much did it slow you down?)
- Convert each into a concrete feature request

### Step 3: Categorize Issues

Sort all issues into:
1. **Critical (Blocks Usage):** Things that prevent basic functionality
2. **Major (Significant Pain):** Things that work but cause significant difficulty
3. **Minor (Annoyances):** Small issues that don't block anything
4. **Enhancement (Nice to Have):** Ideas for new features or improvements

### Step 4: Write Executive Summary

In 3-5 sentences, summarize:
- Overall impression of Cordyceps quality
- Most significant issues found
- Most valuable improvement opportunities
- Recommendation (ready for production / needs work / not usable)

### Step 5: Generate Final Report

Use the template below to create your final report.

---

## Test Report Template

```markdown
# Cordyceps MCP Test Report

**Date:** [Current date]
**Cordyceps Version:** [Get from get_document_info or component]
**Tester:** [Your LLM model name and version]
**Test Duration:** [Approximate time spent testing]

---

## Executive Summary

[3-5 sentences: overall quality, biggest issues, top improvements, recommendation]

---

## Test Results by Section

| # | Section | Status | Tests | Notes |
|---|---------|--------|-------|-------|
| 1 | Basic Connectivity | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 2 | Component Search | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 3 | Adding/Managing Components | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 4 | Wiring | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 5 | Setting Values | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 6 | Groups | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 7 | Script Components | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 8 | Inspection/Debugging | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 9 | Document Operations | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 10 | Capture/Visualization | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 11 | Infrastructure Protection | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 12 | Complex Scenarios | PASS/PARTIAL/FAIL | X/Y | [Brief note] |
| 13 | Error Handling | PASS/PARTIAL/FAIL | X/Y | [Brief note] |

**Overall: [X/13 sections passed] [Y total tests passed]**

---

## Critical Issues (Must Fix Before Production)

[If none, write "None found"]

1. **[Issue Title]**
   - Section: [Which test section]
   - Operation: [What you tried to do]
   - Expected: [What should happen]
   - Actual: [What happened]
   - Impact: [Why this is critical]

---

## Major Issues (Should Fix Soon)

[If none, write "None found"]

1. **[Issue Title]**
   - Section: [Which test section]
   - Description: [What's wrong]
   - Workaround: [If any]

---

## Minor Issues (Fix When Convenient)

[If none, write "None found"]

1. [Brief description]
2. [Brief description]

---

## Friction Points & Feature Requests

### High-Priority Improvements (Frequent/Painful Friction)

1. **[Feature Request Title]**
   - Problem: [What's difficult now]
   - Suggestion: [How to improve]
   - Benefit: [Why this helps]

### Medium-Priority Improvements

1. **[Feature Request Title]**
   - Problem: [What's difficult now]
   - Suggestion: [How to improve]

### Low-Priority / Nice-to-Have

1. [Brief suggestion]
2. [Brief suggestion]

---

## Documentation Feedback

- [ ] Getting started guide was clear / unclear
- [ ] Tool descriptions were helpful / confusing
- [ ] Error messages were actionable / unhelpful
- [ ] Missing documentation for: [list any gaps]

---

## Final Assessment

**Recommendation:** [READY FOR USE / NEEDS MINOR FIXES / NEEDS MAJOR WORK / NOT USABLE]

**Confidence Level:** [HIGH / MEDIUM / LOW] - based on test coverage

**Best Features:**
1. [What works really well]
2. [What works really well]

**Biggest Opportunities:**
1. [Most impactful improvement possible]
2. [Second most impactful]

---

*Report generated by [Tester] on [Date]*
```

---

## Notes for Testers

### Pacing
- Don't rush through tests
- Allow time for Grasshopper to update between operations
- If something seems wrong, try the operation again

### Clean Up
- Delete test components when done with a section
- Use clear_document to reset between major test sections
- Save important test files before clearing

### Reporting Quality Issues
Even if a test "passes", note if:
- The response was confusing
- The operation was slower than expected
- The error message could be improved
- Documentation was unclear
- A common operation requires too many steps

### Safety Reminders
- Never attempt to guess or discover infrastructure IDs
- If you accidentally find an infrastructure ID, do not attempt to use it
- The infrastructure protection exists to keep the MCP connection stable

---

## Appendix: Common Patterns

### Pattern: Disable Solver During Bulk Operations
```
1. set_solver_enabled(false)
2. Add multiple components
3. Make multiple connections
4. set_solver_enabled(true)
5. recompute_solution()
```

### Pattern: Verify Component Added Successfully
```
1. Add component, capture returned ID
2. Get component info using ID
3. Verify position and type match expectations
```

### Pattern: Safe Delete
```
1. Get component info (verify it exists and is not critical)
2. Delete component
3. Verify it's no longer in component list
```
