# Operator Verification Queue

<!-- Append-only queue of pre-merge human-verification items for live-integration
     changes that automated tests can't fully cover. Each entry is a level-2 heading:
     `## VRF-<id> — <Chunk N> — <title>`; first body line is `**Status:** pending | verified | accepted`.
     operator_verification_required is false, so these do not block /pr create — they are
     an honesty/handoff record of what only a running Rhino can confirm. -->

## VRF-001 — Chunk 01 — Bounded UI-thread lock (timeout + re-entrancy guard)

**Status:** pending
**Added:** 2026-06-21 (Chunk 01, solidity-hardening Stage 1)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** The host-free timeout/release logic (`Core/DocumentLock`) is unit-tested
in CI, but the UI-thread half — `RhinoApp.InvokeRequired` re-entrancy guard and `InvokeAndWait`
marshaling under the lock — cannot run without a live Rhino. The build agent has no headless Rhino,
so this was reasoned + statically reviewed, not executed.

**Verify:**
- **Normal path unaffected:** ordinary tool calls (e.g. `gh_canvas` add/move, `rhino_scene` list)
  return identical results to before, with no added latency.
- **Wedge no longer hangs the server:** configure a Python script component with an infinite loop
  (e.g. `while True: pass`) and trigger it; while it's stuck, issue another MCP request. Expected:
  the second request returns within ~120s with `{"success": false, "error": "Document is busy…"}`
  (a structured error), **not** an indefinite hang. Before this change every later request hung forever.
- **Server stays responsive after:** subsequent requests keep returning the busy error (rather than
  the connection dying); restarting Rhino clears the wedged holder.
- (Optional) Confirm no deadlock if any UI-thread-originated path calls a tool (the inline branch).

## VRF-002 — Chunk 02 — Server lifecycle (bind-failure surfacing, drain, teardown race)

**Status:** pending
**Added:** 2026-06-21 (Chunk 02, solidity-hardening Stage 1)
**Where to verify:** Rhino 8 + Grasshopper with the Cordyceps component placed and an MCP client connected.

**Why this needs a human:** The host-free drain logic (`Core/InFlightRequests`) is unit-tested in CI,
but the `HttpListener` bind path, the `Stop()` drain under live UI-thread marshaling, and the
`_context` teardown race only exist with a running Rhino. The build agent has no headless Rhino, so
these were reasoned + statically reviewed, not executed.

**Verify:**
- **Bind failure shows an actionable reason:** occupy port 26929 with a *non-Cordyceps* process
  (e.g. `python3 -m http.server 26929`), then place the Cordyceps component on that port. Expected:
  a red **error bubble** on the component and a **Status** output reading *"Server: FAILED TO START"*
  with the actionable message (port in use → choose a different port). Before this change it showed a
  bare "NOT RUNNING". Then free the port / change the component's port and confirm it starts cleanly.
- **Clean shutdown drains in-flight work:** issue a slow MCP request (e.g. a tool call that takes a
  few seconds) and, while it's running, remove the component (or change its port). Expected: the
  in-flight request completes normally if it finishes within the shutdown budget; the operator log
  (`gh_inspect(action='log')` before removal, or Rhino command line) shows **no `NullReferenceException`**.
- **Teardown race does not NRE:** repeat the above with a request that outlives the shutdown budget.
  Expected: the request returns a structured *"MCP server is shutting down; the request was not
  processed"* result (or completes), **never** an unhandled `NullReferenceException` in the worker.
- **No listener/port leak:** add and remove the component several times on the same port. Expected:
  each add starts cleanly with no "port in use" error attributable to a leaked prior listener.
