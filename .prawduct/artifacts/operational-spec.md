---
artifact: operational-spec
version: 1
depends_on:
  - artifact: nonfunctional-requirements
  - artifact: security-model
last_validated: 2026-11-11
---

# Operational Specification

Cordyceps is not a service. It is a Grasshopper plugin that thousands of people install into
their own Rhino, and it runs only while its component sits on an open canvas. Nobody operates a
fleet. "Operations" here means two things:

1. **Shipping:** getting a correct build to every user and fixing a bad one quickly.
2. **Running on one machine:** what a user, or the agent working for them, does when the local
   server misbehaves.

A defect cannot be rolled back centrally, because every install updates on its own schedule. So
the operational weight sits on the release gate, not on recovery.

<!-- OPTIONAL norm home. No norms are ratified yet; see "Norm candidates" at the end. -->

## Deployment

**Distribution channels.** Every release publishes the same `.gha` to two places, both driven by
`scripts/release.sh` (process: `docs/release-process.md`; step-by-step checklist:
`.prawduct/runbooks/ship-a-release.md`):

| Channel | How users get it | Update model |
|---|---|---|
| Yak (Rhino package manager) | `_PackageManager` → Cordyceps | User-initiated. Rhino flags available updates; nothing installs on its own |
| GitHub Release | README link → `/releases/latest/download/Cordyceps.gha`, copied into the Grasshopper Libraries folder by hand | Manual |

**Release pipeline.** gitflow. Feature branches merge into `develop` through reviewed PRs. A
`develop → main` release PR must pass `build-test` (`.github/workflows/dotnet-ci.yml`), and `main`
is strict-protected with no bypass. Then `release.sh publish` builds from the merged commit, tags
it, and pushes to GitHub and Yak. CI builds and runs the host-free tests on Linux; nothing
automated runs inside Rhino.

**Pre-release and test builds.** CI artifacts (`Cordyceps.gha-<sha>`, 90-day expiry, need a GitHub
login) and GitHub pre-releases (anonymous, durable, skipped by `/releases/latest/`). `release.sh`
has no pre-release command yet (#50).

**Environments.** There is no staging. The equivalent is the operator-verification queue
(`.prawduct/operator-verification.md`): live-Rhino checks recorded per change and run before a
release.

## Backup & Recovery

**Cordyceps keeps no user data that needs backing up.** Its state:

| State | Lifetime | Loss on Rhino restart |
|---|---|---|
| The user's Grasshopper definitions | The user's files, saved by the user | None. Cordyceps never writes them to disk unless a tool is asked to save |
| Edit history | Grasshopper's undo stack; each Cordyceps edit is one undo step, and a failed call rolls back | Same as Grasshopper's own undo |
| Snapshots (`gh_document` snapshot/revert) | In memory, ≤ 20 per Rhino process | All lost. Accepted (`nonfunctional-requirements.md` § Availability) |
| Debug log | In-memory ring buffer, 500 entries | All lost |
| Port and debug level | Inputs on the `Cordyceps` component, saved inside the user's `.gh` file | None |

The project's own durable state (source, tags, releases, backlog) lives on GitHub. Each published
`.gha` is attached to its GitHub Release and stored by Yak, so any shipped version can be
recovered.

**Known defect:** snapshots are global to the Rhino process, not stored per document. A `revert`
while a different definition has focus installs another definition's content into that tab. Until
that is fixed, snapshots are a convenience, not a recovery mechanism. (Not yet filed.)

## Monitoring

**No telemetry.** Cordyceps makes no outbound network calls (verified: no `HttpClient` or
`WebRequest` in `src/Cordyceps/`). The maintainers do not see installs, usage or failures. That is
deliberate for privacy, and it means every signal about the fleet comes from people:

| Signal | Where | Who watches |
|---|---|---|
| Bug reports and feature requests | GitHub Issues on `brookstalley/cordyceps` | Maintainer, during backlog triage |
| Build and host-free test health | `build-test` on every PR and on `develop`/`main` | CI; release PRs cannot merge red |
| Live-Rhino correctness | Operator-verification queue, run before release | Maintainer, manually |

**Per-install health**, for the user and their agent (full detail: `observability-strategy.md`):
- `GET /health` and `gh_inspect(action='connection')` answer from cached state even when Rhino's
  UI thread is blocked.
- Every tool reply carries a `status` block: which document, whether a solve is running, and
  `modal_inferred` when a human probably needs to dismiss a dialog.
- The component's `Status` and `LastCommand` outputs, and runtime errors on the component (for
  example a port conflict, with its fix).

## Failure Recovery

**On one machine**, the blast radius is one user's session, so recovery is simple: "restart it"
is the right answer more often than not.

| Symptom | Cause it usually means | Recovery |
|---|---|---|
| Client cannot connect | Component not on an open canvas, wrong port, or the port is taken | Place the component; read its runtime message; change `HttpPort` |
| Calls return a busy error after 120 s | Another call holds the document | Wait, or let the other call finish; nothing needs repair |
| `modal_inferred: true` in replies | A dialog is open in Rhino | A human dismisses it. The agent should stop and say so, not retry |
| Server answers `/health` but tool calls never return | UI thread blocked (long solve, hung script) | Wait for the solve, or stop the script; as a last resort, restart Rhino. Unsaved work is the user's to save first |
| A tool call failed partway | — | Nothing to do: the call rolled back. Read the error and retry |
| Server will not start after an upgrade | Bad release or incompatible Rhino | Check the Rhino version (8.21+); install the previous version; file an issue |

**For a bad release:** fix forward. A Yak publish cannot be withdrawn or replaced, so the fix is a
new patch version through the normal pipeline. Users already on the bad version stay on it until
they update. That makes a broken release expensive to undo, which is why the release gate
(operator verification on both platforms) carries more weight than recovery does. Whether Rhino's
package manager lets a user pick an older version was not verified while writing this spec. The
GitHub Release for every version is the fallback.

**Runbooks.** One exists: `.prawduct/runbooks/ship-a-release.md`. A "bad release" runbook (how to
confirm the regression, cut the fix-forward patch, and tell users) does not exist yet. It is the
next one worth writing.

## Gaps

- **No staging for Rhino itself.** CI cannot run Grasshopper, so host-dependent behaviour is
  checked only by hand (#37 proposes a scripted live-Rhino smoke harness, #38 the operator queue
  backlog).
- **No two-platform release record.** No release yet records a pass on both Windows and macOS
  (also in `nonfunctional-requirements.md`).
- **No bad-release runbook** (above).
- **Global snapshots** (above).

## Norm candidates (not ratified)

1. A release ships only after its live-Rhino checks pass on both Windows and macOS.
2. Every published version is fixed forward, never replaced: a fix is always a new version.
3. Cordyceps makes no outbound network calls. Any future telemetry is opt-in and documented
   before it ships.
