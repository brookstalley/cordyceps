---
artifact: observability-strategy
version: 1
depends_on:
  - artifact: architecture
  - artifact: security-model
  - artifact: nonfunctional-requirements
last_validated: 2026-11-11
---

# Observability Strategy

Cordyceps runs as one process on one person's machine. It has two audiences, and neither of them
is a maintainer watching a dashboard:

1. **The AI agent driving it**, which must decide its next move. Is Rhino busy? Does a human need
   to act? Which document did that call touch?
2. **The user, or a maintainer reading their bug report**, who needs to find out what went wrong
   after the fact.

The fit for that is "error logging, plus liveness state on every reply". There are no metrics or
traces, and nothing leaves the machine (`operational-spec.md` § Monitoring).

<!-- OPTIONAL norm home. No norms are ratified yet; see "Norm candidates" at the end. -->

## Signals

| Signal | What it carries | Where it lives | Read by |
|---|---|---|---|
| Status block on every tool reply | Focused document, UI-thread state, `solving` / `solving_since`, the solving document when it differs, `modal_inferred` | `Core/StatusEnvelope.cs` (compact form) | The agent, on every call, failures included |
| Health endpoint | Endpoint answered, tool count, command count, uptime, active document, full host status | `GET /health` (`McpServer.HandleHealthCheckAsync`); `gh_inspect(action='connection')` | Agents, scripts, a user with `curl` |
| Debug log | Timestamped entries at INFO / WARN / ERROR / DEBUG | `Core/DebugLog.cs` → `Core/LogBuffer.cs`, a 500-entry in-memory ring buffer | The agent through `gh_inspect(action='log')`; the user in Rhino's command history |
| Component outputs | `About` (version, build), `Status` (listening, port, command count, URL), `LastCommand` | `CordycepsComponent` | The user, on the canvas |
| Component runtime messages | Start failures with the fix, e.g. a port conflict | `CordycepsComponent`, `McpServer.Start()` | The user |
| Build identity | Informational version with build stamp, reported in MCP `initialize` | csproj `SourceRevisionId` | Testers telling two builds apart |

**Liveness is derived, not measured directly.** The health probe never goes to the UI thread, so
it keeps answering while Rhino is wedged. "UI blocked" is inferred from UI-thread occupancy,
recorded where `GrasshopperContext` marshals calls onto that thread. The probe's own requests do
not count. That inference is the most safety-relevant signal: a false `modal_inferred` tells an
agent to stop and fetch a human (`Core/SolverState.cs`; learning in `.claude/rules/learnings/core.md`).

## Log levels and what reaches the user

`DebugLevel` is an input on the component (default **0**):

| Message | Level | Buffered | Written to Rhino command history |
|---|---|---|---|
| Server started / stopped, start failure | 0 | always | always |
| Errors (`DebugLog.Error`, tool exceptions with full stack, listener and JSON-RPC errors) | 0 | always | always |
| Each request line, the first 200 characters of every request and response body, rejected origins, oversize bodies | 1 | always | only at `DebugLevel` ≥ 1 |
| Shutdown drain details, stack trace on start failure | 1 | always | only at `DebugLevel` ≥ 1 |

Every message goes into the buffer regardless of level, so `gh_inspect(action='log')` can return
detail that the command line did not show.

**Error conventions.** A tool exception is caught at the tool-invocation boundary. The full
exception, type and stack, is logged at ERROR. The client gets a message-only `{success:false}`
result with the status block attached, so the agent sees a failure and the host's state together
(`McpServer.cs`, tool-invocation catch). The broad catches at the server's start, teardown and
tool-invocation boundaries are waived inline with their reasons. The request-handling catches in
the same file log at ERROR but carry no waiver comment.

## Correlation

**None today.** Log entries carry a timestamp and level only. A JSON-RPC request has an `id`, but
the log lines for that request do not carry it, and a tool's ERROR entry names the tool, not the
call. With one user and calls serialized on one UI thread, timestamps usually suffice. They stop
sufficing when two MCP clients are connected at once, which is a supported setup.

## Sensitive data

What a log can contain: Grasshopper document names, file paths (in exception messages and stack
traces), component nicknames and values, and, at level 1, the first 200 characters of request and
response bodies, which can include script source and panel text.

Where it can go:
- **Rhino's command history**: local. Errors always appear there; bodies only at level 1.
- **The MCP client**, through `gh_inspect(action='log')`. That means **the user's AI provider**:
  log content becomes model input. The agent is already allowed to read the document, so this
  discloses nothing new to the agent. But a body logged from one client can be read back by a
  different client.
- **Bug reports**, when a user pastes their command history or log output.

There is no filtering or redaction. Given the security model (local only, the agent already has
full document access), that is accepted. The gap that matters is user awareness: nothing tells a
user that the log may hold script source before they paste it into a public issue.

## Instrumentation conventions

- Log through `Core.DebugLog`, never `RhinoApp.WriteLine` directly. Errors go through
  `DebugLog.Error` (level 0, always visible).
- A new reply surface carries the status block. `StatusEnvelope` is total: it must never break the
  payload it decorates.
- A new liveness input is recorded at the marshaling choke point, not at a call site. A probe path
  never enters the UI thread.
- A buffer that grows with use has a cap (the log ring buffer is 500 entries).

## Gaps

| Gap | Cost of leaving it | Cheapest fix |
|---|---|---|
| No per-call duration | A performance regression ships unnoticed, and reports carry no numbers | `elapsedMs` per tool call at level 1 (also in `nonfunctional-requirements.md`) |
| No correlation id | With several clients, log lines cannot be tied to a call | Prefix each request's log lines with its JSON-RPC `id` |
| Log lost on restart | A crash that takes Rhino down leaves no Cordyceps record | Optional rolling file log, off by default |
| No machine-readable error code | Agents branch on message text (#101) | Stable `error.code` in failure replies (`api-contract.md`) |
| No redaction notice | Users may post script source publicly | One line in the bug-report template and the README |

## Norm candidates (not ratified)

1. Every tool reply carries the host status block, on failures too.
2. The liveness probe and health endpoint answer from cached state only, and never touch the UI
   thread or the document lock.
3. Errors are always logged with type and stack locally, and returned to the client as a message
   only.
4. Nothing observed leaves the machine except through the MCP client the user connected.
