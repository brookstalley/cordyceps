---
schema: 1
type: fix
scope: solidity-hardening
date: 2026-06-24
title: "gh_script(set) flags a silently-broken Script component (issue #15)"
---
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

Legacy tags: chunks=01,02,03; status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
