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
