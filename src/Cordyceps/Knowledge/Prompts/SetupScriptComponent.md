# Setting Up a {language} Script Component

## Step 1: Add Script Component
```
gh_canvas(action='add', type='{language} Script', x=200, y=100)
```

## Step 2: Configure Inputs and Outputs

Use gh_script with configure action and explicit type hints:

```
gh_script(
    action='configure',
    id=[script_id],
    inputs='[
        {"name": "points", "type": "Point3d", "access": "list"},
        {"name": "radius", "type": "double", "access": "item"}
    ]',
    outputs='[
        {"name": "circles", "type": "Circle"}
    ]',
    code='[your code here]'
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
{
    var plane = new Plane(pt, Vector3d.ZAxis);
    result.Add(new Circle(plane, radius));
}
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
gh_script(action='set', id=[script_id], code='[your code]')
```

> The script's language directive (`#! python 3`, `// #! csharp`) is preserved automatically, so `code` can be a plain body. To force a language, make the directive the first line of `code`.

> The response reports `rebuilt` (the component was rebuilt from the new source, so the next solve runs it) and `verified` (the running program was read back and matches what you wrote). A `verified:false` with a `runningSource` is expected on C# SDK-mode scripts — Rhino rewrites their `RunScript` signature as it builds. Compile errors are *not* reported here; they surface on the component, so check `gh_inspect(action='status')` in the next step.

> **Write the body as top-level statements** (`a = ...;`), not as a bare `RunScript` function definition. A script that only *defines* `RunScript` compiles cleanly and syncs its output ports from the signature, but the function is never invoked — outputs stay null with no error anywhere, and on C# even `verified:true` cannot flag it, because the stored and running text match.

## Step 5: Connect Inputs and Verify
```
gh_wire(action='connect', sourceId=[data_source], sourceParam='0', targetId=[script_id], targetParam='points')
gh_inspect(action='status')
gh_inspect(action='reports')
```

## Debugging Tips:
- Check `gh_inspect(action='reports')` for script output
- Use `Report` or `out` parameter for debug messages
- Verify types with `gh_canvas(action='info', id=[script_id])`