---
schema: 1
type: fix
scope: gh-document-save
date: 2026-06-24
title: "gh_script(set) flags a silently-broken Script component (issue #15)"
---
**Why:** `gh_document(action='save')` could not overwrite an existing `.gh` (binary) file —
every repeated save returned a bare `"Failed to write file"`, breaking incremental
checkpoints and "save before mutating" safety nets. Root cause was a format-dependent
overwrite flag: the `.gh` branch passed `overwrite=false` to GH_IO's
`GH_Archive.WriteToFile`, while `.ghx` correctly passed `overwrite=true`. The save policy
is now a pure, host-free helper (`Core/GhArchiveSave.cs`) that returns
`overwrite=true, rememberPath=true` for both formats (File→Save semantics), with 15 unit
tests including a regression guard for the format-dependent overwrite. Reproduced and to be
re-verified live against the running Cordyceps MCP server. Reported by @anthonyesau (#14).

Legacy tags: status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
