---
schema: 1
type: fix
scope: mcp-error-contract
date: 2026-06-20
title: Honor the MCP error contract at the server boundary (MCP-4R2K)
---
**Why:** `McpServer.HandleToolCallAsync` hardcoded the transport `isError` flag to `false`, so
tool results carrying `{"success": false}` were reported to MCP clients as successes; and
tool-body exceptions escaped as raw JSON-RPC `-32603` protocol errors (only `GhScriptTool`
caught them), so the 7 tools behaved inconsistently. Both are now routed through a new
host-free `Core/McpResultFormatter` (`IsErrorResult` derives `isError` from the parsed
`success` field; `FormatExceptionResult` converts any tool-body throw — unwrapping
`TargetInvocationException` — into a `{success:false,error}` result with `isError:true`),
applied uniformly at the boundary. 15 new unit tests (68 total). Broad boundary catch carries a
`prawduct:allow` waiver. Committed `905825c`/`0a525d0` on `fix/mcp-error-contract`; merged to main
via PR #18 (squash `f9e0663`). Root CHANGELOG `[Unreleased]` Fixed entry + `McpTestingGuide.md`
contract line already added.

Legacy tags: status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
