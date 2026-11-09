---
schema: 1
type: chore
scope: backlog-batch-2026-06-20
date: 2026-06-20
title: Backlog batch — docs sync, test coverage, code-quality cleanup
---
**Why:** four ready backlog items addressed as one stacked PR on `fix/mcp-error-contract`.
(DOC-8M3T) `GetServerInstructions()` lagged the code by 11 live actions agents see on MCP
initialize — synced in code-dispatch order (gh_canvas `zoomable`; rhino_scene `set_color`,`bbox`;
rhino_render 4 view + 4 light actions). (TST-6W7H) the host-free `RequestValidator` +
`UnifiedToolHelpers` contract classes had zero coverage — linked into the test project with ~69
new unit cases (suite 68→137). (CQ-2X8B) duplicated proxy-instantiation unified behind
`ToolHelpers.WithProxyComponent`; dead `GrasshopperContext.ExecuteOnUiThreadAsync` removed.
(CQ-5J9N) every silent `catch` swallow in `src/Cordyceps/` now logs with context or is narrowed
to the expected exception type, and the MCP tool-boundary catch logs the full exception (type +
stack) operator-side. Internal quality + agent-facing docs; no shipped-plugin behavior change, so
no root CHANGELOG entry. Merged to main via PR #18 (squash `f9e0663`).

Legacy tags: chunks=01,02,03,04; status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
