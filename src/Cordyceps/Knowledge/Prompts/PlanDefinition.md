# Planning a Grasshopper Definition

**Goal:** {goal}

Before building, analyze what you need following this structured approach:

## Step 1: Read Layout Guide
First, read the layout best practices:
```
resources/read: gh://docs/canvas-layout
```

## Step 2: Check for Patterns
Read the built-in pattern resources for common structures:
```
resources/read: gh://patterns/linear-array
resources/read: gh://patterns/grid-array
```
If the goal matches one of these patterns (repeated geometry along a line, or a 2D grid), follow the pattern's guidance for component choice and layout.

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
gh_document(action='solver', enabled='false')
```

Add components in left-to-right order:
1. Add all input sliders at x=50
2. Add constants below inputs
3. Add processing components in middle columns
4. Add output geometry on right
5. Wire all connections using gh_wire with connections array for bulk
6. Enable solver and verify

```
gh_document(action='solver', enabled='true')
gh_inspect(action='status')
```

## Step 8: Validate and Organize

```
gh_canvas(action='validate')
```

If there are overlaps, adjust positions with:
```
gh_canvas(action='move', id=[id], x=[new_x], y=[new_y])
```

Then organize with groups:
```
gh_canvas(action='group_create', name='Inputs', ids='[...]', color='#4CAF50')
gh_canvas(action='group_create', name='Processing', ids='[...]', color='#2196F3')
gh_canvas(action='group_create', name='Output', ids='[...]', color='#FF9800')
```

## Common Mistakes to Avoid

1. **Sliders too close:** Leave 200px for slider width
2. **Vertical cramping:** Use 70px vertical gaps
3. **Forgetting constants:** 0, 1, 2, Pi often needed
4. **Wrong component:** Check deprecation with gh_canvas(action='search')
5. **Layout not validated:** Always run gh_canvas(action='validate') after building