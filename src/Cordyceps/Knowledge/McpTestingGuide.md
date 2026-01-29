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

1. **Get Categories**
   - Call `get_categories()` with no parameters
   - Verify response includes: category names, component counts, isBuiltIn flags
   - Check that plugin info is returned for known plugins (e.g., Kangaroo2)

2. **Search for Common Components**
   - Search for "Circle" - should find multiple circle-related components
   - Search for "Panel" - should find the Panel component
   - Search for "Slider" - should find Number Slider
   - Search for "Addition" - should find the math addition component

3. **Search with Category Filter**
   - Search for "circle" with `category="Curve"`
   - Verify only Curve category components are returned
   - Verify response includes `filters` object with category value

4. **Search with Limit**
   - Search for "circle" with `limit=3`
   - Verify at most 3 results returned
   - Verify response includes `totalMatches` count (may be > 3)

5. **Get Component Documentation**
   - Request documentation for "Circle" component
   - Verify you receive: inputs, outputs, descriptions, category
   - Request documentation for "C# Script" component
   - Verify script components show their special parameters

6. **Get Component Parameters**
   - Request parameter info for a component type before adding it
   - Verify input/output names, types, and optionality are clear

7. **Search for Non-Existent Component**
   - Search for "XyzNotARealComponent123"
   - Verify you receive an appropriate "not found" response

### Expected Behaviors
- Search should be case-insensitive
- Partial matches should work
- Results should include category information for disambiguation
- Category filters should be case-insensitive
- Invalid category filters should return empty results, not errors

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

10. **Bulk Delete Components**
    - Create 3 test components
    - Use `bulk_delete_components` to delete all at once
    - Verify all were removed
    - Test partial failure: include one invalid ID in the array
    - Verify valid components are still deleted, invalid ID returns error in results

11. **Get All Components with Filters**
    - Add components from different categories (e.g., Params, Curve, Kangaroo2)
    - Test `get_all_components(category="Curve")` - should only return Curve components
    - Test `get_all_components(type="slider")` - should only return sliders
    - Test `get_all_components(category="NonExistent")` - should return empty, not error
    - Create a group and test `get_all_components(group="GroupName")`
    - Test combined filters: `get_all_components(category="Params", type="slider")`

12. **Get Canvas Status with Filter**
    - Add components from multiple categories
    - Test `get_canvas_status(category="Curve")` - should only show Curve component status
    - Verify summary counts only reflect filtered components

### Expected Behaviors
- Components should appear at specified positions
- Component IDs should be valid GUIDs
- Deleted components should disappear immediately
- Layout validation should detect overlapping components
- Filter parameters should be case-insensitive
- Invalid filters should return empty results gracefully

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

3. **Get Connections with Filter**
   - Test `get_connections(componentId="<circle-id>")`
   - Verify only connections involving that component are returned
   - Verify response includes `filteredBy` field

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
   - Set a Number Slider to value 5.0 using: `set_component_value(id, value="5")`
   - Verify the value was set (check component outputs if possible)
   - Note: This only sets the current value within the existing range

2. **Configure Slider Range and Type**
   - Configure a slider's full properties using `set_slider_properties`:
     `set_slider_properties(id, min=0, max=10, value=5)`
   - Verify minimum, maximum, and current value all changed
   - Test integer detection: use whole numbers (min=0, max=100, value=50)
     and verify the slider becomes an integer slider
   - Test explicit integer flag: `set_slider_properties(id, min=0.0, max=1.0, value=0.5, integer=false)`
     to force floating-point even with whole-number-looking values

3. **Set Panel Value**
   - Set a Panel's text to "Hello Grasshopper"
   - Verify the text appears

4. **Set Component Preview**
   - Set preview to false (hidden) for a component
   - Set preview to true (visible)
   - Verify the state changes

5. **Set Component Enabled**
   - Set enabled to false (disable/lock a component)
   - Set enabled to true (re-enable it)
   - Verify the state changes

6. **Bulk Set Preview**
   - Create 3+ components
   - Use `bulk_set_preview` to hide all at once
   - Verify all components have preview disabled
   - Use `bulk_set_preview` to show all at once

7. **Bulk Set Enabled**
   - Use `bulk_set_enabled` to disable multiple components
   - Verify all are locked
   - Re-enable them in bulk

8. **Configure Value List**
   - Add a Value List component
   - Configure it with named options: [{name: "Option A", value: "0"}, {name: "Option B", value: "1"}]
   - Select a specific option
   - Verify configuration

### Expected Behaviors
- Slider values should clamp to min/max range
- Invalid value formats should return clear errors
- State changes should take effect immediately
- Bulk operations should report per-component success/failure
- Bulk operations with partial failures should still process valid items

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

5. **Undo Protection (Fresh Session)**
   - If testing on a fresh session with no prior MCP operations, call `undo()`
   - Verify it returns an error: "Undo is not available until after MCP operations have been performed"
   - This prevents undoing the creation of the Cordyceps component itself

6. **Undo/Redo Basic Operations**
   - Add a component (note: this creates an undoable action)
   - Call `undo()` - verify it succeeds and returns undo/redo counts
   - Verify the component is no longer on the canvas
   - Call `redo()` - verify the component reappears
   - Call `redo()` again - verify it returns "Nothing to redo"

7. **Undo After Multiple Operations**
   - Add a slider, add a circle, connect them
   - Undo once - connection should be removed
   - Undo again - circle should be removed
   - Redo twice - both should be restored
   - Verify final state matches original

### Expected Behaviors
- Clear should NEVER remove Cordyceps infrastructure
- Save should work with .gh and .ghx extensions
- Solver disable should prevent computation
- Undo is blocked until at least one MCP operation has been performed
- Undo/redo counts should reflect available actions

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

### Scenario D: Radial Sine Wave Cylinder Array (Advanced)

**Goal:** Build a complex parametric model that tests multiple features working together.

**Description:** Create an array of cylinders arranged in a circle around the origin on the XY plane. The heights of the cylinders vary according to a sine wave pattern, creating a wave-like effect around the circle.

**Parameters to expose as sliders:**
- Cylinder Count (integer, 6 to 36, default 12)
- Array Radius (distance from origin, 5 to 50, default 20)
- Cylinder Diameter (0.5 to 5, default 1)
- Min Height (1 to 10, default 2)
- Max Height (5 to 30, default 10)
- Wave Frequency (number of complete waves around circle, 1 to 6, default 2)

**Construction Steps:**

1. **Setup**
   - Clear the document
   - Disable the solver
   - Read `gh://docs/geometry-orientation` (cylinders extend along plane Z-axis)

2. **Create Parameter Sliders** (stack vertically at x=50)
   - "Count" slider: integer, min=6, max=36, default=12
   - "ArrayRadius" slider: min=5, max=50, default=20
   - "CylDiameter" slider: min=0.5, max=5, default=1
   - "MinHeight" slider: min=1, max=10, default=2
   - "MaxHeight" slider: min=5, max=30, default=10
   - "WaveFreq" slider: integer, min=1, max=6, default=2

3. **Build Radial Positions**
   - Add Range component: 0 to 2*Pi, divided by Count
   - Add Circle component at origin with ArrayRadius
   - Use Divide Curve or evaluate circle at parameter values to get points
   - These points define where cylinders will be placed

4. **Calculate Sine Wave Heights**
   - Multiply the range values (angles) by WaveFreq
   - Apply Sin function to get values between -1 and 1
   - Remap from [-1,1] to [MinHeight, MaxHeight]
   - This gives varying heights around the circle

5. **Create Cylinder Axes**
   - At each point, create a vertical line (direction = Z-axis)
   - Line length = calculated height for that position
   - Use Plane Normal or similar to ensure cylinders point up (Z direction)

6. **Generate Cylinders**
   - Use Pipe or Cylinder component
   - Connect the lines as axes
   - Connect CylDiameter/2 as radius
   - Cap the cylinders if using Pipe

7. **Organize and Validate**
   - Create groups: "Parameters", "Position Logic", "Height Logic", "Geometry"
   - Enable solver
   - Check canvas status - all should be OK
   - Validate layout for overlaps

8. **Capture Results**
   - Capture the canvas to see the definition structure
   - Capture the Perspective viewport to see the geometry
   - Try adjusting sliders and re-capturing to verify parametric control

**Validation Checklist:**
- [ ] Changing Count updates number of cylinders
- [ ] Changing ArrayRadius moves cylinders closer/further from origin
- [ ] Changing CylDiameter affects cylinder thickness
- [ ] Changing MinHeight/MaxHeight affects the height range
- [ ] Changing WaveFreq changes the number of "peaks" around the circle
- [ ] All cylinders point upward (not sideways)
- [ ] Canvas has no errors or warnings
- [ ] Definition is organized with logical groups

**What This Tests:**
- Multiple slider types (integer and float)
- Configure slider range with `set_slider_properties` tool
- Mathematical operations (Range, Sin, Remap)
- Geometric operations (Circle, Points, Lines, Cylinders)
- Data tree matching (multiple cylinders from lists)
- Plane orientation (cylinders pointing correct direction)
- Groups for organization
- Capture tools for visualization
- Complex wiring with bulk_connect

### Scenario E: Studio Product Shot (Rendering Pipeline)

**Goal:** Create geometry, bake it, apply materials, set up studio lighting, and capture a high-quality render.

**What This Tests:**
- Grasshopper geometry creation
- Baking to Rhino with layers
- PBR material creation and application
- Render settings (background, ground plane)
- Sun and skylight configuration
- Display mode control
- Raytraced capture with wait

**Steps:**

1. **Create Geometry in Grasshopper**
   - Add a Box component with sliders for dimensions (e.g., 10x10x5)
   - Add a Sphere component offset above the box
   - Verify geometry appears in Grasshopper preview

2. **Bake to Rhino with Layers**
   - `bake_geometry(boxId, layer="Objects", name="Pedestal")`
   - `bake_geometry(sphereId, layer="Objects", name="Product")`
   - Verify both objects appear in Rhino

3. **Create and Apply Materials**
   - `rhino_create_material(name="Concrete", color="#808080", roughness=0.9)`
   - `rhino_create_material(name="Chrome", color="#E0E0E0", roughness=0.1, metallic=1.0)`
   - `rhino_get_objects(layer="Objects")` to get object IDs
   - `rhino_apply_material(objectIds=[pedestalId], material="Concrete")`
   - `rhino_apply_material(objectIds=[productId], material="Chrome")`

4. **Set Up Studio Background**
   - `rhino_set_render_settings(style="gradient", colorTop="#E8E8E8", colorBottom="#A0A0A0")`
   - Or create an environment: `rhino_create_environment(name="Studio", color="#D0D0D0")`
   - `rhino_set_current_environment(environment="Studio", usage="all")`
   - `rhino_set_render_settings(style="environment")`

5. **Configure Ground Plane**
   - `rhino_set_ground_plane(enabled="true", altitude="0", shadowOnly="true")`
   - This creates invisible floor that catches shadows

6. **Set Up Lighting**
   - `rhino_set_sun(enabled="true", azimuth="135", altitude="35", intensity="1.2")`
   - `rhino_set_skylight(enabled="true", shadowIntensity="0.5")`

7. **Position Camera**
   - `rhino_get_camera()` to see current position
   - `rhino_set_camera(location="30,30,20", target="0,0,5", lens="50")`
   - `rhino_zoom_extents()` if needed

8. **Capture Final Render**
   - `rhino_set_display_mode(mode="Raytraced")`
   - `rhino_wait_for_render(minPasses=200, timeout=60)`
   - `capture_viewport(outputPath="studio_shot.png", width=1920, height=1080)`

9. **Cleanup**
   - `rhino_delete_objects(objectIds)` to remove baked geometry
   - `rhino_delete_material(name="Concrete")`
   - `rhino_delete_material(name="Chrome")`

**Validation Checklist:**
- [ ] Both objects baked to correct layer
- [ ] Materials visibly different (matte vs reflective)
- [ ] Background is studio gradient or environment
- [ ] Shadows visible on ground plane
- [ ] Sun creates directional shadows
- [ ] Skylight provides fill illumination
- [ ] Final render is high quality (200+ passes)
- [ ] Image file created at correct resolution

---

### Scenario F: Outdoor Scene with Sun Position (Time-Based Lighting)

**Goal:** Create an outdoor scene with sun position calculated from geographic location and time.

**What This Tests:**
- Geometry creation and baking
- Layer organization
- Sun position calculation (latitude/longitude/dateTime)
- Ground plane with material
- Environment-based background
- Camera orbit pattern

**Steps:**

1. **Create Simple Architecture**
   - Create a box (building) with sliders
   - Create cylinders (columns) arranged in front
   - Bake all to layer "Building"

2. **Create Ground**
   - Create a large, flat box or surface for terrain
   - Bake to layer "Terrain"

3. **Apply Materials**
   - `rhino_create_material(name="Stone", color="#C0B0A0", roughness=0.7)`
   - `rhino_create_material(name="Grass", color="#3A5F0B", roughness=0.95)`
   - Apply Stone to Building layer objects
   - Apply Grass to Terrain layer objects

4. **Set Geographic Sun Position**
   - Morning light (dramatic shadows):
     `rhino_set_sun(enabled="true", latitude="51.5074", longitude="-0.1278", dateTime="2024-06-21T08:00:00", intensity="1.0")`
   - This sets sun position for London at 8am on summer solstice

5. **Configure Environment**
   - `rhino_set_render_settings(style="environment")`
   - Enable skylight: `rhino_set_skylight(enabled="true")`

6. **Set Ground Plane**
   - `rhino_set_ground_plane(enabled="true", autoAltitude="true", material="Grass")`

7. **Capture Time-Lapse Frames** (Optional Advanced)
   - Loop through times of day:
     - 6:00 (sunrise)
     - 9:00 (morning)
     - 12:00 (noon)
     - 15:00 (afternoon)
     - 18:00 (evening)
   - For each time:
     - `rhino_set_sun(dateTime="2024-06-21T{time}:00")`
     - `rhino_wait_for_render(minPasses=100)`
     - `capture_viewport(outputPath="frame_{time}.png")`

**Validation Checklist:**
- [ ] Sun position changes with dateTime
- [ ] Shadows are long in morning, short at noon
- [ ] Building casts shadows on grass
- [ ] Geographic coordinates affect sun angle
- [ ] Multiple captures show lighting progression

---

### Scenario G: Camera Orbit Animation

**Goal:** Capture multiple frames orbiting around an object to create an animation.

**What This Tests:**
- Camera position calculation (LLM computes orbit positions)
- Sequential capture workflow
- Consistent render quality across frames

**Steps:**

1. **Create Subject Geometry**
   - Create interesting geometry (e.g., twisted tower, sculptural form)
   - Bake to Rhino

2. **Apply Materials**
   - Create and apply an interesting material

3. **Set Up Scene**
   - `rhino_set_render_settings(style="gradient", colorTop="#1a1a2e", colorBottom="#16213e")`
   - `rhino_set_ground_plane(enabled="true", shadowOnly="true")`
   - `rhino_set_skylight(enabled="true")`

4. **Get Initial Camera Info**
   - `rhino_get_camera()` to get target point and distance
   - Note the distance from camera to target

5. **Calculate Orbit Positions**
   - Target: (0, 0, 5) - center of object
   - Distance: 50 - from get_camera
   - Height: 20 - constant Z
   - For 36 frames (10° each):
     - angle = frame_index * 10 * (π/180)
     - x = target.x + distance * cos(angle)
     - y = target.y + distance * sin(angle)
     - z = height

6. **Capture Orbit Frames**
   - `rhino_set_display_mode(mode="Rendered")` (faster than Raytraced for many frames)
   - For each frame:
     - `rhino_set_camera(location="{x},{y},{z}", target="0,0,5")`
     - `capture_viewport(outputPath="orbit_{frame:03d}.png")`

7. **Optional: Assemble Animation**
   - Note: Animation assembly (ffmpeg) is outside MCP scope
   - Files can be combined externally: `ffmpeg -framerate 10 -i orbit_%03d.png -loop 0 orbit.gif`

**Validation Checklist:**
- [ ] Camera moves smoothly around object
- [ ] Target remains constant (object stays centered)
- [ ] All frames captured at consistent quality
- [ ] Distance from object remains constant

---

### Expected Behaviors
- Complex definitions should build without issues
- Groups help organize large definitions
- Debugging should identify root causes
- Parametric models should respond to slider changes
- Rendering pipeline tools work together seamlessly
- Camera orbit positions are calculated correctly by LLM

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
   - Use `set_slider_properties` with min > max (e.g., min=10, max=5, value=7)
   - Verify error explains that min must be <= max
   - Try setting value outside range (e.g., min=0, max=10, value=20)
   - Verify error explains the constraint

5. **Invalid JSON**
   - For bulk operations, send malformed JSON
   - Verify error message identifies the JSON issue

### Expected Behaviors
- Errors should never crash the MCP server
- Error messages should explain what went wrong
- Suggestions for fixing should be provided when possible

---

## Test Section 14: Rhino Objects, Layers, and Materials

**Goal:** Verify Rhino object management, layers, and materials.

### Prerequisites
- Create geometry in Grasshopper first (e.g., simple box or sphere)
- Have at least one preview-enabled component with valid geometry

### Tests to Perform

#### Object Management

1. **Bake Geometry**
   - Use `bake_geometry(id, layer="TestLayer", name="TestObject")`
   - Verify object is baked to Rhino
   - Verify returned `layerIndex` and `layerCreated` fields

2. **Get Rhino Objects**
   - Use `rhino_get_objects()` to list all objects
   - Use `rhino_get_objects(layer="TestLayer")` to filter by layer
   - Verify baked object appears with correct name

3. **Select/Deselect Objects**
   - Use `rhino_select_objects(objectIds)` to select baked object
   - Use `rhino_deselect_all()` to clear selection
   - Verify selection state changes

4. **Set Object Layer**
   - Create a new layer with `rhino_create_layer(name="NewLayer")`
   - Move object with `rhino_set_object_layer(objectIds, layer="NewLayer")`
   - Verify object moved to new layer

5. **Hide/Show Objects**
   - Use `rhino_hide_objects(objectIds)` to hide
   - Use `rhino_show_objects(objectIds)` to show
   - Verify visibility state changes

#### Layer Management

6. **Get Layers**
   - Use `rhino_get_layers()` to list all layers
   - Verify TestLayer and NewLayer appear

7. **Create Layer**
   - Use `rhino_create_layer(name="ColoredLayer", color="#FF5500", parent="NewLayer")`
   - Verify layer created with correct properties

8. **Set Layer Properties**
   - Use `rhino_set_layer_properties(name="ColoredLayer", visible=false)`
   - Verify layer visibility changed

9. **Delete Layer**
   - Use `rhino_delete_layer(name="ColoredLayer", deleteObjects=false)`
   - Verify layer deleted but objects remain on default layer

#### Material Management

10. **Get Materials**
    - Use `rhino_get_materials()` to list document materials
    - Note current material count

11. **Create PBR Material**
    - Use `rhino_create_material(name="TestMetal", color="#808080", roughness=0.3, metallic=1.0)`
    - Use `rhino_create_material(name="TestGlass", color="#AADDFF", transparency=0.8, ior=1.5)`
    - Verify materials created

12. **Apply Material**
    - Use `rhino_apply_material(objectIds, material="TestMetal")`
    - Verify material applied to object

13. **Delete Material**
    - Use `rhino_delete_material(name="TestGlass")`
    - Verify material removed from document

### Expected Behaviors
- Object IDs are Rhino GUIDs (different from Grasshopper component IDs)
- Layer creation happens automatically when baking with nonexistent layer
- Materials are PBR-based with standard parameters
- Colors accept hex "#RRGGBB" or RGB "255,128,0" format

---

## Test Section 15: Rhino Environments and Render Settings

**Goal:** Verify render environment management and scene lighting configuration.

### Tests to Perform

#### Render Environments

1. **Get Environments**
   - Use `rhino_get_environments()` to list all environments in document
   - Note the default environment(s) available
   - Verify returned fields: id, name, typeName

2. **Get Current Environment**
   - Use `rhino_get_current_environment()`
   - Verify returns environment for each usage type: background, lighting, reflection
   - Note which environment is currently active

3. **Create Environment**
   - Use `rhino_create_environment(name="TestSky", color="#87CEEB")`
   - Verify environment created with solid color
   - Creating with same name again should return `alreadyExists: true`

4. **Set Current Environment**
   - Use `rhino_set_current_environment(environment="TestSky", usage="background")`
   - Verify only background changed
   - Use `rhino_set_current_environment(environment="TestSky", usage="all")`
   - Verify all three usages updated

5. **Delete Environment**
   - Use `rhino_delete_environment(name="TestSky")`
   - Verify environment removed from document

#### Render Settings (Background)

6. **Get Render Settings**
   - Use `rhino_get_render_settings()`
   - Verify returned: backgroundStyle, colorTop, colorBottom, transparentBackground

7. **Set Background Style - Solid**
   - Use `rhino_set_render_settings(style="solid", colorTop="#404040")`
   - Verify background is solid gray

8. **Set Background Style - Gradient**
   - Use `rhino_set_render_settings(style="gradient", colorTop="#87CEEB", colorBottom="#2F4F4F")`
   - Verify gradient from sky blue to dark slate

9. **Set Background Style - Environment**
   - Use `rhino_set_render_settings(style="environment")`
   - Verify background uses the current environment

10. **Set Transparent Background**
    - Use `rhino_set_render_settings(transparent="true")`
    - Verify transparentBackground is true (useful for compositing renders)

#### Ground Plane

11. **Get Ground Plane**
    - Use `rhino_get_ground_plane()`
    - Verify returned: enabled, altitude, showUnderside, shadowOnly, materialName

12. **Enable Ground Plane**
    - Use `rhino_set_ground_plane(enabled="true", altitude="0")`
    - Verify ground plane visible at Z=0

13. **Shadow-Only Ground Plane**
    - Use `rhino_set_ground_plane(shadowOnly="true")`
    - Verify ground plane catches shadows but is invisible

14. **Ground Plane with Material**
    - Create a material first: `rhino_create_material(name="GroundMat", color="#228B22")`
    - Use `rhino_set_ground_plane(material="GroundMat")`
    - Verify ground plane uses the material

15. **Auto Altitude**
    - Use `rhino_set_ground_plane(autoAltitude="true")`
    - Verify altitude adjusts to geometry bounding box

#### Sun

16. **Get Sun**
    - Use `rhino_get_sun()`
    - Verify returned: enabled, manualControl, azimuth, altitude, intensity, location, dateTime

17. **Enable Sun with Manual Position**
    - Use `rhino_set_sun(enabled="true", azimuth="135", altitude="45")`
    - Verify sun positioned at 135° azimuth (SE), 45° altitude
    - Verify manualControl is true

18. **Set Sun by Location and Time**
    - Use `rhino_set_sun(latitude="40.7128", longitude="-74.0060", dateTime="2024-06-21T14:00:00")`
    - Verify sun position calculated for New York at 2pm on summer solstice
    - Note: This disables manual control

19. **Set Sun Intensity**
    - Use `rhino_set_sun(intensity="1.5")`
    - Verify intensity increased (brighter shadows)

20. **Set North Direction**
    - Use `rhino_set_sun(north="45")`
    - Verify north direction rotated 45° from Y-axis

#### Skylight

21. **Get Skylight**
    - Use `rhino_get_skylight()`
    - Verify returned: enabled, shadowIntensity, customEnvironmentOn, customEnvironment

22. **Enable Skylight**
    - Use `rhino_set_skylight(enabled="true")`
    - Verify skylight provides ambient illumination

23. **Set Shadow Intensity**
    - Use `rhino_set_skylight(shadowIntensity="0.7")`
    - Verify shadow darkness adjusted

24. **Custom Skylight Environment**
    - Create an environment: `rhino_create_environment(name="SkylightEnv", color="#E0E0E0")`
    - Use `rhino_set_skylight(customEnvironmentOn="true", customEnvironment="SkylightEnv")`
    - Verify skylight uses custom environment instead of default sky

### Expected Behaviors
- Environment changes affect viewport immediately (redraw)
- Sun manual vs calculated modes are mutually exclusive
- Ground plane altitude can be manual or auto-detected
- Skylight provides soft ambient light separate from sun
- All color parameters accept hex "#RRGGBB" or RGB "255,128,0" format

---

## Test Section 16: Rhino Viewport and Capture

**Goal:** Verify viewport control, camera positioning, and image capture.

### Tests to Perform

1. **Get Display Modes**
   - Use `rhino_get_display_modes()`
   - Verify standard modes listed (Wireframe, Shaded, Rendered, Raytraced, etc.)

2. **Set Display Mode**
   - Use `rhino_set_display_mode(mode="Rendered")`
   - Verify display mode changed

3. **Get Camera**
   - Use `rhino_get_camera()`
   - Verify returned: location, target, up, lens, distance

4. **Set Camera**
   - Use `rhino_set_camera(location="50,50,30", target="0,0,0")`
   - Verify camera moved

5. **Set Camera Lens**
   - Use `rhino_set_camera(lens="35")`
   - Verify lens changed (affects field of view)

6. **Zoom Extents**
   - Use `rhino_zoom_extents()`
   - Verify all geometry fits in view

7. **Zoom Objects**
   - Use `rhino_zoom_objects(objectIds)`
   - Verify specific objects fit in view

#### Raytraced Rendering (Optional - Slower)

8. **Set Raytraced Mode**
   - Use `rhino_set_display_mode(mode="Raytraced")`
   - Verify mode changed

9. **Get Render Status**
   - Use `rhino_get_render_status()`
   - Verify returned: isRaytraced, currentPass, maxPasses, isComplete, progress

10. **Wait For Render**
    - Use `rhino_wait_for_render(minPasses=50, timeout=30)`
    - Verify waits until passes reached or timeout

#### Capture

11. **Capture Viewport (Basic)**
    - Use `capture_viewport()` with no path (uses temp file)
    - Verify file created and path returned

12. **Capture Viewport with Options**
    - Use `capture_viewport(outputPath="test.png", width=1920, height=1080)`
    - Verify image at specified resolution

13. **Capture with Render Wait**
    - In Raytraced mode: `capture_viewport(waitForRender=100, renderTimeout=30)`
    - Verify capture waits for render passes before capturing

14. **Capture with Transparency**
    - Use `capture_viewport(outputPath="test.png", transparent=true)`
    - Verify PNG has transparent background (requires transparent render setting)

15. **Get Available Views**
    - Use `get_available_views()`
    - Verify standard views listed (Perspective, Top, Front, Right, etc.)

16. **Capture Specific View**
    - Use `capture_viewport(view="Top")`
    - Verify captures from Top view, not active view

### Expected Behaviors
- Camera positions use "x,y,z" string format
- Raytraced render status only available when in Raytraced display mode
- Viewport capture includes Grasshopper preview geometry
- Transparent capture requires `rhino_set_render_settings(transparent="true")`

---

## End of Testing: Summary Instructions

**IMPORTANT:** After completing all test sections, provide a BRIEF summary. Keep it concise - no more than 20 lines.

### Required Summary Format

```
## Test Summary

**Results:** X tests run, Y passed (Z%)

**Failures:** (if any)
- [Brief description of what failed and why]

**Recommendations:** (if any)
- [Concrete suggestions to make the MCP server easier for LLMs to use]
- [Focus on friction points, confusing APIs, missing conveniences]

**Verdict:** READY / NEEDS WORK / NOT USABLE
```

### What to Include in Recommendations

Focus on things that would make the MCP server easier for an LLM to use:
- Confusing parameter names or formats
- Operations that required too many steps
- Missing convenience methods (e.g., "add a connect_slider_to_radius shortcut")
- Unclear error messages
- Documentation gaps
- Common patterns that should be built-in

### Optional: Detailed Report

If requested, or if significant issues were found, you may also generate the detailed report template below.

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
| 14 | Rhino Rendering Pipeline | PASS/PARTIAL/FAIL | X/Y | [Brief note] |

**Overall: [X/14 sections passed] [Y total tests passed]**

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
