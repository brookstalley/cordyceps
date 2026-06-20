# Backlog — Cordyceps

<!-- Structured backlog (Prawduct v1.7+). Managed with the `/backlog` skill:
     /backlog            summary + menu
     /backlog pick       what to work on next (filters + natural language)
     /backlog add        file a new item (searches for duplicates first)
     /backlog find <q>   search title/metadata/body
     /backlog list       tabular view (default: open, added within 90d)
     /backlog update ID  change metadata or status
     /backlog migrate    convert legacy unstructured items to this format

     Items move between the three sections below via `/backlog update ID status=...`.
     The framework never infers status from build plans or change logs — an agent
     or human makes the call explicitly (see backlog-system-requirements.md D4/§5).

== Item shape ==

  - **[PFX-XXXX]** One-line title
    `effort: M · impact: M · area: stop-hook · source: reflection · added: 2026-05-29 · status: open`

    Free-form body of any length — a single sentence or multi-paragraph analysis
    with file refs, fix-shape, and open questions. The author chooses what fits.

  ID format `[PFX-XXXX]`:
    PFX = 2–3 uppercase letters naming the work-space the item was filed from.
          Derive a sensible prefix from the item's area; reuse existing ones so
          related items share a prefix. Starter vocabulary (extend freely):
            STH stop-hook · CRT critic · SYN sync · LLM prompt/LLM · BKL backlog
            MIG migration · JNT janitor · MET methodology · DOC docs · TST tests
          A project may optionally declare its prefix vocabulary as
          `backlog_prefixes:` in project-state.yaml for validation — not required.
    XXXX = 4-char random alphanumeric (base36). Random IDs avoid cross-branch
           collisions; ~1.7M combinations per prefix.

  Metadata bar (one backticked, dot-separated line; required on new items):
    effort: S | M | L     S = <30 min · M = hours · L = multi-chunk
    impact: S | M | L     S = cosmetic · M = quality-of-life · L = user-felt/structural
    area:   <tag>         free-form topic tag; reuse existing tags to enable grouping
    source: builder | critic | reflection | janitor | user
    added:  YYYY-MM-DD
    status: open | promoted | shipped | dropped
  Optional, on the same line (distinct concepts — keep them straight):
    related:   PFX-XXXX, PFX-XXXX   cross-references to related items
    closes:    PFX-XXXX             this item supersedes another backlog item (item → item)
    closed-by: <chunk-id|tag>       what shipped this item, set on status=shipped (item → release)
    reviewed:  YYYY-MM-DD           last-touched timestamp (auto-set on any update)
    accepted-by: @actor             soft claim "someone is on this" so others don't
                                    double-pick; pick/list exclude claimed items.
                                    Does NOT auto-expire; auto-cleared on ship/drop.
                                    Not a lock (backlog.md is eventually-consistent).
    stage: <lifecycle>              idea | research | requirements | design | ready.
                                    Where the item sits in the feature lifecycle;
                                    only `ready` is implementable. Absent/early =>
                                    pick routes to discovery/planning, not code.
    refs: <doc#section>, <doc>      links to governing artifacts (requirements /
                                    arch / design docs). Distinct from `related:`
                                    (which is item -> item).

  Legacy items (no metadata) remain valid — tools treat them as
  `effort: ? · impact: ? · area: untagged · status: open` and rank them lower.
  Run `/backlog migrate` to add structure at your own pace; nothing is forced. -->

## Open

<!-- Items available to pick up. -->

- **[GHS-7K2P]** `gh_script(set)` silently drops the script component's language directive → "Can not determine input code language"
  `effort: M · impact: L · area: gh-script · source: user · added: 2026-06-20 · status: open · stage: ready · refs: incoming-bugs/script-component-language-lost-on-setsource.md`

  `ActionSet`/`ActionConfigure` in `src/Cordyceps/Tools/Unified/GhScriptTool.cs` call
  `scriptComp.SetSource(code)` (`:164`, and `:260`/`:283` in configure), which overwrites the
  leading `#!` language directive that Rhino 8's unified `ScriptComponent` uses to infer the
  script language. If the caller's `code` lacks the directive, the component loses its language
  association and emits no geometry, failing at solve time with a runtime error on `out`:
  "Can not determine input code language". Manifests only at solve time — `set` returns
  `success: true`.

  Hits anyone following cordyceps' own docs: `Knowledge/Prompts/SetupScriptComponent.md` and
  `Knowledge/ComponentPatternsGuide.md` both show directive-less Python bodies. Full root cause
  and live-verified repro in `incoming-bugs/script-component-language-lost-on-setsource.md`.

  **Preferred fix:** before `SetSource`, auto-preserve/prepend the directive matching the
  component's current language (Python 3 / Python 2 / C#) when `code` doesn't already start
  with a recognized `#!` directive — backward compatible. Also fix the docs/templates to show
  `#! python 3` / `#! csharp` as line 1, and mention the directive requirement in `gh_script`
  help text. Touches the Documentation Contract boundary (server instructions, ActionInfo help,
  Knowledge guides — see CLAUDE.md Documentation Audit table).

## Promoted

<!-- Items currently being addressed in an active build plan. /backlog pick
     skips these by default (work is already in flight). -->

## Archive

<!-- Shipped and dropped items, kept for searchability. Never deleted. -->
