---
schema: 1
type: fix
scope: gh-script-language
date: 2026-06-24
title: "gh_script(set) flags a silently-broken Script component (issue #15)"
---
### 2026-06-24: gh_script(set) flags a silently-broken Script component (issue #15)

**Why:** Setting a directive-less body on a bare unified **Script** component (Rhino 8's
`ScriptComponent`, which has no language until one is chosen) leaves it unable to compile —
it fails at solve time with "Can not determine input code language" and emits no geometry.
`gh_script(set)` returned `codeSet:true` with no signal, so the natural retry silently
re-broke it (the painful core of issue #15). The error is produced during SolveInstance, so
it can't be observed synchronously inside `set` without forcing a cluster-unsafe recompute
(confirmed live: with the solver disabled the error doesn't surface). Instead, `set` now
detects the at-risk condition statically — a unified `ScriptComponent` whose final source
(after directive preservation) still has no language directive — and returns a
`languageWarning` with the remedy. Both `set` and `configure` (which share the same
`SetSource` path) emit it. New pure helper `ScriptDirective.LanguageWarning(...)` with 11
unit tests; docs audited (CommonErrorsGuide, gh_script set/configure help, CHANGELOG). Live
investigation also showed the component is **recoverable** by setting source with a
directive, correcting issue #15's "permanently broken" claim. Reported by @anthonyesau (#15).

Legacy tags: status=merged

### 2026-06-20: Fix gh_script dropping the script language directive (GHS-7K2P)

**Why:** `gh_script(set/configure)` replaced the whole script body via `SetSource`,
stripping the Rhino 8 language directive (`#! python 3`, `// #! csharp`) when the new
body omitted it — causing "Can not determine input code language" at solve time and no
geometry, which bit anyone following the plain-body examples in the docs. The
component's existing directive is now preserved automatically (a directive in the new
code is respected as-is). New pure helper `Core/ScriptDirective.cs` with 28 unit tests;
docs audited (CommonErrorsGuide, gh_script help, templates, root CHANGELOG).

Legacy tags: chunks=01; status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
