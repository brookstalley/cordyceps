# Build Plan — MCP-4R2K: Honor the MCP error contract at the server boundary

**Work size/type:** Medium · Bugfix (correctness, on the PRIMARY external contract surface)
**Branch:** `fix/mcp-error-contract`
**Critic mode:** chunk → final (single chunk)

## Confidence Check

1. **Problem:** `McpServer.HandleToolCallAsync` (1) hardcodes `isError = false` (`McpServer.cs:645`) so tool results carrying `{"success": false}` are reported to MCP clients as successes, and (2) does not catch exceptions thrown by the tool method, so a tool-body crash escapes as a raw JSON-RPC `-32603` protocol error (caught at `:433`) instead of the project's documented `{success:false, error}` tool result. Behavior is inconsistent across tools (GhScriptTool catches; the other 6 do not).
2. **Success:** A tool returning `{"success": false, ...}` is reported with `isError: true`; a tool method that throws yields a 200 tool result with `isError: true` and text `{"success": false, "error": "<message>"}` — uniformly for all 7 tools. The pure derivation logic is unit-tested.
3. **Out of scope:** Pre-invoke protocol errors (unknown tool, missing required param, JSON arg-conversion failures) — these legitimately stay JSON-RPC errors. The broad-catch sweep (CQ-5J9N), the server-instructions action drift (DOC-8M3T), and removing GhScriptTool's now-redundant inner catch (harmless; behavior-preserving to leave).

**Requirements confidence: High.** The error-handling contract is already documented in `project-preferences.md` ("catch at the tool boundary and return `{success:false, error}` JSON rather than throwing across the MCP boundary") and aligns with MCP semantics (tool-execution errors → tool result with `isError`, not protocol errors). No open product decision.

## Boundary Investigation (PRIMARY: MCP Tool/Action Contract — response shape)

- **Crossed:** the JSON response shape — the transport `isError` flag and how tool-execution exceptions surface.
- **Consumers:** external MCP clients only. `isError` has **no in-repo consumer** (grep: sole occurrence is the producer at `McpServer.cs:645`). Nothing in-repo depends on the `-32603`-on-tool-crash behavior.
- **Impact:** corrective and MCP-spec-aligned; the `{success, ...}` payload shape tools emit is unchanged (not a breaking change). The one client-observable change: a tool-body crash now returns HTTP 200 + `isError:true` + `{success:false,error}` instead of a JSON-RPC `-32603` error — exactly the documented project contract.
- **Tests:** pure formatter logic gets xUnit coverage; transport-level behavior is verified manually in a live host (no automated host harness — stated honestly, not asserted).

## Design

**Defect (a) — `isError`:** derive it from the result text. A result is an error iff it parses as a JSON object whose `success` member is the boolean `false`. Unparseable / non-object / missing-`success` / `success:true` ⇒ not an error (preserves current default for help text and other non-`success` payloads).

**Defect (b) — exception handling:** wrap *only* the tool-method invoke + async-unwrap (`McpServer.cs:631-639`) in a try/catch. On exception, unwrap `TargetInvocationException` to the real cause, log it, and return a tool result with `isError:true` and text `{success:false, error:<message>}`. This fixes all 7 tools at the boundary without editing each (DRY; GhScriptTool's existing inner catch becomes harmless defense-in-depth). The pre-invoke validation throws (`:602`, `:626`) are left as protocol errors.

**Testability:** put the pure logic in a new GH/Rhino-free `Core/McpResultFormatter.cs` (uses only `System`, `System.Reflection`, `Newtonsoft.Json`) and link it into `Cordyceps.Tests.csproj` — same pattern as `JsonTypeConverter`/`ScriptDirective`.

## Chunk 01 — Error contract at the MCP boundary

- [x] `Core/McpResultFormatter.cs`: `IsErrorResult(string)` + `FormatExceptionResult(Exception)` (pure, GH-free).
- [x] `McpServer.HandleToolCallAsync`: derive `isError` via `IsErrorResult`; wrap invoke/await in try/catch → `FormatExceptionResult` + `isError:true` (broad catch waived — tool-invocation boundary, logs + structured-error, never silent).
- [x] Link `McpResultFormatter.cs` into `Cordyceps.Tests.csproj` (+ explicit Newtonsoft.Json 13.0.3 ref); add `McpResultFormatterTests.cs` (15 cases: success-true/false, missing/non-boolean `success`, non-JSON, empty/null, array; exception formatting incl. `TargetInvocationException` unwrap + null-inner; round-trip invariant `IsErrorResult(FormatExceptionResult(ex)) == true`).
- [x] CHANGELOG `[Unreleased]` → Fixed entry.
- [x] Doc audit: server instructions make no transport error claim (no change). Added a contract line to `McpTestingGuide.md` Part 5 describing `isError`/`{success:false,error}` behavior. ActionInfo/ResourceRegistry/Prompts unaffected.

**Done when:** `dotnet test` green (existing 53 + new), build plan Status `[x]`, Critic clean, reflection captured.

## Status

- [x] Chunk 01 — Error contract at the MCP boundary

**Context:** COMPLETE. Committed @ `905825c` on `fix/mcp-error-contract`. `dotnet test` green (68 passed, was 53; +15 formatter tests); Release plugin build clean; test evidence recorded @ `905825c`. Docs audited (CHANGELOG + McpTestingGuide). Critic (verify-resolutions chain): 0 blocking / 0 warning / 1 note (test-evidence sha lag — resolved). Reflection captured. Backlog MCP-4R2K = promoted. Next: PR (on request) or pick the next item (DOC-8M3T overlaps this file).
