# Build Plan — script recompile on write (issue #33)

Branch: `fix/script-recompile-on-set` (off `develop`)
Size: medium · Type: bugfix
Critic mode: cumulative (single review — one coherent surface, ~8 files)

## Problem

Reported in issue #33 against `v1.5.0-rc.1`, from a real production definition: `gh_script(action='set')`
returns `success/codeSet:true`, `gh_script(action='get')` round-trips the new source — and the
component keeps executing its previous program. No error, no runtime message. Neither a slider
change (via MCP or by hand), nor `gh_document(action='recompute')`, nor solver-off/on made the new
code take effect.

Two defects behind it, both read out of the decompiled Rhino 8.32 script-component internals
(`RhinoCodePluginGH.gha`, `RhinoCodePlatform.GH*.dll`):

1. **Nothing asks the component to rebuild.** `set` stores source (`SetSource`) and expires the
   Grasshopper solution (`ExpireSolution(false)`). Every source-changing path *inside* Rhino pairs
   the store with a code-level update or an explicit rebuild — the editor and file-load paths do
   `Script.Text = …` **and** `code.Text.Set(…)`; the language switch does `SetScript(…)` then
   `ReBuild()`; the "Discard Caches" menu item does `Context.ExpireCache()` then `Context.ReCompute()`.
   Cordyceps is the only writer that stores text and asks for nothing. Because it also never builds,
   a compile failure cannot be reported — the silent `success: true` is structural.

2. **`get` cannot see the divergence it is being used to rule out.** `TryGetSource` returns
   `Context.Script.Text` (the *stored* text). The program that compiles and runs is the `Code`
   object's text. Rhino's own accessor prefers the latter
   (`ScriptContext.GetText()` → `code.Text` when a Code exists, `Script.Text` otherwise), and the two
   are only kept in step where Rhino explicitly writes both. So a clean `get` round-trip is not
   evidence that the running program changed — exactly the false confirmation the reporter acted on.

Not a v1.5.0-rc.1 regression, contrary to the report's hypothesis: `git show v1.4.12` shows the `set`
path was behaviourally identical (same `ExpireSolution(false)`, same `SyncScriptParams`); the only
rc.1 change was `dynamic` → reflection onto the same `SetSource(String)`. The implicit rebuild that
`set` used to inherit was `SetParametersFromScript()` → `Context.OnScriptChanged()` (prepare + build),
removed in **v1.4.6** for the cluster-corruption fix — long before the reporter's 1.4.12 baseline.

## Success

- After `gh_script(action='set'|'configure')` with new code, the component's next solve runs the
  code that was just written — because the write is followed by an explicit cache-expire + rebuild
  through Rhino's own `IScriptObject` hooks, not because a solve happens to rebuild it.
- `set`/`configure` verify their own write: after the rebuild they read back the program the
  component will actually run and report whether it matches what was written. An agent no longer has
  to take `success: true` on faith — which is the reporter's core complaint.
- `gh_script(action='get')` reports the program that actually runs, and says so when the stored and
  running texts differ.
- A tester can tell rc.2 from rc.1 over MCP, which is a precondition for them re-running the
  battery against this fix at all.
- No cluster regression: nothing added calls `ExpireSolution(true)` or `NewSolution` (issue #12 /
  v1.4.6).

## Out of scope

- Reproducing the reporter's exact failure in a live Rhino. It has not been reproduced here and this
  plan does not claim to have root-caused *their* stale program — it fixes two defects that are
  provable from the Rhino source and that make the reported symptom either impossible or visible.
- Changing `SyncScriptParams` / the surgical param sync (cluster-safety constraint stands).
- Folding pre-releases into `scripts/release.sh` (backlog `REL-6H4X`); rc.2 is cut by hand per
  `docs/release-process.md`.
- Rhino 7 / GhPython paths beyond degrading gracefully when the rebuild hooks are absent.
- **Reporting compile diagnostics from the write call — descoped, not dropped.** It was in the first
  cut of this plan and is not reachable on public API: `IScriptObject.ReBuild()` → `PreBuild(kind)`
  → `Context.TryBuildCode(runContext, out _)` **discards the `Diagnosis`**, and
  `IScriptObject.HasErrors` is just "does the component have error runtime messages", which a
  rebuild never adds. The two ways to get the text — reading the `protected ScriptContext Context`
  field, or building the `Code` against a hand-made `RunContext` — mean reaching past the public
  surface or building under different settings than the host would use. Build errors keep surfacing
  where they do today: on the component at the next solve, readable via `gh_inspect(action='status')`.
  Filed to backlog; the read-back check below covers the "did my write take effect" half.

## Requirements confidence

High for the two defects, and deliberately **not** claimed for the reporter's root cause. Every
mechanism below was read out of the shipped Rhino assemblies on this machine (8.32.26160.13002),
not recalled:

- `BaseScriptComponent.SetSource(string)` → `Context.SetScript(new Grasshopper1Script(text))` —
  replaces the whole script object; the old `Code` is disposed.
- `BaseScriptComponent.TryGetSource(out string)` → `Context.Script.Text` (stored, not running).
- `ScriptContext<T>.GetText()` → `code.Text` when a Code exists — Rhino treats the Code as the
  effective program.
- `ScriptContext<T>.SetText`, `BaseScriptComponent.Read` — both write `Script.Text` *and*
  `code.Text.Set(…)`.
- `IScriptObject.Expire()` → `code.ExpireCache()`; `IScriptObject.ReBuild()` → `PreBuild(Run)` →
  `Context.TryBuildCode(...)`; both are explicit interface implementations, neither schedules or
  expires a Grasshopper solution — so both are cluster-safe.
- `Menu_DestroyAssemblyCaches` (the "Discard Caches" item) = `Context.ExpireCache()` +
  `Context.ReCompute()` — the in-product precedent for expire-then-rebuild.
- `Rhino.Runtime.Code.Code` implements `ICode.Text` explicitly as a plain `string` — readable by
  reflection without referencing any Rhino assembly.
- `McpServer.GetServerInfo` reports `Assembly.GetName().Version` → `1.5.0.0` for *both* rc.1 and
  rc.2, since `-p:Version=1.5.0-rc.2` only moves the informational version.
  `docs/release-process.md` currently promises testers can identify their build this way.

## Chunks

### Chunk 01 — Rebuild the program after writing source

`Core/ScriptProgram.cs` (new, host-free, reflection over `object` like `ScriptSourceWriter`) — one
module for reaching a script component's live program through the public `IScriptObject` interface
it implements explicitly:

- `CanRebuild(object)` — are the hooks there?
- `Rebuild(object)` — `Expire()` (drop the compile cache) then `ReBuild()` (build now). Returns
  success, an ordered probe trace, and the reason when there is no hook. Reflection dispatches
  through the *interface* `MethodInfo`, which resolves explicit implementations.
- `TryReadRunningSource(object, out string)` — `TryGetCode(out Code)` then `ICode.Text` (a plain
  `string`, implemented explicitly on `Code`). False before the component has ever built, which is
  correct: there is no running program yet.

Interfaces are matched by simple name **plus required member shape**, not by full name — same
probe-by-shape philosophy as `ScriptSourceWriter`, and it survives Rhino moving the namespace.

A component without the hooks (Rhino 7 GhPython, third-party) is **not** an error: it is reported as
`rebuilt: false` with the reason, since those recompile off their own `Code` property.

Wire into `GhScriptTool` `set` and both `configure` write paths, after the source write and before
`ExpireSolution(false)`. Then verify the write by reading the running program back:
`verified: true` when it matches what was written; when it differs, `verified: false` plus the
actual `runningSource` — **not** a failure, because Rhino legitimately rewrites the `RunScript`
signature of SDK-mode scripts during `UpdateCode`. Unreadable → the field is omitted rather than
guessed.

Acceptance: unit tests over fakes shaped like the real component (explicit interface implementation,
hook-less component, throwing hook, code-less component); `set` response carries `rebuilt` and
`verified`.

### Chunk 02 — `get` reports the running program

`get` keeps returning `source` (the stored text) unchanged, and adds `runningSource` **and**
`sourceDiverged: true` only when the running text is readable *and* differs. Unreadable → both
omitted; never claim what cannot be read. Divergence is reported as a fact about stored-vs-running,
not as an error — the SDK-mode signature rewrite makes it legitimately non-empty.

Acceptance: unit tests for readable-and-equal, readable-and-different, and unreadable; the
divergence fields appear only in the middle case.

### Chunk 03 — Testers can identify the build

`McpServer` reports `AssemblyInformationalVersion` (`1.5.0-rc.2`) as the MCP `serverInfo.version`,
falling back to the assembly version when there is no informational version. Without this, rc.1 and
rc.2 are indistinguishable over MCP and the reporter cannot confirm they are testing the fix.

Acceptance: unit test for the fallback ordering; a real `initialize` response is verified against the
built rc.2 `.gha` before the release is published.

### Chunk 04 — Documentation audit

`ActionInfo` tips for `set`/`get`/`configure`; `McpServer.GetServerInstructions()`;
`Knowledge/CommonErrorsGuide.md` (stale-program failure mode and what `rebuilt`/`sourceDiverged`
mean); `Knowledge/Prompts/SetupScriptComponent.md` if the workflow changes; `CHANGELOG.md` under
`## [Unreleased]`; `docs/release-process.md` (its "confirm which build via MCP initialize" claim
becomes true between pre-releases, not just against a shipped release).

## Status

- [ ] Chunk 01: Rebuild the program after writing source
- [ ] Chunk 02: `get` reports the running program
- [ ] Chunk 03: Testers can identify the build
- [ ] Chunk 04: Documentation audit

## Context

Cut from issue #33 on 2026-08-27. The `.gha` for `v1.5.0-rc.2` is published by hand from `develop`
after this lands (`docs/release-process.md` → "Getting a build without releasing"); rc.2 is a
pre-release only — not Yak, not `main`.
