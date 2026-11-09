# Change Log — Cordyceps

<!-- Append new entries at the top. Each entry is a ## section.
     This file is separate from project-state.yaml to reduce merge conflicts
     when multiple branches add entries simultaneously.

     # Tagged entries

     Add a tag-line directly under each ## header to mark which build-plan
     chunks the entry shipped and which release it belongs to.

     Nothing is derived from these tags any more — there are no generated
     views, and `prawduct-hook regen-views` is inert (it warns and writes
     nothing). The tags are read directly: `check-releasability` and
     `plan-backfill` use `scope=` and the presence/absence of `release=`.

     Format:

         ## YYYY-MM-DD: title

         <!-- prawduct: type=bugfix | chunks=01,02 | scope=my-scope -->

         **Why:** ...

     Copy that line as-is for a new entry: no `release=` (the release adds it)
     and no `status=` (retired). Historical entries below carry both.

     Recognized keys:
       chunks   - comma-separated chunk IDs (zero-padded, must match
                  build-plan.md ## Status headers exactly: `Chunk 00:`)
       release  - version string, added when the develop->main release ships
                  the entry. Its ABSENCE is the release-pending state, so never
                  write a placeholder: any value at all drops the entry's whole
                  scope out of `check-releasability`'s pending set.
       status   - LEGACY, inert. Older entries carry shipped|merged|built|
                  deferred; nothing reads it. Don't add it to new entries.
       scope    - rollup identifier (e.g., release-artifact-provenance)

     The `## Status` checkboxes in a build plan are NOT derived from this file.
     They are written by hand: tick a box when its chunk's review passes, and
     nothing will overwrite it. -->

<!-- Older entries live in .prawduct/change-log-archive/YYYY-MM.md, moved there verbatim by `prawduct-hook archive-change-log`. -->
