# Bug: `gh_script(set)` drops the script component's language → "Can not determine input code language"

**Reported:** 2026-06-20
**Component area:** `Tools/Unified/GhScriptTool.cs` (`ActionSet`, `ActionConfigure`)
**Severity:** High — following the documented workflow produces a non-functional
script component that emits no geometry.

## Symptom

After adding a `Python 3 Script` component and setting its body with
`gh_script(action='set', id=..., code=...)`, the component fails at solve time
with a runtime **error** on its `out` port:

```
Can not determine input code language
```

All other outputs stay empty (`count: 1, preview: []` / `count: 0`), so no
geometry is produced. `gh_script(action='info')` reports `runtimeLevel: "Error"`,
`hasSource: true`, and the message above — i.e. the source is set, but the
component doesn't know what language it is.

## Root cause

Rhino 8's unified `ScriptComponent` (RhinoCodePluginGH) infers its language from
a leading hashbang directive on the **first line** of the source, e.g.:

```
#! python 3
```

`ActionSet` replaces the **entire** body via `scriptComp.SetSource(code)`
(`GhScriptTool.cs:164`; same call in `ActionConfigure` at `:260` / `:283`). There
is no language handling anywhere in the set/configure path — `SetSource` just
swaps the text. So if the caller's `code` does not include the `#!` directive
(because they wrote a plain Python body), the directive that the freshly-added
component started with is overwritten and the component loses its language
association → the error above.

This is easy to hit because it only manifests at solve time, not at `set` time
(`set` returns `success: true, codeSet: true`).

## Reproduction

```text
1. gh_canvas(action='add', type='Python 3 Script', x=350, y=120)        -> id
2. gh_script(action='configure', id, inputs='[...]', outputs='[...]')   -> success
3. gh_script(action='set', id, code='import Rhino.Geometry as rg\n...')  -> success
4. gh_document(action='recompute')
5. gh_inspect(action='reports', id)
   -> { param: "out", report: "Can not determine input code language" }
```

Adding `#! python 3` as the first line of the `code` in step 3 makes the error
disappear and the component runs correctly (verified live on Rhino 8 today).

## Why this bites users

cordyceps' own guidance shows Python bodies **without** the directive, so anyone
following the docs reproduces the bug:

- `Knowledge/Prompts/SetupScriptComponent.md` — "Python Template" and "Step 4: Set
  the Code" show a plain `import Rhino.Geometry as rg` body, no hashbang.
- `Knowledge/ComponentPatternsGuide.md` — "Python template" likewise has no
  hashbang.

## Current workaround (consumer side)

Prepend the directive to every Python body before `set`:

```python
#! python 3
import Rhino.Geometry as rg
...
```

(The `Puzzles` project now carries `#! python 3` as line 1 of all committed
`scripts/*.py` GH bodies.)

## Suggested fixes (in cordyceps), roughly in order of preference

1. **Preserve/auto-apply the language in `ActionSet`/`ActionConfigure`.** Before
   `SetSource(code)`, if the component is a script component and `code` does not
   already start with a recognized `#!` directive, prepend the directive that
   matches the component's current language (Python 3 / Python 2 / C#). This makes
   the documented plain-body workflow "just work" and is backward compatible
   (bodies that already include a directive are untouched).
2. **Add an explicit `language` parameter** to `gh_script(set|configure)` (e.g.
   `'python3' | 'python2' | 'csharp'`) that injects the correct directive.
3. **At minimum, fix the docs/templates** (`SetupScriptComponent.md`,
   `ComponentPatternsGuide.md`) to show `#! python 3` / `#! csharp` as line 1, and
   mention the directive requirement in the `gh_script` help text.

Option 1 is the most robust — it removes a sharp edge that, by your note, has been
recurring for a while.
