# Boundary Patterns — Cordyceps

<!-- Contract surfaces where components interact. When changes cross these
     boundaries, the builder investigates consumer impact before completing
     the chunk. The Critic verifies investigation occurred. -->

## Contract Surfaces

### MCP Tool / Action Contract  (PRIMARY — external, consumed by AI agents & scripts)
- **Producer:** `src/Cordyceps/Tools/Unified/*.cs` — 7 `[McpServerToolType]` classes, each a single `[McpServerTool]` method dispatching on an `action` parameter — surfaced by reflection in `src/Cordyceps/McpServer.cs`.
- **Consumer:** External MCP clients — Claude Desktop/Code, Cursor, VS Code, or any MCP library — over HTTP+SSE (default port 26929).
- **Contract:** Tool names (method name → snake_case, e.g. `GhCanvas` → `gh_canvas`), the per-tool `action` vocabulary, parameter names/types/`[Description]`s, and the JSON response shape (`{ success, ... }`). **Breaking any of these breaks installed users' agent workflows** — this is the medium-risk surface. Evolve with additive actions and `Core/DeprecationRegistry.cs`, never silent renames or removals.

### Embedded Documentation Contract  (agent-facing — must track the code)
- **Producer:** `McpServer.GetServerInstructions()`, per-tool `UnifiedToolInfo` (`action='help'`), `Resources/ResourceRegistry.cs` (gh:// guides + `Knowledge/*.md`), `Prompts/PromptRegistry.cs`.
- **Consumer:** AI agents discovering and using the tools at runtime.
- **Contract:** Documented actions, params, and workflows must match actual tool behavior — drift makes the agent do the wrong thing. Enforced by the CLAUDE.md Documentation Audit (Critic-checked). (The pending `gh_script` language bug is a live example: the docs/templates show Python bodies without the `#! python 3` directive the host now requires.)

### Grasshopper / Rhino Host API  (FOREIGN API — not owned by this project)
- **Producer:** Grasshopper 8 SDK / RhinoCommon (RhinoCodePluginGH script components, `GH_Document`, `IGH_VariableParameterComponent`, cluster input hooks, etc.).
- **Consumer:** `src/Cordyceps/Core/` and `src/Cordyceps/Tools/Unified/`.
- **Contract:** Host behaviors the project depends on but cannot change — the UI-thread requirement, `ExpireSolution(false)` vs `NewSolution`, cluster-editor solution semantics, script-component language inference. Verify against the live host (read the SDK or probe in Rhino) before relying on new host behavior.

### Configuration Interface
- **Producer:** The `Cordyceps` Grasshopper component inputs (Port, DebugLevel) in `CordycepsComponent.cs`.
- **Consumer:** `McpServer` (port binding) and `Core/DebugLog` (log verbosity).
- **Contract:** Default port 26929; DebugLevel 0+ controls request/response logging verbosity.

## Test Levels

| Level | Exists | When to Run | Location |
|-------|--------|-------------|----------|
| Unit | Yes | Every change to host-independent logic | `src/Cordyceps.Tests/` (xUnit) |
| Integration | Manual / live | Changes touching the live document or host APIs | Verified live in Rhino 8 (no automated host harness) |
| Contract | Manual | Any change to tool names, actions, params, or response shape | Compare against server instructions / `action='help'`; see `Knowledge/McpTestingGuide.md` |
| End-to-end | Manual | Before release | Drive a real definition via an MCP client in Rhino |
