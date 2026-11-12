---
artifact: nonfunctional-requirements
version: 1
depends_on:
  - artifact: project-state
last_validated: 2026-11-11
---

# Non-Functional Requirements

Cordyceps runs on one person's machine, inside their Rhino, next to them. "Non-functional" here
mostly means **never make Rhino worse to use**: no hangs, no lost work, no surprises, on every
platform and Rhino version users have. Thousands of people rely on it (owner, 2026-11-11), so a
regression is felt widely even though each install is single-user.

<!-- OPTIONAL norm home. No norms are ratified yet; see "Norm candidates" at the end. -->

## Responsiveness

There are no measured latency targets today, and none of the numbers below comes from a benchmark.
What the product guarantees is bounds, not speeds:

| Requirement | Bound | Where it is enforced |
|---|---|---|
| The liveness probe answers even when Rhino is stuck | `gh_inspect(action='connection')` and `GET /health` read cached state only; never the UI thread, never the document lock | `McpServer.HandleHealthCheckAsync`, `Core/SolverState.cs` |
| No call waits forever for the document | A call waiting for another call's document work fails after **120 s** with a busy error | `Core/GrasshopperContext.cs` (`DOCUMENT_LOCK_TIMEOUT_SECONDS`) |
| A solve wait is bounded and caller-chosen | `timeout` 1–300 s, default **30 s**; past it the reply is `over_budget` and the solve keeps running | `Core/SolvePolicy.cs` |
| Waiting for a solve does not block other calls | The wait holds no lock; it polls every 25 ms off the UI thread | `Core/SolveWait.cs` |
| Edits do not run the solver inline | An edit schedules one solve and returns; `NewSolution` is never called | `Core/SolveCoordinator.cs` |
| Stopping the server does not freeze Rhino | The port is freed synchronously; draining in-flight calls (≤ 2 s) happens in the background | `McpServer.Stop()` |

**Expected interactive latency** (inferred from how the code works, not measured): a canvas read
or single edit on a typical definition returns well under a second; a call that triggers or waits
for a solve takes as long as Grasshopper's solve. A call's duration is dominated by the UI thread
being free, not by Cordyceps.

**Gap:** nothing measures this. A regression that makes `outline` take ten seconds on a large
definition would ship unnoticed. Cheapest fix: log `elapsedMs` per tool call at `DebugLevel` 1 so a
user's report carries the number.

## Scalability

| Dimension | Expected | What would break it |
|---|---|---|
| Users per install | 1 human, with 1–3 MCP clients | Several agents calling at once all serialize on the one UI thread; nothing breaks, it just queues |
| Definitions | Several open tabs; tools act on the focused one | — |
| Definition size | Hundreds to low thousands of objects (inferred) | `outline` and `list` read the whole document per call; very large definitions make them slow. Unmeasured |
| Snapshots | ≤ 20 per Rhino process, each a whole document | Large definitions × 20 can use a lot of memory; there is no byte cap (deferred 2026-07-02) |
| Request size | ≤ 10 MB per request | Hard cap (413) |
| Debug log | ≤ 500 entries | Ring buffer |

Growth means more users, each with one machine, not more load on one machine, so no architecture
change is needed for adoption. What growth does raise is the cost of a regression.

## Availability

- **Up while Rhino is open and the `Cordyceps` component is on an open canvas.** There is no service
  to keep up; "down" means the component is not placed, its port is taken, or Rhino is not running.
- **A failure must explain itself.** A port conflict shows the cause and the fix on the component;
  a busy or wedged host is reported in every reply's `status` block, with whether a human must act
  (`modal_inferred`).
- **Nothing is lost when Cordyceps fails.** The documents are the user's; every Grasshopper edit is
  one undo step and a failing call rolls back. In-memory snapshots and the debug log are lost on a
  Rhino restart; that is accepted.

## Compatibility

This is the dimension where thousands of users matter most:

| Surface | Supported | Verified by |
|---|---|---|
| Rhino | 8.21 and later, on .NET 8 | Manual live testing; no automated host matrix |
| Operating systems | Windows and macOS | Manual; CI builds and runs the host-free tests on Linux only (`.github/workflows/dotnet-ci.yml`) |
| MCP protocol | `2025-06-18`, Streamable HTTP, stateless, JSON responses | Clients in use: Claude Desktop, Claude Code, Cursor, VS Code |
| Other Grasshopper plugins | Components from any installed plugin can be added and wired | Best effort; third-party objects that do not save all their state may not undo exactly (CHANGELOG, Unreleased) |

**Gap:** live-Rhino checks are queued per change (`.prawduct/operator-verification.md`, step 17 of
`.prawduct/runbooks/ship-a-release.md`), but no release records a pass on both Windows and macOS
against the current Rhino 8 service release. A two-row platform line in the release runbook is the
cheapest guard.

## Cost Constraints

No hosting, no paid services: it runs on the user's machine and is distributed free through Yak and
GitHub releases. The ongoing costs are CI minutes (GitHub Actions) and maintainer time.

## Norm candidates (not ratified)

1. Nothing Cordyceps does may hang Rhino without a bound, and the liveness probe must always answer.
2. Every buffer or store that grows with use has a cap (request size, log, snapshots).
3. A release is not shipped until it has been run in live Rhino on both Windows and macOS.
