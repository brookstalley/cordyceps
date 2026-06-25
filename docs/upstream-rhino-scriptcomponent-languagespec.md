# Unified `ScriptComponent` silently loses its language on source replacement

---

## Summary

Replacing the source of the **unified "Script" component**
(`RhinoCodePluginGH.Components.ScriptComponent`, ComponentGuid
`c9b2d725-6f87-4b07-af90-bd9aefef68eb`) with a body that lacks a first-line language directive
(`#! python 3`, `// #! csharp`) leaves the component **unable to determine its language**. At the
next solution it fails with the runtime error **"Can not determine input code language"** and
emits no output. The source-setting call (`BaseScriptComponent.SetSource(string)` /
`IScriptComponent.Text`) **returns no error or warning** — the failure surfaces only at solve
time — so an automation/API caller has no synchronous signal that it just broke the component.

The dedicated **"C# Script"** component
(`RhinoCodePluginGH.Components.CSharpComponent`, ComponentGuid `b6ba1144-…`) is **not** affected:
it carries a concrete `LanguageSpec` and continues to compile through the same directive-less
`SetSource` call.

## Environment

- Rhino 8 (reproduced on 8.31, macOS; the mechanism is platform-independent — it is in the
  Grasshopper scripting plug-in, driven through the public component API).
- Grasshopper, `RhinoCodePluginGH` / `RhinoCodePlatform.GH`.
- Reproduced both through a third-party MCP bridge (Cordyceps) and, in the original report, via
  direct reflection on the component instance.

## The two component classes

Rhino 8 ships two .NET classes that both present as "a script component" in the GH UI:

| Toolbar entry | Class (`Type.FullName`) | ComponentGuid | Baseline `LanguageSpec` |
|---|---|---|---|
| **Script** (pick language on the component) | `RhinoCodePluginGH.Components.ScriptComponent` | `c9b2d725-…` | all-wildcard `*.*.*@*.*` until a language is chosen |
| **C# Script** (dedicated) | `RhinoCodePluginGH.Components.CSharpComponent` | `b6ba1144-…` | `*.*.csharp@*.*` (language slot fixed) |

Both derive from `BaseScriptComponent<,>` and implement `RhinoCodePlatform.GH.IScriptComponent`;
they share `SetSource(string)`. The difference in behavior below traces to the `ScriptComponent`
variant having no concrete language slot of its own — it infers the language from the source's
first-line directive — while `CSharpComponent` has the language baked into its `LanguageSpec`.

## Reproduction (API / automation)

1. Add a unified **Script** component (`c9b2d725-…`) to the document. Freshly added, it has no
   language: `RuntimeMessageLevel = Warning`, message *"No script to execute. Choose a language
   from component menu."* (`LanguageSpec = *.*.*@*.*`).
2. Set its source to a body **without** a directive, e.g. `a = 42` (or
   `private void RunScript(object x, ref object A){ A = x; }`), via `SetSource(...)` /
   `IScriptComponent.Text`.
   - **The call returns normally — no exception, no error, no warning.**
3. Let the document solve. The component now reports
   `RuntimeMessageLevel = Error`, message **"Can not determine input code language"**, and
   produces no output.

Observed timing: with the solver **disabled**, step 2 leaves the component showing no error
(`RuntimeMessageLevel = Blank`); the error only materializes when a solution actually runs
(i.e. it is raised in `SolveInstance`, not by `SetSource`). This is why the failure is invisible
to a caller that just sets source and checks the immediate return value.

## Expected vs. actual

- **Expected:** either (a) `SetSource` keeps/derives a usable language for a `ScriptComponent`
  that previously had one (or had a directive), or (b) the failure to determine a language is
  reported **at the point of the call**, not silently deferred to solve time.
- **Actual:** the language association is lost silently; the only signal is a solve-time runtime
  error, and the component emits nothing.

## `LanguageSpec` observations (from the original reporter, reflection-verified)

From Cordyceps issue #15 (verified by the reporter via reflection on the live instance — included
here for McNeel's benefit, attributed, not independently re-derived in this write-up):

- For a `ScriptComponent` that had a concrete spec (e.g. `C# 9.0 (mcneel.roslyn.csharp)`), the
  same source replacement wiped `IScriptComponent.LanguageSpec` to the all-wildcard `*.*.*@*.*`.
- Writing `LanguageSpec` back via reflection (e.g. to `LanguageSpec.CSharp`) **did not stick** —
  the setter ran without error but a subsequent read returned `*.*.*@*.*` again. So there is no
  API-only "restore the spec" repair path.

## Recovery (corrects the original "permanently broken" claim)

While the `LanguageSpec` *property* cannot be restored via reflection, the component **is
recoverable** through the normal source path: calling `SetSource` with a body whose **first line
is a directive** (`#! python 3` / `#! python 2` / `// #! csharp`) makes it compile again. A
component previously left in the *"Can not determine input code language"* error state returns to
healthy (`RuntimeMessageLevel = Blank`) after a directive-bearing `SetSource`. So the practical
problem is not unrecoverability — it is the **silent loss** of language on a directive-less set,
with no signal until solve time.

## Suggested fix (McNeel)

Any one of these would resolve the user-facing problem:

1. When `ScriptComponent.SetSource` receives a body with no language directive, **retain the
   component's current language** instead of resetting `LanguageSpec` to all-wildcard.
2. If a language genuinely cannot be determined, **surface it from `SetSource`** (return value or
   a runtime message set immediately) rather than only at solve time — so API callers can detect
   it.
3. Make `set IScriptComponent.LanguageSpec` actually persist, giving programmatic callers a
   reliable way to set/restore the language.

## Cordyceps mitigation (for context — not part of the McNeel ask)

Cordyceps cannot fix the Rhino-side behavior, but as of v1.4.11 it no longer hides it:

- `gh_script(set/configure)` **preserves an existing first-line directive** when the new body
  omits one (so a component whose source already carries `#! …` keeps its language across
  updates).
- When the final source still has no directive on a unified `ScriptComponent`, the tool returns a
  **`languageWarning`** telling the caller to start the body with `#! python 3` / `// #! csharp`
  (which also recovers a component already in the broken state), instead of silently reporting
  success.
