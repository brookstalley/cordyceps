# Build Plan — CQ-7T4P: surface proxy-introspection failure to the caller

**Branch:** `fix/proxy-params-unavailable`
**Item:** [CQ-7T4P] `effort: S · impact: S · area: code-quality · source: critic · stage: ready`
**Work size/type:** Medium / Bugfix (touches the MCP Tool/Action contract surface → build plan + Critic).
**Critic mode:** chunk (single uncommitted diff), then cumulative before any PR.

## Confidence Check

1. **Problem:** `gh_inspect(action='docs')` returns `success: true` with empty `inputs`/`outputs`
   even when the component proxy fails to instantiate. A caller can't distinguish "component
   genuinely has no params" from "introspection failed." (CQ-5J9N added the *log*; the *caller*
   still gets no signal.)
2. **Success:** When `WithProxyComponent`'s callback does not run, the `docs` response carries an
   explicit `paramsUnavailable: true` flag plus an agent-readable `note`. The success path is
   byte-for-byte unchanged (flag absent when introspection succeeds).
3. **Out of scope:** the failure logging (already shipped in CQ-5J9N), retrying instantiation, and
   fixing *why* a given proxy fails to instantiate.

## Boundary investigation (done — recorded here)

Crosses the **MCP Tool / Action Contract** (response JSON shape) and the **Embedded Documentation
Contract** (ActionInfo). Change is **additive** (a new optional field that appears only on the
failure path) — the contract's prescribed non-breaking evolution. Internal consumers traced:

- `ToolHelpers.WithProxyComponent` (`ToolHelpers.cs:618`) — `void`, logs + degrades on failure.
- Caller 1: `GhInspectTool.ActionDocs` (`GhInspectTool.cs:449`) — **the only path that surfaces
  proxy params in a tool result.** Gets the flag.
- Caller 2: `ComponentRegistry.CreateComponentMatch` (`ComponentRegistry.cs:366`) — populates
  `ComponentMatch.Inputs/Outputs`, but those reach a tool result **only** via `GhCanvasTool`'s
  disambiguation response (`GhCanvasTool.cs:423-433`), which does **not** surface params at all.
  So there is no broken result on this path; the CQ-5J9N log is the right signal. **No flag here**
  (return value intentionally ignored, with a comment). *Not a dropped requirement — the
  requirement ("caller can't tell empty-from-failed") does not apply where params are never
  surfaced.*
- Surface 3 (**found by Critic, not via `WithProxyComponent`**): `ResourceRegistry`
  `GenerateComponentDocumentation` (`gh://component/{name}`, `ResourceRegistry.cs:381-423`)
  independently instantiates a proxy via `ComponentRegistry.CreateComponent` and, on failure,
  silently omitted the `## Inputs`/`## Outputs` markdown sections — the **same empty-vs-failed
  ambiguity in markdown form**. Now **fixed**: an `else` branch emits a `## Parameters` note
  mirroring the `paramsUnavailable` signal. (My original trace keyed on `WithProxyComponent`
  callers and missed this direct-`CreateComponent` path.)

## Chunk 01 — return-value signal + ActionDocs flag + doc audit

**Changes:**
1. `ToolHelpers.WithProxyComponent` → return `bool` (`true` iff the callback ran). Keep the existing
   broad-catch waiver + log. Update the XML doc comment.
2. `GhInspectTool.ActionDocs` → capture the bool; when `false`, add `paramsUnavailable = true` and a
   `note` to the serialized response. Success path unchanged.
3. `ComponentRegistry.CreateComponentMatch` → call site compiles against the new signature; ignore
   the return with a one-line comment explaining why (params not surfaced downstream).
4. **Doc audit:** `gh_inspect` `["docs"]` ActionInfo description mentions the `paramsUnavailable`
   signal. Check `GetServerInstructions()` (docs action already listed — only update if the field
   warrants a mention; it does not change the action vocabulary). CHANGELOG entry.

**Tests / verification:**
- The wiring is **host-dependent** (`IGH_ObjectProxy`, `Instances.ComponentServer`, `DebugLog` — none
  host-free / linkable into `Cordyceps.Tests`). Per `project-preferences.md` Testing → "document-
  touching behavior is verified live in Rhino; the host cannot be exercised off the UI thread in a
  unit test." So: **no host-free unit test is possible for this path** — verify via Release build +
  targeted self-review, consistent with the documented test boundary. Flagged for Critic.
- Verify: `dotnet build -c Release` clean; existing suite still green (`dotnet test`).

**Done when:** code compiles (Release), existing tests green, doc audit complete (ActionInfo +
CHANGELOG), Critic (chunk) clean, reflection captured, build plan Status updated.

## Critic findings (chunk/final, 2026-06-21)

- **WARNING (resolved):** `ResourceRegistry.GenerateComponentDocumentation` was a third
  params-surfacing path with the same ambiguity. Fixed in-chunk (see Surface 3 above). Re-reviewed
  via `verify-resolutions`.
- **NOTE (no action):** `verify-chunk-refs` exits 1 — a chunk-title lookup miss (untracked plan +
  em-dash title), not a missing-file report. All referenced source files exist.

## Status

- [ ] Chunk 01 — return-value signal + ActionDocs flag + doc audit
  *(box stays `[ ]` per `views_enabled`; progress tracked here in prose)*

**Context:** Chunk 01 implemented across 4 source files (`ToolHelpers`, `GhInspectTool`,
`ComponentRegistry`, `ResourceRegistry`) + CHANGELOG + ActionInfo. Release build clean (0/0),
137 tests pass (evidence current), Critic WARNING resolved. Next: verify-resolutions re-review,
then ready for commit / PR when the user asks.
