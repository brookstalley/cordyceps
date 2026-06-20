# Change Log — Cordyceps

<!-- Append new entries at the top. Each entry is a ## section.
     This file is separate from project-state.yaml to reduce merge conflicts
     when multiple branches add entries simultaneously.

     # Tagged entries (enabled by default; set `views_enabled: false` in project-state.yaml to opt out)

     With views enabled (the default), add a tag-line directly under each ##
     header to mark which build-plan chunks the entry shipped and which
     release it belongs to. `prawduct-hook regen-views` uses these tags to
     regenerate three derived views:
       * build-plan `## Status` block — checkboxes flip from `status=shipped`
       * `.prawduct/release-notes.md` — sections grouped by `release=`
       * `scope_rollups:` block in project-state.yaml — grouped by `scope=`
     Untagged entries are ignored by all three views.

     Format:

         ## YYYY-MM-DD: title (vN.M.P)

         <!-- prawduct: chunks=00,01,02 | release=v1.3.18 | status=shipped | scope=v1.4 -->

         **Why:** ...

     Recognized keys:
       chunks   - comma-separated chunk IDs (zero-padded, must match
                  build-plan.md ## Status headers exactly: `Chunk 00:`)
       release  - version string (used by release-notes view, future)
       status   - shipped | in-progress | deferred
                  `shipped` means MERGED TO MAINLINE — per-chunk timing.
                  Tag chunks `status=shipped` as soon as the merge commit lands;
                  inclusion in a tagged release is tracked separately via
                  `release=vN.M.P` (set when a release entry consolidates one
                  or more shipped chunks).
       scope    - rollup identifier (e.g., v1.4)

     With `views_enabled: true`, the Status checkboxes in build-plan.md are a
     derived view. Don't hand-edit them — add/update a tagged entry here and
     run `prawduct-hook regen-views`. -->
