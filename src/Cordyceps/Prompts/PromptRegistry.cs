using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Prompts
{
    /// <summary>
    /// Registry for MCP prompts. Provides workflow templates that guide
    /// multi-step operations in Grasshopper.
    /// </summary>
    public class PromptRegistry
    {
        private static PromptRegistry _instance;
        private static readonly object _lock = new object();

        private readonly Dictionary<string, PromptDefinition> _prompts = new Dictionary<string, PromptDefinition>();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static PromptRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PromptRegistry();
                            _instance.Initialize();
                        }
                    }
                }
                return _instance;
            }
        }

        private PromptRegistry() { }

        /// <summary>
        /// Initialize with built-in prompts
        /// </summary>
        private void Initialize()
        {
            RegisterPrompt(new PromptDefinition
            {
                Name = "create_parametric_geometry",
                Description = "Guide for creating parametric geometry with input sliders",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "geometry_type", Description = "Type of geometry to create (circle, box, curve, surface)", Required = true },
                    new PromptArgument { Name = "parameter_count", Description = "Number of input parameters to create", Required = false }
                },
                Template = @"# Creating Parametric {geometry_type}

Follow these steps to create parametric geometry with adjustable inputs:

## Step 1: Disable Solver
```
set_solver_enabled(enabled: false)
```
This prevents recomputation after each step.

## Step 2: Create Input Parameters
Add Number Sliders for each parameter you need:
```
add_component(type: ""Number Slider"", x: 50, y: 50)
set_component_value(id: [slider_id], value: ""0<50<100"")
rename_component(id: [slider_id], nickname: ""Parameter1"")
```

## Step 3: Create Geometry Component
Based on your geometry type, add the appropriate component:
- Circle: `add_component(type: ""Circle"", x: 300, y: 50)`
- Box: `add_component(type: ""Box"", x: 300, y: 50)`
- Curve: `add_component(type: ""Interpolate"", x: 300, y: 50)`

## Step 4: Create Connections
Connect sliders to geometry inputs:
```
connect_components(sourceId: [slider_id], sourceParam: ""0"", targetId: [geometry_id], targetParam: ""Radius"")
```

## Step 5: Enable Solver and Verify
```
set_solver_enabled(enabled: true)
get_canvas_status()
```

Check for any errors or warnings in the status output."
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "debug_data_mismatch",
                Description = "Diagnostic workflow for data tree mismatch errors",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "component_id", Description = "ID of the component with errors", Required = false }
                },
                Template = @"# Debugging Data Tree Mismatch

Data tree mismatches are the most common source of Grasshopper errors. Follow this diagnostic workflow:

## Step 1: Identify Problem Components
```
get_canvas_status()
```
Look for components with ERROR or WARNING status.

## Step 2: Check Component Outputs
For each problem component, examine its output structure:
```
get_component_outputs(id: [component_id])
```
Note the `branchCount` and `dataCount` for each output.

## Step 3: Trace Upstream
Find what's feeding the problem component:
```
trace_data_flow(id: [component_id], direction: ""upstream"")
```

## Step 4: Compare Tree Structures
For each upstream component:
```
get_component_outputs(id: [upstream_id])
```
Compare branch counts. Mismatched branch counts cause cross-reference behavior.

## Step 5: Common Fixes

### If one input has more branches than another:
- **Graft** the simpler input to match structure
- Or **Flatten** both to ignore structure (destructive)

### If getting unexpected combinations:
- Check if you need to **Graft** an input
- Consider using **Path Mapper** for complex restructuring

### If data is in wrong order:
- Use **Flip Matrix** to swap rows/columns
- Or **Shift Path** to adjust depth

## Step 6: Verify Fix
After making changes:
```
get_canvas_status()
get_component_outputs(id: [component_id])
```"
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "setup_script_component",
                Description = "Step-by-step guide for configuring a C# or Python script component",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "language", Description = "Script language: 'csharp' or 'python'", Required = true },
                    new PromptArgument { Name = "purpose", Description = "Brief description of what the script should do", Required = false }
                },
                Template = @"# Setting Up a {language} Script Component

## Step 1: Add Script Component
```
add_component(type: ""{language} Script"", x: 200, y: 100)
```

## Step 2: Configure Inputs and Outputs

Use configure_script_component with explicit type hints:

```
configure_script_component(
    id: [script_id],
    inputs: ""[
        {{\""name\"": \""points\"", \""type\"": \""Point3d\"", \""access\"": \""list\""}},
        {{\""name\"": \""radius\"", \""type\"": \""double\"", \""access\"": \""item\""}}
    ]"",
    outputs: ""[
        {{\""name\"": \""circles\"", \""type\"": \""Circle\""}}
    ]"",
    fullSource: ""[your code here]""
)
```

### Common Type Names:
- Primitives: `int`, `double`, `bool`, `string`
- Geometry: `Point3d`, `Vector3d`, `Plane`, `Line`, `Circle`, `Curve`, `Surface`, `Brep`, `Mesh`

### Access Modes:
- `item`: Single value per iteration
- `list`: Entire list per iteration
- `tree`: Full data tree

## Step 3: Write the Script

### C# Template:
```csharp
// Inputs are available as variables matching input names
// Set outputs by assigning to output variable names

var result = new List<Circle>();
foreach(var pt in points)
{{
    var plane = new Plane(pt, Vector3d.ZAxis);
    result.Add(new Circle(plane, radius));
}}
circles = result;
```

### Python Template:
```python
import Rhino.Geometry as rg

circles = []
for pt in points:
    plane = rg.Plane(pt, rg.Vector3d.ZAxis)
    circles.append(rg.Circle(plane, radius))
```

## Step 4: Set the Code
```
set_script_code(id: [script_id], code: ""[your code]"")
```

## Step 5: Connect Inputs and Verify
```
connect_components(sourceId: [data_source], sourceParam: ""0"", targetId: [script_id], targetParam: ""points"")
get_canvas_status()
get_debug_reports()
```

## Debugging Tips:
- Check `get_debug_reports()` for script output
- Use `Report` or `out` parameter for debug messages
- Verify types with `get_component_info(id: [script_id])`"
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "optimize_canvas",
                Description = "Workflow for analyzing and optimizing Grasshopper canvas performance",
                Arguments = new List<PromptArgument>(),
                Template = @"# Optimizing Grasshopper Canvas Performance

## Step 1: Get Current Status
```
get_canvas_status()
get_all_components()
```
Count the total components and identify complex areas.

## Step 2: Identify Expensive Operations

Common performance bottlenecks:
- Boolean operations (Solid Union, Difference)
- Mesh operations on dense meshes
- Many geometry intersections
- Large data trees with mismatched structures

Look for:
- Components with large output counts
- Deep nesting of transformations
- Repeated expensive operations

## Step 3: Analyze Data Flow
For suspected slow components:
```
trace_data_flow(id: [component_id], direction: ""upstream"")
get_component_outputs(id: [component_id])
```

Check if data structures are unnecessarily complex.

## Step 4: Optimization Strategies

### Reduce Data Before Expensive Operations
- Cull unnecessary items early in the pipeline
- Simplify curves before boolean operations
- Reduce mesh density when precision isn't needed

### Use Solver Management
```
set_solver_enabled(enabled: false)
// Make multiple changes
set_solver_enabled(enabled: true)
```

### Consider Data Caching
Add Data Dam components after expensive operations to cache results.

### Simplify Tree Structures
- Flatten data when branch structure isn't needed
- Use Simplify to remove redundant path levels

## Step 5: Verify Improvements
After optimizations:
```
recompute_solution()
get_canvas_status()
```

Compare component status before and after changes."
            });

            RegisterPrompt(new PromptDefinition
            {
                Name = "plan_definition",
                Description = "Step-by-step planning workflow for building a Grasshopper definition",
                Arguments = new List<PromptArgument>
                {
                    new PromptArgument { Name = "goal", Description = "What the definition should accomplish", Required = true }
                },
                Template = @"# Planning a Grasshopper Definition

**Goal:** {goal}

Before building, analyze what you need following this structured approach:

## Step 1: Read Layout Guide
First, read the layout best practices:
```
Read resource: gh://docs/canvas-layout
```

## Step 2: Check for Patterns
Search for a matching pattern:
```
suggest_pattern(description: ""{goal}"")
```
If a pattern is found, read the pattern resource for detailed guidance.

## Step 3: Identify Inputs
What parameters should be adjustable?

| Input Type | Component | Typical Position |
|------------|-----------|------------------|
| Numeric value | Number Slider | x=50, y=50/120/190... |
| Point | Point parameter | x=50 |
| Text | Panel | x=50 |
| Boolean | Toggle | x=50 |
| Selection | Value List | x=50 |

**Layout Rule:** Stack sliders vertically with 70px gaps. Sliders need ~200px width.

## Step 4: Identify Transformations
What operations are needed?

| Operation Type | Components | Position |
|----------------|------------|----------|
| Mathematical | Add, Multiply, Division, Pi | x=250 |
| Sequences | Series, Range, Repeat | x=400 |
| Geometric | Move, Rotate, Scale, Mirror | x=550 |
| Data | Merge, Graft, Flatten, Cross Reference | x=550 |

**Layout Rule:** 150px horizontal gaps between columns.

## Step 5: Identify Outputs
What geometry is created?

| Output Type | Components |
|-------------|------------|
| Curves | Circle, Arc, Line, Polyline, Interpolate |
| Surfaces | Extrude, Loft, Revolve, Pipe |
| Solids | Box, Cylinder, Sphere, Boolean |
| Points | Construct Point, Divide Curve |

**Layout Rule:** Output components at rightmost position (x=700+).

## Step 6: Sketch Layout

```
x=50          x=250        x=400         x=550         x=700
INPUTS        MATH         SEQUENCES     TRANSFORM     OUTPUT
├─[Slider1]   ├─[Pi]       ├─[Series]    ├─[Rotate]    ├─[Geometry]
├─[Slider2]   ├─[Divide]   │             ├─[Move]      │
├─[Slider3]   │            │             │             │
│             │            │             │             │
├─[Const 0]   │            │             │             │
├─[Const 1]   │            │             │             │
└─[Const 2]   │            │             │             │
```

## Step 7: Build with Solver Disabled

```
set_solver_enabled(false)
```

Add components in left-to-right order:
1. Add all input sliders at x=50
2. Add constants below inputs
3. Add processing components in middle columns
4. Add output geometry on right
5. Wire all connections using bulk_connect
6. Enable solver and verify

```
set_solver_enabled(true)
get_canvas_status()
```

## Step 8: Validate and Organize

```
validate_layout()
```

If there are overlaps, use:
```
auto_space_components(mode: ""horizontal"", spacing: 100)
```

Then organize with groups:
```
add_to_group(componentIds: [...], groupName: ""Inputs"", color: ""#4CAF50"")
add_to_group(componentIds: [...], groupName: ""Processing"", color: ""#2196F3"")
add_to_group(componentIds: [...], groupName: ""Output"", color: ""#FF9800"")
```

## Common Mistakes to Avoid

1. **Sliders too close:** Leave 200px for slider width
2. **Vertical cramping:** Use 70px vertical gaps
3. **Forgetting constants:** 0, 1, 2, Pi often needed
4. **Wrong component:** Check deprecation with search_components
5. **Layout not validated:** Always run validate_layout() after building"
            });
        }

        /// <summary>
        /// Register a prompt
        /// </summary>
        public void RegisterPrompt(PromptDefinition prompt)
        {
            _prompts[prompt.Name] = prompt;
        }

        /// <summary>
        /// List all available prompts
        /// </summary>
        public List<object> ListPrompts()
        {
            return _prompts.Values.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                arguments = p.Arguments?.Select(a => new
                {
                    name = a.Name,
                    description = a.Description,
                    required = a.Required
                }).ToList()
            }).ToList<object>();
        }

        /// <summary>
        /// Get a prompt by name with arguments filled in
        /// </summary>
        public PromptResult GetPrompt(string name, Dictionary<string, string> arguments)
        {
            if (!_prompts.TryGetValue(name, out var prompt))
            {
                return null;
            }

            var result = prompt.Template;

            // Substitute arguments
            if (arguments != null)
            {
                foreach (var arg in arguments)
                {
                    result = result.Replace($"{{{arg.Key}}}", arg.Value);
                }
            }

            // Replace any remaining placeholders with defaults or empty
            if (prompt.Arguments != null)
            {
                foreach (var arg in prompt.Arguments)
                {
                    result = result.Replace($"{{{arg.Name}}}", arg.Name);
                }
            }

            return new PromptResult
            {
                Name = prompt.Name,
                Description = prompt.Description,
                Content = result
            };
        }
    }

    /// <summary>
    /// Definition of an MCP prompt
    /// </summary>
    public class PromptDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<PromptArgument> Arguments { get; set; }
        public string Template { get; set; }
    }

    /// <summary>
    /// Argument for a prompt
    /// </summary>
    public class PromptArgument
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
    }

    /// <summary>
    /// Result of getting a prompt
    /// </summary>
    public class PromptResult
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Content { get; set; }
    }
}
