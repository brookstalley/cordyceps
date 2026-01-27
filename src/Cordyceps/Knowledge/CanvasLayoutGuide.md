# Canvas Layout Best Practices

This guide helps you create readable, well-organized Grasshopper definitions. Following these conventions ensures components don't overlap and the canvas remains navigable.

## Component Dimensions

Understanding component sizes is essential for proper spacing.

### Input Components
| Component | Typical Width | Height | Notes |
|-----------|--------------|--------|-------|
| Number Slider | 150-250 | 20 | Width varies with range display |
| Panel | 100-200 | 50-100 | Expands with content |
| Toggle | 80 | 20 | Compact boolean input |
| Value List | 120 | 30 | Dropdown selector |
| Point Parameter | 80 | 40 | Single input/output |

### Standard Components
| Component Type | Typical Width | Height per I/O |
|----------------|--------------|----------------|
| Simple (1-2 I/O) | 80-100 | 40 |
| Medium (3-4 I/O) | 100-120 | 50-60 |
| Complex (5+ I/O) | 120-150 | 60-80 |
| Script Component | 100-140 | Varies with I/O count |

### Special Components
| Component | Width | Height | Notes |
|-----------|-------|--------|-------|
| Cluster | 80-120 | Varies | Depends on I/O count |
| Scribble | Variable | Variable | Text annotation |
| Group | +40 padding | +40 padding | Adds margin around contents |

## Spacing Conventions

### Horizontal Spacing
- **Between columns**: 150 units minimum
- **Between sliders and first component**: 200 units (sliders are wide)
- **Between tightly related components**: 100 units
- **Between groups**: 50 units

### Vertical Spacing
- **Between slider rows**: 70 units
- **Between component rows**: 70-100 units
- **Between groups**: 50 units
- **Within a vertical stack**: 60 units

## Layout Patterns

### Standard Left-to-Right Flow

```
x=50        x=250       x=400       x=550       x=700       x=850
|           |           |           |           |           |
[Slider1]   [Math]------[Transform]-[Geometry]--[Output]
[Slider2]---/           |
[Slider3]---------------/
```

### Recommended Column Positions
- **Inputs (sliders, parameters)**: x = 50
- **Constants (panels with 0, 1, 2, Pi)**: x = 50-150, below sliders
- **First processing column**: x = 250
- **Subsequent columns**: x = 400, 550, 700, 850... (150-unit increments)

### Vertical Organization
- **Primary data flow**: y = 50-150
- **Secondary branches**: y = 200-300
- **Constants and helpers**: y = 300+

## Handling Constants

Constants (0, 1, 2, π, etc.) are frequently needed. Choose the best approach:

### Option 1: Use Component Defaults (Preferred)
Many components have sensible defaults when inputs are unconnected:
- `Construct Point` → defaults to (0, 0, 0)
- `XY Plane` → defaults to world XY at origin
- `Unit X/Y/Z` → default factor is 1
- `Number` parameter → can be set directly with right-click

### Option 2: Panels for Explicit Constants
When you need visible, reusable constants:
- Create a dedicated "Constants" area below sliders (y = 250+)
- Use clear nicknames: "Zero", "One", "Two", "2Pi"
- Group related constants together

Example layout:
```
x=50, y=50:   [Count Slider]
x=50, y=120:  [Diameter Slider]
x=50, y=190:  [Offset Slider]

x=100, y=280: [Panel: 0] nicknamed "Zero"
x=100, y=330: [Panel: 1] nicknamed "One"
x=100, y=380: [Panel: 2] nicknamed "Two"
```

### Option 3: Derive from Math
For π and mathematical constants:
- Use the `Pi` component with factor input
- Panel with "2" → Pi component gives 2π

## Groups

### Group Sizing
Groups automatically expand to contain their members plus padding:
- **Padding**: ~20-30 units on each side
- **Total added size**: ~40-60 units width and height

### Group Placement Strategy
1. Add all components first with proper spacing
2. Use `get_component_bounds` to find the extent of components to group
3. Create the group - it will auto-size
4. Leave 50+ units between adjacent groups

### Recommended Group Organization
- **"Inputs"** (green): All sliders and input parameters
- **"Processing"** (blue): Mathematical and data operations
- **"Geometry"** (orange): Geometry creation and transformation
- **"Output"** (purple): Final results and visualization

## Common Layout Mistakes

### 1. Ignoring Slider Width
**Wrong**: Placing a component at x=100 next to a slider at x=50
**Right**: Place next component at x=250+ (slider needs ~200 width)

### 2. Vertical Cramping
**Wrong**: Components at y=50, y=70, y=90 (20-unit gaps)
**Right**: Components at y=50, y=120, y=190 (70-unit gaps)

### 3. Group Overlap
**Wrong**: Two groups both starting at y=50
**Right**: Check bounds after creating first group, start second group 50+ units below

### 4. Panel Expansion
**Wrong**: Assuming panel stays at initial size
**Right**: Panels grow with content - leave extra space or set fixed size

## Example: Radial Array Layout

This layout creates cylinders arranged in a circle:

```
INPUTS (x=50, green group)
├── y=50:  [Count Slider: 2<8<16]
├── y=120: [Diameter Slider: 10<50<100]
└── y=190: [Offset Slider: 0<100<200]

CONSTANTS (x=150, below inputs)
├── y=280: [Panel: 2]
├── y=340: [Panel: 0]
└── y=400: [Panel: 1]

MATH (x=300-550, blue group)
├── y=50:  [Pi] ─────────► [Division: 2π/Count] ─► [Series]
├── y=120: [Division: Diameter/2]
└── Connections from sliders and constants

GEOMETRY (x=400-900, orange group)
├── y=190: [Construct Point] ─► [Rotate 3D] ─► [XY Plane] ─► [Cylinder]
├── y=260: [Origin Point (0,0,0)]
└── y=330: [Unit Z]
```

## Workflow Summary

1. **Plan the layout** before adding components
2. **Disable solver**: `set_solver_enabled(false)`
3. **Add inputs** at x=50, stacked vertically with 70-unit gaps
4. **Add constants** below inputs if needed
5. **Add processing** starting at x=250, in 150-unit columns
6. **Add geometry** in rightmost columns
7. **Check bounds**: Use `get_component_bounds` for precise positioning
8. **Wire connections** with `bulk_connect`
9. **Enable solver**: `set_solver_enabled(true)`
10. **Validate**: `get_canvas_status()` then `validate_layout()`
11. **Group**: `add_to_group` for visual organization
12. **Final check**: Ensure groups don't overlap
