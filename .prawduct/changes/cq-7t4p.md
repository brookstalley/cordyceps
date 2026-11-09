---
schema: 1
type: fix
scope: cq-7t4p
date: 2026-06-21
title: Flag failed component introspection in gh_inspect docs (CQ-7T4P)
---
**Why:** `gh_inspect(action='docs')` returned `success:true` with empty `inputs`/`outputs`
when a component proxy couldn't be instantiated, so callers couldn't distinguish "component
has no parameters" from "introspection failed" (CQ-5J9N had added the log but no caller
signal). `ToolHelpers.WithProxyComponent` now returns `bool` (did the callback run); on
failure `ActionDocs` adds `paramsUnavailable:true` + a `note`, success-path shape unchanged.
The cumulative Critic surfaced a third params-surfacing path — `gh://component/{name}`
(`ResourceRegistry.GenerateComponentDocumentation`), reached via a direct `CreateComponent`
that bypassed the helper — which silently omitted its markdown Inputs/Outputs sections; now
emits a `## Parameters` note instead. Doc-audit: root CHANGELOG `[Unreleased]` Fixed entry +
`gh_inspect` `docs` ActionInfo Tips. Critic `final` + `verify-resolutions` clean; 137 tests
pass. Committed `ccd8e1d` on `fix/proxy-params-unavailable`; pushed direct to main (`d1e1787`).

Legacy tags: chunks=01; status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
