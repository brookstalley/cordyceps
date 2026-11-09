---
schema: 1
type: fix
scope: gh-canvas-slider-add
date: 2026-06-24
title: slider add-params + configure wire-preservation (v1.4.12)
---
**Why:** `gh_canvas(action='add', type='slider', ...)` silently dropped min/max/value/decimals —
the `add` dispatcher forwarded only type/x/y/nickname to `ActionAdd`, so a new slider always
landed at the default 0–1 range / 0.5 value regardless of args, forcing a second `config` call.
The slider-config decision is now a pure host-free helper (`Core/SliderConfig.cs`, parsing the
value with InvariantCulture — a latent locale fix) shared by both `add` (applied after
`AddObject` when the new object is a `GH_NumberSlider`: range first, then value so it isn't
clamped to the old range) and `config` (refactored for parity). 19 unit tests incl. the
dropped-params regression; non-slider adds ignore the params. Host glue queued for operator
verification (VRF-004). (GHC-7X4B)

Legacy tags: status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
