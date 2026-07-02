# Change Log — Cordyceps

<!-- Append new entries at the top. Each entry is a ## section.
     This file is separate from project-state.yaml to reduce merge conflicts
     when multiple branches add entries simultaneously.

     # Tagged entries (enabled by default; set `views_enabled: false` in project-state.yaml to opt out)

     With views enabled (the default), add a tag-line directly under each ##
     header to mark which build-plan chunks the entry shipped and which
     release it belongs to. `prawduct-hook regen-views` uses these tags to
     regenerate three derived views:
       * build-plan `## Status` block — checkboxes flip from `status=shipped`
       * `.prawduct/release-notes.md` — sections grouped by `release=`
       * `scope_rollups:` block in project-state.yaml — grouped by `scope=`
     Untagged entries are ignored by all three views.

     Format:

         ## YYYY-MM-DD: title (vN.M.P)

         <!-- prawduct: chunks=00,01,02 | release=v1.3.18 | status=shipped | scope=v1.4 -->

         **Why:** ...

     Recognized keys:
       chunks   - comma-separated chunk IDs (zero-padded, must match
                  build-plan.md ## Status headers exactly: `Chunk 00:`)
       release  - version string (used by release-notes view, future)
       status   - shipped | in-progress | deferred
                  `shipped` means MERGED TO MAINLINE — per-chunk timing.
                  Tag chunks `status=shipped` as soon as the merge commit lands;
                  inclusion in a tagged release is tracked separately via
                  `release=vN.M.P` (set when a release entry consolidates one
                  or more shipped chunks).
       scope    - rollup identifier (e.g., v1.4)

     With `views_enabled: true`, the Status checkboxes in build-plan.md are a
     derived view. Don't hand-edit them — add/update a tagged entry here and
     run `prawduct-hook regen-views`. -->

## 2026-07-02: janitor full reliability audit — hygiene, doc contract, HIGH bugs, MEDIUM sweeps, testability

<!-- prawduct: type=maintenance | chunks=01,02,03,04,05,06 | scope=janitor-2026-07-02 | status=built -->

**Why:** The user requested a thorough audit ("quality, bugs, consistency, gaps — ultra reliable
and stable"). A six-agent survey found the dominant defect class was mutate-then-report-success
(silent-success paths in the tool layer), plus agent-facing doc drift and governance debt.

**What (branch `chore/janitor-2026-07-02`, six chunks, each Critic-gated):**
- Chunk 01 hygiene: merged-branch cleanup (11 refs), `.work-model-index.json` untrack+ignore,
  shipped-feature bug report archived + advisory resolved, stale gitflow build plan replaced,
  unused images pruned, stale sln removed.
- Chunk 02 doc contract: 14 verified drift fixes across guides/ActionInfo/server
  instructions/prompts/README/csproj message.
- Chunk 03 HIGH bugs (6): document-close server teardown; configure JSON param-wipe;
  preview/enable per-id results; layer_delete current-layer ordering; material_create PBR
  application; place_image replace ordering. New host-free `Core/ScriptParamDefs` (+12 tests).
  Host-bound halves → VRF-007.
- Chunk 04 MEDIUM GH+boundary (29 items): silent-success→structured errors, group protection,
  cluster-safe clear, bulk expire, lossless JSON-RPC id echo, binding-error contract, coercion,
  registry races, case-insensitive actions, strict bool.
- Chunk 05 MEDIUM Rhino (17 items): InvariantCulture coordinates, select safety + type='all',
  light validation, render-wait precheck, sun mode restore, error-field + notFound conventions,
  FindByLayer guards, nested-layer resolution/creation, place_image absolute path.
- Chunk 06 testability: `ParseHelpers`/`ResponseHelpers`/`McpNaming`/`PromptTemplate`/`LogBuffer`
  extracted host-free with tests (+108); PromptRegistry unfilled-placeholder bug fixed; tool-name
  contract pinned; xUnit1031 fixed async; drain-snapshot flake eliminated via deterministic seam.

**Verification:** 224 → 370 tests, all green; plugin + test builds 0 warnings; Critic per chunk
(03: clean; 04: 1 warning resolved in 06, 1 note fixed in-chunk; 05: 2 notes — one fixed
in-chunk, one closed in 06; 06: clean). Cumulative Critic at close-out. Live-Rhino halves
recorded in VRF-007; queue burn-down (VRF-001..007) remains operator work.

## 2026-06-24: gitflow + two-step release (prep/publish)

<!-- prawduct: type=tooling | scope=gitflow-release-refactor | status=merged -->

**Why:** Adopted gitflow — `develop` is now the default/integration branch and `main` is the
release surface, to be strict-protected (require PR + `build-test`, no bypass). The old
`scripts/release.sh` pushed the `Release vX.Y.Z` commit **directly to `main`**, which strict
protection rejects — breaking releases. Split it into `prep` (on develop: bump version + CHANGELOG,
build `.gha`, commit, push, open a `develop→main` PR) and `publish` (on main, after the PR merges:
build the yak package, **push only the `vX.Y.Z` tag** — branch protection guards branches not tags —
create the GitHub Release, push to yak). Also: `base_branch: develop` in project-state.yaml so the
prawduct gates/`resolve-base` diff against develop; CI runs on `develop` pushes; untracked the
gitignored-yet-tracked `dist/manifest.yml` (restamped every release). A bash release script isn't
unit-testable — syntax + branch-guard + dispatch were verified statically; the next real release is
operator-verified (VRF-006). Also disabled xUnit test parallelization
(`src/Cordyceps.Tests/AssemblyInfo.cs`) — a pre-existing flaky timing test
(`InFlightRequestsTests.Count_…`) starved its removal continuation under parallel execution on the
2-core CI runner, and `build-test` must be reliable since it becomes the required main-protection
check. Docs: `docs/release-process.md` rewritten, `CLAUDE.md` Publishing updated. Strict main
protection itself is applied as a separate step after this lands.

## 2026-06-24: slider add-params + configure wire-preservation (v1.4.12)

<!-- prawduct: type=bugfix | scope=gh-canvas-slider-add | status=merged -->

**Why:** `gh_canvas(action='add', type='slider', ...)` silently dropped min/max/value/decimals —
the `add` dispatcher forwarded only type/x/y/nickname to `ActionAdd`, so a new slider always
landed at the default 0–1 range / 0.5 value regardless of args, forcing a second `config` call.
The slider-config decision is now a pure host-free helper (`Core/SliderConfig.cs`, parsing the
value with InvariantCulture — a latent locale fix) shared by both `add` (applied after
`AddObject` when the new object is a `GH_NumberSlider`: range first, then value so it isn't
clamped to the old range) and `config` (refactored for parity). 19 unit tests incl. the
dropped-params regression; non-slider adds ignore the params. Host glue queued for operator
verification (VRF-004). (GHC-7X4B)

<!-- prawduct: type=bugfix | scope=gh-script-configure-wires -->

**Why:** `gh_script(action='configure')` unregistered every input/output param and re-registered
them from scratch (`ConfigureViaVariableParams`), silently destroying ALL wires — even on
name-unchanged params — and reported nothing lost. It now reshapes params by name via the same
LCS sync `set` already used, extracted to a pure helper (`Core/ParamSyncPlan.cs`): name-matched
params keep their connections; wires on renamed/removed params return in a `lostConnections` array
(with a `reconnectHint`) usable directly with `gh_wire(action='connect')`. `configure` is now also
a partial update — omit a side to leave it untouched, pass `[]` to clear it (previously,
configuring only `inputs` wiped all `outputs`). 10 unit tests; host glue queued for operator
verification (VRF-005). (GHS-3W9N)

## 2026-06-24: gh_script(set) flags a silently-broken Script component (issue #15)

<!-- prawduct: type=bugfix | scope=gh-script-language | status=merged -->

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

<!-- prawduct: type=bugfix | scope=gh-document-save | status=merged -->

**Why:** `gh_document(action='save')` could not overwrite an existing `.gh` (binary) file —
every repeated save returned a bare `"Failed to write file"`, breaking incremental
checkpoints and "save before mutating" safety nets. Root cause was a format-dependent
overwrite flag: the `.gh` branch passed `overwrite=false` to GH_IO's
`GH_Archive.WriteToFile`, while `.ghx` correctly passed `overwrite=true`. The save policy
is now a pure, host-free helper (`Core/GhArchiveSave.cs`) that returns
`overwrite=true, rememberPath=true` for both formats (File→Save semantics), with 15 unit
tests including a regression guard for the format-dependent overwrite. Reproduced and to be
re-verified live against the running Cordyceps MCP server. Reported by @anthonyesau (#14).

<!-- prawduct: type=bugfix | chunks=01,02,03 | scope=solidity-hardening | status=merged -->

**Why:** Stage 1 of the firsthand solidity-hardening analysis (2026-06-21) closes the
highest-severity operational hazards in the HTTP/SSE server and UI-thread marshaling.
**Chunk 01** — `GrasshopperContext.ExecuteOnUiThread` could wedge every later request forever
behind one hung UI operation (infinite-loop script, modal) with no recovery but a Rhino restart,
and would deadlock on a re-entrant UI-thread caller. New host-free `Core/DocumentLock` bounds the
mutex acquire (7 unit tests); the wait on the UI invocation is bounded; a re-entrancy guard
(`RhinoApp.InvokeRequired`) runs inline when already on the UI thread; on timeout the caller now
gets a structured `{success:false,error:"… timed out"}` (existing `FormatExceptionResult` shapes
it — no `McpServer` change). `verify-api` confirmed `InvokeAndWait` exposes no native
timeout/cancellation (notes in `api-notes-rhinocommon.md`), so the timeout bounds waiters, not the
holder. **Chunk 02** — three lifecycle defects: a failed listener bind was swallowed and returned a
silently-dead server, so the component now records an actionable `StartError` surfaced as a canvas
error + Status output (no more bare "NOT RUNNING"); request handlers were fire-and-forgotten, so
they are now tracked via host-free `Core/InFlightRequests` (8 unit tests) and drained on `Stop()`
within the shutdown budget; and the teardown race that let an in-flight handler NRE on a nulled
`_context` is closed by capturing `_context` once and returning a structured "shutting down"
result. **Chunk 03** — two remaining data races: `CommandCount`/`LastCommand` (unsynchronized
auto-properties mutated from concurrent HTTP worker threads) now route through host-free
`Core/CommandStats` (`Interlocked.Increment` + `Volatile`; 5 tests incl. a genuinely-concurrent,
mutation-verified lost-increment test), and `GhDocumentTool._snapshots` (written on the UI thread,
listed off-thread) is now a `ConcurrentDictionary`. 169 tests pass; Release build 0/0. Host-coupled
behavior is verified live in Rhino — operator queue `VRF-001/002/003` (agent has no headless
Rhino). Doc-audit: root CHANGELOG + `CommonErrorsGuide.md` ("timed out" and "shutting down" rows).

## 2026-06-21: Place raster images as PictureFrame objects — rhino_scene(place_image) (RSC-2H9K)

<!-- prawduct: type=feature | chunks=01 | scope=place-image | status=merged -->

**Why:** External feature request from the Puzzles print-and-cut generator (Chunk 06, deferred on
this): preview a cut layout *over* printed artwork by placing the image as a real Rhino object. No
prior path existed — `rhino_render material_texture` is a PBR texture, not a placed object. New
`rhino_scene(action='place_image')` places a Rhino PictureFrame at a caller-specified
origin/size/optional Z-rotation on an auto-created layer and returns the new object id;
`replace=true`+`name` makes repeated parametric calls idempotent. Foreign API
`AddPictureFrame(Plane, path, asMesh, width, height, selfIllumination, embedBitmap)` re-verified by
reflection on Rhino 8 RhinoCommon (no `ObjectAttributes` overload → layer/name set post-add). New
host-free `Core/PlaceImageValidation.cs` (path-exists + positive-dimension checks) with 12 unit
tests; the find-or-create-layer block shared with `set_layer` was extracted to one helper. Doc
audit: server instructions, `rhino_scene` ActionInfo (`place_image`), root CHANGELOG. Release build
0/0; 149 tests pass. Per project-preferences, the document-touching handler is verified live in
Rhino, not by host-free unit tests.

## 2026-06-21: Flag failed component introspection in gh_inspect docs (CQ-7T4P)

<!-- prawduct: type=bugfix | chunks=01 | scope=cq-7t4p | status=merged -->

**Why:** `gh_inspect(action='docs')` returned `success:true` with empty `inputs`/`outputs`
when a component proxy couldn't be instantiated, so callers couldn't distinguish "component
has no parameters" from "introspection failed" (CQ-5J9N had added the log but no caller
signal). `ToolHelpers.WithProxyComponent` now returns `bool` (did the callback run); on
failure `ActionDocs` adds `paramsUnavailable:true` + a `note`, success-path shape unchanged.
The cumulative Critic surfaced a third params-surfacing path — `gh://component/{name}`
(`ResourceRegistry.GenerateComponentDocumentation`), reached via a direct `CreateComponent`
that bypassed the helper — which silently omitted its markdown Inputs/Outputs sections; now
emits a `## Parameters` note instead. Doc-audit: root CHANGELOG `[Unreleased]` Fixed entry +
`gh_inspect` `docs` ActionInfo Tips. Critic `final` + `verify-resolutions` clean; 137 tests
pass. Committed `ccd8e1d` on `fix/proxy-params-unavailable`; pushed direct to main (`d1e1787`).

## 2026-06-20: Backlog batch — docs sync, test coverage, code-quality cleanup

<!-- prawduct: type=maintenance | chunks=01,02,03,04 | scope=backlog-batch-2026-06-20 | status=merged -->

**Why:** four ready backlog items addressed as one stacked PR on `fix/mcp-error-contract`.
(DOC-8M3T) `GetServerInstructions()` lagged the code by 11 live actions agents see on MCP
initialize — synced in code-dispatch order (gh_canvas `zoomable`; rhino_scene `set_color`,`bbox`;
rhino_render 4 view + 4 light actions). (TST-6W7H) the host-free `RequestValidator` +
`UnifiedToolHelpers` contract classes had zero coverage — linked into the test project with ~69
new unit cases (suite 68→137). (CQ-2X8B) duplicated proxy-instantiation unified behind
`ToolHelpers.WithProxyComponent`; dead `GrasshopperContext.ExecuteOnUiThreadAsync` removed.
(CQ-5J9N) every silent `catch` swallow in `src/Cordyceps/` now logs with context or is narrowed
to the expected exception type, and the MCP tool-boundary catch logs the full exception (type +
stack) operator-side. Internal quality + agent-facing docs; no shipped-plugin behavior change, so
no root CHANGELOG entry. Merged to main via PR #18 (squash `f9e0663`).

## 2026-06-20: Honor the MCP error contract at the server boundary (MCP-4R2K)

<!-- prawduct: type=bugfix | scope=mcp-error-contract | status=merged -->

**Why:** `McpServer.HandleToolCallAsync` hardcoded the transport `isError` flag to `false`, so
tool results carrying `{"success": false}` were reported to MCP clients as successes; and
tool-body exceptions escaped as raw JSON-RPC `-32603` protocol errors (only `GhScriptTool`
caught them), so the 7 tools behaved inconsistently. Both are now routed through a new
host-free `Core/McpResultFormatter` (`IsErrorResult` derives `isError` from the parsed
`success` field; `FormatExceptionResult` converts any tool-body throw — unwrapping
`TargetInvocationException` — into a `{success:false,error}` result with `isError:true`),
applied uniformly at the boundary. 15 new unit tests (68 total). Broad boundary catch carries a
`prawduct:allow` waiver. Committed `905825c`/`0a525d0` on `fix/mcp-error-contract`; merged to main
via PR #18 (squash `f9e0663`). Root CHANGELOG `[Unreleased]` Fixed entry + `McpTestingGuide.md`
contract line already added.

## 2026-06-20: Drop attribution trailer from release commits

<!-- prawduct: type=chore | scope=release-attribution | status=merged -->

**Why:** `scripts/release.sh` `git_commit_and_tag` hard-coded a `🤖 Generated with …` +
`Co-Authored-By: Claude …` trailer on every `Release vX.Y.Z` commit, contradicting the
project's `Commit attribution: none` preference. Removed both lines so release commits carry a
plain `Release vX.Y.Z` message. Release tooling only; no shipped-plugin change.

## 2026-06-20: Janitor maintenance pass

<!-- prawduct: type=maintenance | chunks=01,02,03 | scope=janitor-2026-06-20 | status=merged -->

**Why:** periodic `/prawduct:janitor` survey + user-approved cleanup. Fixed release-metadata
drift (tracked `manifest.yml` was stale at 1.4.0 while shipping 1.4.9) and closed the gap that
let it drift — `scripts/release.sh` now bumps the manifest version, not just the csproj. Added
the first build/test CI (`.github/workflows/dotnet-ci.yml`: `dotnet build`/`dotnet test` on
push/PR) so the 53 xUnit tests run automatically. Removed obsolescence: a 402-line unreferenced
`src/` planning doc that contradicted the HTTP+SSE implementation, the shipped GHS-7K2P bug
report, stray `output/`/`memory/` dirs, and merged/stale branches. Documented
`Core/ToolHelpers.cs` in CLAUDE.md. No compiled C# changed; build 0/0, 53/53 tests pass. Not
user-facing (dev tooling + release plumbing), so no root CHANGELOG entry.

## 2026-06-20: Wire .NET/xUnit test evidence into the Prawduct gate (TST-9Q4M)

<!-- prawduct: type=tooling | chunks=01 | scope=gate-soundness | status=merged -->

**Why:** `prawduct-hook test-evidence record` defaulted to pytest and could not run this
C#/xUnit repo, so no `.test-evidence.json` was ever produced and the freshness/Critic/PR
gates were unsound (every code chunk warned "no test evidence"). Added the
`JunitXml.TestLogger` package to `Cordyceps.Tests` and declared `test_command` in
project-state.yaml so the hook runs the real xUnit suite and records exact counts.
Verified end-to-end: `test-evidence record` → 53 passed / 0 failed @ HEAD; `test-status`
→ current. No user-facing change (dev tooling), so no root CHANGELOG entry.

## 2026-06-20: Fix gh_script dropping the script language directive (GHS-7K2P)

<!-- prawduct: type=bugfix | chunks=01 | scope=gh-script-language | status=merged -->

**Why:** `gh_script(set/configure)` replaced the whole script body via `SetSource`,
stripping the Rhino 8 language directive (`#! python 3`, `// #! csharp`) when the new
body omitted it — causing "Can not determine input code language" at solve time and no
geometry, which bit anyone following the plain-body examples in the docs. The
component's existing directive is now preserved automatically (a directive in the new
code is respected as-is). New pure helper `Core/ScriptDirective.cs` with 28 unit tests;
docs audited (CommonErrorsGuide, gh_script help, templates, root CHANGELOG).
