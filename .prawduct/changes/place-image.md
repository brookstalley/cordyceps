---
schema: 1
type: feature
scope: place-image
date: 2026-06-21
title: Place raster images as PictureFrame objects — rhino_scene(place_image) (RSC-2H9K)
---
**Why:** External feature request from the Puzzles print-and-cut generator (Chunk 06, deferred on
this): preview a cut layout *over* printed artwork by placing the image as a real Rhino object. No
prior path existed — `rhino_render material_texture` is a PBR texture, not a placed object. New
`rhino_scene(action='place_image')` places a Rhino PictureFrame at a caller-specified
origin/size/optional Z-rotation on an auto-created layer and returns the new object id;
`replace=true`+`name` makes repeated parametric calls idempotent. Foreign API
`AddPictureFrame(Plane, path, asMesh, width, height, selfIllumination, embedBitmap)` re-verified by
reflection on Rhino 8 RhinoCommon (no `ObjectAttributes` overload → layer/name set post-add). New
host-free `Core/PlaceImageValidation.cs` (path-exists + positive-dimension checks) with 12 unit
tests; the find-or-create-layer block shared with `set_layer` was extracted to one helper. Doc
audit: server instructions, `rhino_scene` ActionInfo (`place_image`), root CHANGELOG. Release build
0/0; 149 tests pass. Per project-preferences, the document-touching handler is verified live in
Rhino, not by host-free unit tests.

Legacy tags: chunks=01; status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
