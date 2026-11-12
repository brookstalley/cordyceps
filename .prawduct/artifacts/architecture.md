---
artifact: architecture
version: 1
depends_on:
  - artifact: data-model
  - artifact: security-model
  - artifact: nonfunctional-requirements
last_validated: 2026-11-11
---

# Architecture

The process and component topology of Cordyceps. Where each source file lives and what it does is
in [`docs/architecture.md`](../../docs/architecture.md); the rules that bind code changes are in
`CLAUDE.md`. This file names the runtimes, the channels between them, and the rules that keep them
coherent. Per-boundary contracts are in `api-contract.md` and `boundary-patterns.md`; trust in
`security-model.md`; targets in `nonfunctional-requirements.md`.

<!-- OPTIONAL norm home. No norms are ratified yet; candidates are listed under "Norm
     candidates" at the end and become a `## Direction` section only when the owner ratifies
     them (/prawduct:doctor → Ratify Norms). -->

## Overview & Topology

Two runtimes, one machine:

```
 ┌──────────────────────────────┐          ┌──────────────────────────────────────────────┐
 │ MCP client process           │          │ Rhino 8 process (one per open Rhino)         │
 │ Claude Desktop / Claude Code │  HTTP    │  ┌──────────────────────────────┐            │
 │ Cursor / VS Code / a script  │ ───────► │  │ McpServer (HttpListener)     │ worker     │
 │                              │ POST /mcp│  │ 127.0.0.1 + localhost:26929  │ threads    │
 │                              │ ◄─────── │  └──────────────┬───────────────┘            │
 └──────────────────────────────┘  JSON    │                 │ GrasshopperContext          │
                                           │                 │ (DocumentLock, 120 s acquire)│
                                           │                 ▼ RhinoApp.InvokeAndWait      │
                                           │  ┌──────────────────────────────┐            │
                                           │  │ Rhino UI thread              │            │
                                           │  │ Grasshopper documents,       │            │
                                           │  │ RhinoDoc, solver             │            │
                                           │  └──────────────────────────────┘            │
                                           └──────────────────────────────────────────────┘
```

- **MCP client** — any MCP client, user-launched, outside our control. It reads the tool schema and
  server instructions on every `initialize`, so it holds no compiled copy of our contract.
- **Cordyceps server** — an in-process HTTP server hosted by the `Cordyceps` Grasshopper component
  (`CordycepsComponent.cs`). It lives while that component is on an open canvas: started when the
  component solves, stopped when it is removed, its port input changes, or its document closes.
  One server per port; a second component asking for a port already owned gets `Server: BLOCKED`.
- **The host** — Rhino 8 and Grasshopper, on their single UI thread. Not ours; every rule below
  exists because of how it behaves (`boundary-patterns.md` → Grasshopper / Rhino Host API).

**Why this shape.** The server must share the Rhino process to touch the live document; an
out-of-process server with RPC into Rhino was rejected for IPC complexity and no native document
access (`project-state.yaml` → `technical_decisions.architecture`, 2026-06-20). The client is
separate because MCP clients are separate applications.

## Components & Responsibilities

| Component | Purpose | Owns | Must never |
|---|---|---|---|
| `McpServer` | JSON-RPC over Streamable HTTP; tool discovery by reflection; status block on every reply | Server lifecycle state (`ServerState`), in-flight request set, command counters | Touch Grasshopper or Rhino off the UI thread; throw a tool failure across the MCP boundary as a protocol error |
| `GrasshopperContext` | The single marshal onto the UI thread, and the `Edit` entry point for canvas changes | The static `DocumentLock` serializing all document work across every server instance | Run a canvas change outside `Edit`; re-marshal from the UI thread (re-entrant calls run inline) |
| Tool classes (`Tools/Unified/*`) | Seven tools, one `action`-dispatched method each | Nothing durable; per-call state only (except the static snapshot store, below) | Mutate before every input is resolved and validated |
| `Core/Edits` | One Grasshopper undo step per call; roll back on throw | The per-call `EditTransaction` journal | Record after mutating; leave the solver paused after commit |
| `SolveCoordinator` + `SolutionWatcher` + `SolverState` | Start solves (scheduled, never `NewSolution`); observe solution events; derive liveness | Per-document solve state and the UI heartbeat | Hold the document lock while Grasshopper solves |
| `CordycepsComponent` | The human-visible handle: port and log-level inputs, status outputs | The port → server map | Start two servers on one port |

## Communication & Boundaries

| Channel | Transport | Direction | Sync | Trust |
|---|---|---|---|---|
| Client → server | HTTP/1.1 `POST /mcp`, JSON-RPC 2.0, MCP `2025-06-18` Streamable HTTP in stateless mode; JSON responses only (no SSE stream; `GET`/`DELETE /mcp` answer 405) | request/response | sync | **Trust boundary**: any local process can reach it. See `security-model.md` |
| Client → server | `GET /` or `/health`: cached status JSON | request/response | sync | same |
| Server worker → UI thread | `RhinoApp.InvokeAndWait`, behind `DocumentLock` (one holder at a time, waiters fail after 120 s with `DocumentBusyException`) | call | sync, uncancellable once started | in-process |
| Grasshopper → `SolutionWatcher` | Solution start/end events on the UI thread | push | async | in-process |
| Server → filesystem | Capture images (temp dir or caller path), `.gh` save (caller path) | write | sync | see `security-model.md` |

Contracts: the MCP surface is in `api-contract.md`; host behaviours are in `boundary-patterns.md`.

## Data Ownership & Consistency

- **The Grasshopper and Rhino documents belong to the host and the human.** Cordyceps is one of
  two writers: the human edits the same canvas at the same time. There is no locking against the
  human. The coordination rule is that every Cordyceps write happens on the UI thread (so it never
  interleaves with a human gesture mid-step) and is one undo step the human can reverse.
- **Tools act on whichever canvas tab is focused.** The `status.document` field in every reply names
  the document a call acted on, so the client can detect that the human switched tabs.
- **Read-after-write:** a reply reflects the document after the call's own change. Solved outputs
  do not: an edit schedules a solve and returns before it runs, so a client reads computed results
  only after `gh_document(action='recompute')` (which waits) or a later solve.
- **Process-lifetime state** (snapshots, debug log, solver state) is in memory and dies with Rhino.
  Ownership of each is in `data-model.md`.

## Failure Modes & Resilience

| Failure | What the rest sees | Intended |
|---|---|---|
| UI thread busy (long solve, slow call) | Later calls queue on `DocumentLock`; after 120 s a waiter fails with a busy error. `gh_inspect(action='connection')` and `/health` still answer from cached state | Degraded, never hung |
| Modal dialog open in Rhino | Same as above; `status.modal_inferred` tells the client a human must act | Hard stop that is reported, not hidden |
| A tool throws partway | The edit rolls back; reply `success:false, rolledBack:true` | Recovered |
| Rollback itself fails | `rolledBack:false` + `rollbackFailures`; Grasshopper may show its own dialog | Reported; human checks the canvas |
| Component removed / port changed mid-request | Listener closes first (port freed immediately); in-flight handlers drain for up to 2 s in the background, then detach; a late handler gets "server is shutting down" | Degraded |
| Port held by another process | Component shows the bind error and how to pick another port | Hard failure, reported |
| Rhino crashes | Everything in memory is lost (snapshots, log). The document is whatever the human last saved | Accepted; nothing is persisted by Cordyceps |

There are no retries anywhere in the server. A client that retries does so by its own choice, and
the server instructions tell it when not to (an over-budget solve is still running; an aborted one
was a human pressing Escape).

## Deployment & Version Skew

- **One deploy unit.** Server, tool schemas, server instructions, knowledge guides and prompts ship
  together in one `.gha` (Yak package and GitHub release). They cannot be at different versions.
- **The client re-reads the contract on every connect**, so skew between a client and the server
  lasts at most until the next session. What can be stale is a human-written prompt, a saved
  agent script, or the model's memory of an older surface. That skew is what `api-contract.md`'s
  versioning section is about.
- **Rollout:** the user installs a new `.gha` and restarts Rhino. Rollback means installing the
  previous version. Cordyceps writes no persisted format of its own, so a rollback leaves no data
  migration behind.

## Scaling Model

There is nothing to scale horizontally: one human, one Rhino, a few clients at most. The first
thing to fall over is **the UI thread**. Every document operation from every client is serialized
through it, and Grasshopper solves run on it too. The signal that this is near is
`status.ui = blocked` in replies and `DocumentBusyException` failures. Targets are in
`nonfunctional-requirements.md`. Deliberately not built: a request queue, priority, or more than
one in-flight document operation.

## Cross-Cutting Runtime Concerns

- **Correlation.** None beyond the JSON-RPC `id` the client sends, which is echoed back. The debug
  log records method, path and remote endpoint per request, not a correlation id
  (`observability-strategy.md` is not yet written).
- **Configuration.** Two inputs on the component: `HttpPort` (default 26929) and `DebugLevel`.
  Changing the port stops and restarts the server. No config file, no environment variables.
- **Environment parity.** Host-free logic is linked into `Cordyceps.Tests` and runs anywhere .NET 8
  does. Anything that touches the document runs only in a live Rhino 8, verified by hand
  (`boundary-patterns.md` → Test Levels).

## Decision Log

The architecture decisions are recorded with rationale and alternatives in `project-state.yaml` →
`technical_decisions.architecture`: in-process server (2026-06-20), UI-thread marshaling
(2026-06-20), bounded document lock (2026-06-21), single lifecycle state and split `Stop()`
(2026-07-02), bounded snapshot store (2026-07-02), unified action-dispatch tools (2026-06-20).
Later decisions with their own records: every canvas change is one undo step and rolls back on a
throw (`build-plan-edit-transaction.md`); solves are scheduled, never run in-line, and a waiting
caller holds no lock (`build-plan-solve-policy.md`).

## Norm candidates (not ratified)

Written down so the owner can ratify, amend or reject them. Until then they describe the code; they
do not bind it.

1. All Grasshopper and Rhino access runs on the UI thread through `GrasshopperContext`. (Already a
   preference row in `project-preferences.md` → Enforcement.)
2. Every canvas change is one undo step and rolls back whole on a failure.
3. The liveness probe (`gh_inspect connection`, `/health`) never touches the UI thread, so it
   answers when nothing else can.
4. Cordyceps persists nothing of its own between Rhino sessions.
