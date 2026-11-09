---
schema: 1
type: fix
scope: gh-script-configure-wires
date: 2026-06-24
title: slider add-params + configure wire-preservation (v1.4.12)
---
**Why:** `gh_script(action='configure')` unregistered every input/output param and re-registered
them from scratch (`ConfigureViaVariableParams`), silently destroying ALL wires — even on
name-unchanged params — and reported nothing lost. It now reshapes params by name via the same
LCS sync `set` already used, extracted to a pure helper (`Core/ParamSyncPlan.cs`): name-matched
params keep their connections; wires on renamed/removed params return in a `lostConnections` array
(with a `reconnectHint`) usable directly with `gh_wire(action='connect')`. `configure` is now also
a partial update — omit a side to leave it untouched, pass `[]` to clear it (previously,
configuring only `inputs` wiped all `outputs`). 10 unit tests; host glue queued for operator
verification (VRF-005). (GHS-3W9N)

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
