# Change Log — Cordyceps

<!-- Append new entries at the top. Each entry is a ## section.
     This file is separate from project-state.yaml to reduce merge conflicts
     when multiple branches add entries simultaneously.

     A repo that records changes as fragments instead — one file per scope in
     .prawduct/changes/, from templates/change-fragment.md — adds nothing here:
     this file then holds shipped history only, and `prawduct-hook
     migrate-change-log` moves any release-pending entry into a fragment.

     # Tagged entries

     This file is PROSE. Its body is what a reader — and a release note —
     actually gets. Two machine-read keys ride in a tag-line directly under
     the ## header, and `check-releasability` is the only thing that reads
     them:

         ## YYYY-MM-DD: title (vN.M.P)

         <!-- prawduct: scope=pantry-v1 | release=v1.3.18 -->

         **Why:** ...

     Recognized keys:
       scope    - the `scope:` frontmatter of the build plan that governs
                  the work (e.g., pantry-v1), copied exactly. It names the
                  WORK, not a version: a scope with no plan declaring it
                  warns at release as work shipping with no plan.
       release  - the version that carried this entry: three or four numeric
                  parts, optionally with a -suffix (release=v1.3.18,
                  release=v1.3.18.2, release=v1.4.0-rc.1). Its ABSENCE is what
                  marks the entry release-pending, so write NO release= on
                  the feature branch and add it at release. Any value at all
                  — including a placeholder naming the absence, e.g.
                  `release=unreleased` — drops the whole scope out of the
                  release-pending set and silently unships the work.

     Nothing else is read; any other key is inert. Which chunks an entry
     shipped belongs in the entry BODY, where release notes and readers find
     it — a deliverable omitted from the body ships invisibly. -->

<!-- Older entries live in .prawduct/change-log-archive/YYYY-MM.md, moved there verbatim by `prawduct-hook archive-change-log`. -->
