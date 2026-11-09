---
schema: 1
type: fix
scope: param-spec
date: 2026-09-07
title: one rule for reading a parameter spec
---
**Why:** Coldtea task **BRO-3** (that tracker, not a backlog id; the backlog handle is issue
**#48 / `GHC-6P2M`**, which filed the same defect as a duplicated-resolver divergence). `gh_wire` folded its range check into the same condition as the numeric parse
(`int.TryParse(spec, out i) && i >= 0 && i < list.Count`), so an index past the end of a
component's port list was indistinguishable from a spec that was never a number. Execution fell
through to the exact-name matchers and then to a substring matcher, which accepts a digit
appearing anywhere in a name — so `targetParam='2'` against a two-input component connected to
`Plane 2` and reported success. Grasshopper auto-numbers duplicated nicknames on exactly the
variable-parameter components this tool targets, so digit-bearing names are ordinary there.

**What changed:** the resolution rule now lives once, in host-free `Core/ParamSpecLookup.cs`, with
`ParamSpecLookupTests` covering each branch (the anchor: an out-of-range index against a
digit-bearing list reports the range, it does not match). `GhWireTool` and
`GhCanvasTool.Modifiers` both call it. The modifier tool's semantics are the ones kept — an index
means an index, and every miss carries a reason — so the wire tool's six call sites now return
that reason instead of composing their own bare "not found" text, prefixed `Source:`/`Target:` so
a bulk connect names the failing end.

**Three behaviour changes beyond the reported defect.** Two refuse to conflate "unparseable" with
"empty": an empty `sourceParam`/`targetParam` on `disconnect` used to match port 0, because an
empty string is a substring of every name, and now reports the missing argument; and a `null` spec
used to throw out of `String.IndexOf` rather than return an error. The third closes the same defect
at the far end of the range: `int.TryParse` answers "does this fit in an `Int32`", which is
narrower than "is this written as a number", so a spec like `'99999999999999999999'` failed the
parse and fell through to the name matchers. A spec written as a plain base-10 integer is now
refused as an out-of-range index whatever its magnitude.

**Deliberately not done:** `availableOutputs`/`availableInputs` on connect failures are kept
rather than folded into the reason string — `boundary-patterns.md` makes the response shape an
additively-evolved surface, and dropping a field an agent may read is a silent removal. The wire
tool's `IGH_Param` early return (a bare param object ignores both the spec and the side) is out of
the task's scope and unchanged.

**Verification:** suite green (`prawduct-hook test-status`); Release build 0 warnings. The live
half — that no wire is created — needs a running Rhino and is enqueued as VRF-015.

Legacy tags: chunks=1

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
