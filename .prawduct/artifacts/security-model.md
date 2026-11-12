---
artifact: security-model
version: 1
depends_on:
  - artifact: data-model
last_validated: 2026-11-11
---

# Security Model

**The one-line model:** Cordyceps gives whoever can reach `127.0.0.1:<port>` the full power of the
logged-in user's Rhino — including running arbitrary code — and protects that port only by keeping
it off the network and refusing browser origins other than localhost. There is no authentication.

That is a deliberate trade for a local developer tool, and it matches how most local MCP servers
work. But Cordyceps has thousands of users, so the trade, what it covers, and what it does not,
are written down here instead of left implicit.

<!-- OPTIONAL norm home. No norms are ratified yet; see "Norm candidates" at the end. -->

## What a caller can do

Stating the capability plainly, because it sets the stakes for everything below:

| Capability | Actions | Effect |
|---|---|---|
| **Run arbitrary code in the Rhino process** | `gh_script set` / `configure` (C# and Python 3 script components, which run on the next solve); `rhino_scene script` (any Rhino command line, via `RhinoApp.RunScript`, which includes Rhino's own script runners) | Anything the logged-in user can do: read and write files, open network connections, start processes |
| Write files at a caller-chosen path | `gh_document save` (overwrites), `capture_*` with `path` (creates directories) | Overwrite any file the user can write |
| Read files at a caller-chosen path | `rhino_scene place_image`, `rhino_render material_texture` (load an image into the model) | Only images, loaded into the model, not returned |
| Change or destroy model content | Every `gh_*` and `rhino_*` mutation | Bounded by undo (Grasshopper edits) and snapshots |

So the security question is entirely **who can reach the port**. Once a caller is in, there is no
smaller privilege to give it.

## Authentication

None. The trust boundary is the network binding and the origin check, both in `McpServer.cs`:

1. **Loopback only.** The listener binds `http://localhost:<port>/` and `http://127.0.0.1:<port>/`.
   Nothing on another machine can connect. The port is set by the component's `HttpPort` input
   (default 26929).
2. **Browser origin check (DNS-rebinding defence).** A request carrying an `Origin` header whose
   host is not `localhost` or `127.0.0.1` is refused with 403; a malformed `Origin` is refused too.
   This stops a web page the user visits from driving Rhino through the browser.
3. **No `Origin` header is accepted.** Native MCP clients (Claude Desktop, Claude Code, Cursor,
   scripts) send none, and they must work.

**Accepted risk (inferred, not yet an owner ruling):** any process running as any user on the same
machine can call every tool, including the code-execution ones. On a single-user workstation that
process could already run code as the user, so the port adds no new power. On a shared machine (a
lab, a terminal server, a render node) it does: another logged-in user could run code inside this
user's Rhino. Nothing in the product or its docs says this today.

## Authorization

None beyond authentication: every caller that gets in can call every action. There is one object
model (the user's own documents) and one party (the user and the agents they run), so per-object
authorization has nothing to separate. The OWASP API items reduce to:

- **Broken object-level authorization:** not applicable; all objects belong to the one user.
- **Mass assignment:** tool parameters are explicit primitive arguments; an argument the tool does
  not declare is refused before the tool runs, naming the closest real one
  (`Core/ToolArgumentBinder.cs`).
- **Excessive data exposure:** replies carry document content by design; the caller is the owner.

The **human beside the agent** is the authorization layer that matters in practice: every
Grasshopper edit is one undo step they can reverse, and actions that remove things refuse ambiguous
names (default nicknames, the default group name) so an agent cannot delete "whichever Circle".
That is a safety property against a confused agent, not a security control against an attacker.

## Data Privacy

- **No personal, financial or regulated data.** Cordyceps handles design documents and Rhino models
  (`project-state.yaml` → `scope.never`). Those can be commercially sensitive to the user.
- **Nothing leaves the machine through Cordyceps.** No telemetry, no update check, no outbound
  connection. (The MCP client sends what it reads to its model provider; that is the client's data
  flow, not ours, and users should know their designs reach whichever LLM they use.)
- **The debug log** keeps the last 500 entries in memory, including the first 200 characters of
  each request and response. Every entry is stored whatever the `DebugLevel`; the level only
  decides which are also echoed to the Rhino command history. It is readable by any caller through
  `gh_inspect(action='log')`. Script source sent to
  `gh_script` can appear there.
- **Capture images** written to `<temp>/Cordyceps/` are never deleted by Cordyceps
  (`data-model.md` → Cordyceps-written files).
- **Snapshots** are memory-only and die with Rhino.

## Abuse Prevention

| Vector | Control |
|---|---|
| Remote network caller | Loopback-only bind |
| Malicious web page (DNS rebinding, cross-site POST) | `Origin` check → 403; CORS only echoes a validated localhost origin |
| Memory exhaustion by request size | Body capped at 10 MB (413); a request without `Content-Length` is refused (411) so the cap cannot be bypassed by chunked transfer |
| Wedging the server | Document-lock waiters fail after 120 s; the liveness probe never takes the lock |
| A confused agent destroying work | One undo step per edit; whole-call rollback; exact-name resolution; default names refused on removal |
| Request flooding | **None.** No rate limit; every request takes a worker thread and queues on the document lock. Accepted: the only callers are local |

## Known gaps

Raised here so they are decided rather than discovered. None is tracked in the backlog yet.

1. **No authentication on a code-execution endpoint.** Options, cheapest first: document the
   shared-machine risk in the README and the component's description; add an optional shared
   token (an input on the component, sent as a header by the client); refuse to start on a machine
   with several interactive sessions. Recommendation: the documentation now, an optional token
   when someone asks for it.
2. **`Origin: null` is refused, which is right, but a missing `Origin` is trusted.** That is
   required for native clients. It means the origin check protects against browsers only, which is
   its job — written down so nobody mistakes it for authentication.
3. **The debug log can hold script source and payload fragments**, and any caller can read it.
   Acceptable while there is one party; worth revisiting if authentication arrives.

## Norm candidates (not ratified)

1. The server binds loopback only, and refuses any browser origin that is not localhost.
2. Every request body has a declared, capped size.
3. Cordyceps makes no outbound network connection.
