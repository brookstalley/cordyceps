# Build Plan — Wire .NET/xUnit test evidence into the Prawduct gate (TST-9Q4M)

**Type:** Tooling / gate-soundness · **Size:** Medium (new dependency + governance-gate infrastructure)
**Branch:** `fix/dotnet-test-evidence`
**Backlog:** TST-9Q4M (related: GHS-7K2P)
**Critic mode:** chunk (single-chunk); run `cumulative` before any PR.

## Requirements Confidence: High

1. **What problem are we solving?** `prawduct-hook test-evidence record` defaults to `python -m pytest`, which cannot run this C#/xUnit repo, so no `.test-evidence.json` is ever produced — every code chunk gets a "no test evidence" Critic/PR WARNING and the freshness gate is unsound for this project.
2. **What does success look like?** `prawduct-hook test-evidence record` runs the real xUnit suite, writes `.prawduct/.test-evidence.json` with accurate counts (53 passed / 0 failed), exits 0; `prawduct-hook test-status` then exits 0 (`current`). No WARNING about missing evidence on the next chunk.
3. **What's out of scope?** Coverage enforcement (`coverage_required` stays false; the F4a symbol-grep overlay is pytest-oriented and best-effort here). Not changing the plugin/hook itself. Not adding CI.

## How the hook consumes it (verified by reading `bin/prawduct-hook` cmd_test_evidence)

- Reads `test_command` from project-state.yaml (must contain `{junit_xml}`), `shlex.split`s it (NO shell — `;` inside a token is safe, only a literal `#` truncates the YAML scalar), substitutes `{junit_xml}` with a temp path, runs it from repo root.
- Parses the JUnit XML: `<testsuite>` elements with `tests/failures/errors/skipped/time` attrs → passed/failed/skipped counts; exit 0 iff the run's own exit was 0. `JunitXml.TestLogger` emits a `<testsuites>` root with matching `<testsuite>` children — compatible.
- Best-effort F4a coverage overlay via `test-reference-verify` (won't fail the record; coverage not required here).

### Chunk 01: Add a junit logger and declare test_command

**Files:**
- `src/Cordyceps.Tests/Cordyceps.Tests.csproj` — add `<PackageReference Include="JunitXml.TestLogger" Version="8.0.0" />` so `dotnet test --logger junit` produces JUnit XML.
- `.prawduct/project-state.yaml` — set `test_command: dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release --logger junit;LogFilePath={junit_xml}` (no `#`, no shell operators; `;` is intra-token, safe under shlex).

**No new C# tests:** this is build/config that wires existing tests into the evidence gate; it is verified by exercising the gate itself (below), not by new unit tests.

## Acceptance criteria
- [ ] `dotnet test -c Release` still green (53 passed) with the logger added.
- [ ] `prawduct-hook test-evidence record` exits 0 and writes `.prawduct/.test-evidence.json` with `passed: 53`, `failed: 0`, `git_sha` = HEAD.
- [ ] `prawduct-hook test-status` exits 0 (`current`).
- [ ] Critic (chunk) clean; backlog TST-9Q4M → shipped on merge.

## Status
- [ ] Chunk 01: Add a junit logger and declare test_command
