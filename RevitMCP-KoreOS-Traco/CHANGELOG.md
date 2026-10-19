# Changelog

## [0.9.0] - 2026-10-05

### Added
- Frente estrutural, fase 1: armadura nativa do Revit (guia em `docs/REBAR.md`).
- `list_rebar_types`: tipos de barra (Ø nominal, aço, dobras, kg/m), ganchos, cobrimentos, formas, área/caminho, telas soldadas, emendas, etiquetas e tabelas de vergalhão.
- `get_rebars`: quantidade, comprimento e peso de aço por bitola e por conjunto (modelo, vista, hospedeiros ou partição).
- `get_rebar_host_info`: hospedeiro válido, cobrimento por face e armadura existente.
- `audit_rebar_template`: lacunas do template contra NBR 7480 (bitolas CA-50/CA-60) e NBR 6118 (tabelas 7.2, 9.1, 9.2), ganchos, formas, etiquetas, tabelas, telas e emendas.
- `new_project_from_template`: cria e abre um projeto a partir de um `.rte` (a API não abre `.rte` como documento).
- `create_curved_wall`: parede em arco ou círculo completo (duas meias paredes), raio pelo eixo ou pelas faces, verificado após a criação.
- `reinforce_curved_wall`: anéis em arco (conjuntos editáveis, barras até 12 m, traspasses alternados por meia barra) e verticais de forma livre nas duas faces, divididas com traspasse alternado quando a parede passa de uma barra (silos); camadas concêntricas às faces da parede (cobrimento constante); traspasses pela NBR 6118 9.5.2.2 quando não informados.
- Regras puras `Nbr6118` (fbd, lb, l0t, α0t, dobras mínimas, cobrimentos da tabela 7.2, massa linear) e `CurvedWallRebarPlanner`, com testes.
- Perfil de ferramentas `structure`.

### Known limitations
- Verticais de parede curva em forma livre: Ø editável, espaçamento refeito pela ferramenta (a API não cria a distribuição alinhada).
- No Revit 2026 os métodos `Rebar.CreateFromCurves` com ganchos e `CreateFreeForm(..., out error)` estão obsoletos; mantidos por compatibilidade com o 2025.

## [0.8.66] - 2026-09-18

- Native schedule documentation: `configure_schedule` can combine equal rows, format columns, rename headings and set office text types; `place_view_on_sheet` places schedules as `ScheduleSheetInstance` and rejects duplicates on a sheet.
- Map-based site documentation: `create_drafting_view` creates a scaled 2D documentation view and `place_image_in_view` links or imports a local raster with an explicit model-space width. This keeps aerial-map attribution visible and the source replaceable.

## [0.8.65] - 2026-09-15

- Opt-in session-scoped idempotency for HTTP single/batch dispatch and forwarding MCP tools. Same key and payload share one execution, including concurrent requests; changed payloads and previous-session keys are rejected. Explicit document targets are required.
- `/health` advertises `sessionId` and `idempotencySupported`; authenticated `GET /operations?key=...` returns pending/completed status. The bounded in-memory ledger never evicts keys during a host session; oversized results retain a non-reexecutable summary.
- TypeScript refuses keyed dispatch to older hosts and reports the key on ambiguous transport failures. No automatic write retry. Live Revit 2026 validation passed on a disposable copy: preview/replay, concurrent single creation, batch response discard/recovery, key conflicts and explicit-target enforcement; test levels cleaned up and copy saved. See docs/OPERATION_IDEMPOTENCY.md for scope and remaining limits.
- Added six local reference repositories with pinned provenance under `referencias/`; external checkouts are ignored by Git.

## [0.8.64] - 2026-09-15

- `set_view_crop`: sections and elevations take `viewRectMeters` {minU,minV,maxU,maxV} in view coordinates, keeping the depth — callouts and dimension chains beside a section needed room the original crop did not leave.
- Add `set_category_visibility`: show or hide categories per view (section marks and text notes hidden in plans by a detached office template).
- `create_text_note`: `width` is paper space (default 60 mm) and is validated against the text type; a note without extents in the view fails instead of reporting success. A 0.5 "m" width produced notes that never appeared. Returns `textBox` (text without leaders).
- `create_aligned_dimension` `face`/`top`/`bottom` on family instances (columns) use the symbol geometry qualified by the instance; instance-geometry faces gave zero-length segments.
- `get_annotation_extents`: text notes use the text box without leaders (a leader's bounding box overlapped everything it crossed).

## [0.8.63] - 2026-09-14

- `create_text_note`: `typeId`/`typeName`, `align`, `rotationDegrees` and `leaders` (end, optional elbow; side follows the end point); returns the regenerated view extents. Material callouts in sections, facades and roof plans need a typed text with a leader.
- `create_detail_line`: `lineStyle` by name (dashed projection lines of roofs and elements above); unknown names fail listing the valid styles.
- `create_aligned_dimension`: family instance named references (`left`/`right` jambs, `top`/`bottom` head and sill, front/back, centers), Level plane references, `top`/`bottom` horizontal faces of any element (slab levels in sections) and `face` + `normal` (+ `near`); `typeId`; per-segment values (a chain has no overall value).
- Add `get_annotation_extents`: 2D extents of dimensions, texts, tags and spot dimensions in a view, overlapping pairs and annotations outside the crop — checks that dimensions do not cover texts before a plan goes on a sheet.

## [0.8.62] - 2026-09-14

- `place_family_instance`: `hostSide: "top"` hosts WorkPlaneBased families (basins, drains) on a Floor's top face; a `location.z` above the face becomes the host offset (inset basin at countertop height). Previously such families landed at world z = 0 and could not be moved up.
- `place_family_instance`: OneLevelBased families without host no longer rise by the level elevation twice (Revit stored the point Z as the level offset).
- `tag_all_in_view`: rooms are tagged with `RoomTag` (IndependentTag failed for every room) with optional `tagTypeName`; failures now report the Revit message.
- Add `fit_datums_to_crop`: trims levels and grids visible in a view to its crop region (view-specific 2D extents). Sections and facades placed on an A1 sheet were stretched by level lines running far beyond the drawing.
- `create_floor_type_variant`: `layerMaterialIds` (native order, null preserves), verified after creation — floor finishes per room with their own material.

## [0.8.61] - 2026-09-14

### Added
- create_material: independent Generic appearance, optional model crosshatch and graphics transparency; optional local bitmap cutout with real-world scale. Existing materials are not changed.
- create_direct_shape panel: thin convex planar polygon centered on its input plane, including sloped infill. No modeled mesh wires.
- inspect_planar_faces includeEdgeLoops: actual face boundaries for measured panel placement.

### Validation
- Live material, hatch and panel rendering remain pending restart and visual verification.

## [0.8.60] - 2026-09-14

### Changed
- create_wall_type_variant accepts layerMaterialIds (native exterior-to-interior order, null preserves a layer), including material-only variants that preserve all widths. Material IDs are validated against the target document and read back after regeneration.
- Existing names are reused only when layer widths, functions, core boundaries and materials match. Existing wall instances are never reassigned implicitly.
- Prepared for the Edifício Novo finishes; requires Revit restart and live validation. No claim of live material validation.

## [0.8.59] - 2026-09-14

- Add `configure_railing_type`: top rail, non-continuous rails (profile, material), the main baluster pattern (one baluster, spacing, base/top references and offsets, justification, break condition), start/corner/end posts and balusters per tread, optionally on a duplicate. Rails and balusters live in `RailStructure`/`BalusterPlacement`, so `set_parameter` could not touch them. Live need: the tubular steel guards with wire-mesh infill of an existing rear stair (top and bottom tube, posts at the ends, dense bars for the mesh).

## [0.8.58] - 2026-09-14

- `create_gable_roof` `slopedEdges` (xMin/xMax/yMin/yMax): lean-to (one edge), gable (two opposite) or hip (four) roofs on the rectangular footprint; `ridgeAxis` becomes optional. Live need: the single-slope fibre-cement roof over a rear stair, falling away from the facade, so the enclosure walls can attach to a real roof.

## [0.8.57] - 2026-09-14

- `create_room` `phaseName`: rooms of an existing-building survey in the "Existente" phase (`NewRoom(level, point)` always used the last phase, so the rooms stayed out of the existing-rooms schedule and views): an unplaced room of the phase goes into the plan circuit of that phase containing the point.
- `create_direct_shape` regenerates before reporting the bounding box.

## [0.8.56] - 2026-09-14

Facade fidelity for the existing building survey (user: Street View is the reference, the model must match it):
- Add `create_railing`: railing along an open polyline on a level (balcony guards, stair and ramp handrails), optional base offset, host (follows a sloped ramp or stair) and flip.
- Add `create_direct_shape`: DirectShape from boxes, cylinders and extrusions (AC enclosures, meter boxes, downpipes, masts) with an optional material.
- `edit_curtain_wall` `removeSegments`: remove one segment of a grid line, merging two panels (a double door across two columns under the door head while the lights above keep the line).

## [0.8.55] - 2026-09-14

- `create_stair`: the run path Z is the run base elevation in model coordinates (Revit refused a path at 0 relative to a base level at +4.85: "not a valid location path line"); a rejected run names its index. dryRun now builds the stair inside the edit scope and cancels it, so Revit validates the runs (the geometry-only dry run passed while the commit failed).

## [0.8.54] - 2026-09-14

Survey of an existing building (ramps to the basement, a rear stair):
- `create_floor` `slope {from, to, rise}`: sloped floors (ramps) through `Floor.Create` with a slope arrow; the tail sits at the level plus heightOffset.
- Add `create_stair`: component stair between two levels from straight runs `{start, direction, risers}` with automatic landings (straight, L, U); self-managed `StairsEditScope`; dryRun reports riser height, tread and Blondel; type comfort warnings are reported instead of rolling back.

## [0.8.53] - 2026-09-14

Live point cloud survey (a 4-storey building scanned floor by floor, each floor registered with its own offset and the typical floors rotated 180 degrees):
- `sample_point_cloud`: Revit refuses more than 1,000,000 points per `GetPoints` call; larger requests are split into plan tiles (`tiles`, `tilesAtLimit`). Verified live that the filter planes are in model coordinates and the points come back in the cloud's own coordinates.
- `set_parameter` / `set_parameter_batch`: ElementId parameters accept a name (`PHASE_CREATED` = "Existente", `FAMILY_TOP_LEVEL_PARAM` = "N1-MEZ-AC"); phases had to be set by numeric id. `set_parameter_batch` resolves BuiltInParameter names and refuses ambiguous localized names like `set_parameter`.

## [0.8.52] - 2026-09-14

Point clouds (user: a ReCap cloud duplicated at least twice; "create everything needed to insert and manage"). No ReCap MCP exists and ReCap Pro has no automation API beyond the decap.exe importer, so the work lives in the Revit add-in, which exposes linking, scans, per-view overrides and point reading:
- Add `link_point_cloud`: link .rcp/.rcs (type reused by path), placement origin or center on a level.
- Add `inspect_point_cloud`: instances (path, found status, extents, scans, supported engines); per instance the scans with scanner origin, regions and duplicate hints (same scanner origin, same name up to a copy suffix).
- Add `set_point_cloud_display`: per-view visibility and colour mode (elevation, intensity, normals, fixed colour) for the instance, scans or regions: hide duplicated scans in a working view.
- Add `sample_point_cloud`: points inside a box with density control, z histogram, extents, file output for planners.
- Node `pointCloud.ts` (4 tests): slice RANSAC face extraction (grid dedupe by cell mean: snapping to cell centres split a face into two lines 2 cm apart), duplicated-with-offset detection and removal of the shifted copy, levels from the z histogram.
- Recipes `revit_recipe_plan_walls_from_point_cloud` (slice -> faces -> duplicate shift -> walls) and `revit_recipe_convert_scans` (decap.exe --importWithLicense for .e57/.las/.pts...).
- Also staged from 0.8.51 work: `create_floor` holes and near-axis snapping, `planParkingFromCad`.

## [0.8.51] - 2026-09-11

User review of the traced offices ("these misalignments make no sense; delete the segments and extend the remaining one to the perpendicular wall"; red DWG lines are sliding doors):
- Add `merge_walls`: replace collinear segments by one wall on the longest centerline, extended to the crossing walls, recreating hosted doors/windows with type and flips. Nudging with move_element could not fix steps: a joined wall drags its collinear neighbours.
- `set_parameter`: accepts BuiltInParameter names; a localized name that repeats among writable parameters is refused with the candidates (a curtain wall type has two "Layout": the vertical grid could not be turned off). Type-driven curtain grid lines cannot be deleted: use a type without that layout.
- Live on 0.8.51: merge_walls on the 5-piece east facade (33.96 m, 3 inserts recreated) after moving one door 8 cm off a joint ("insert conflicts with joined wall").
- `merge_walls`: read type/id before deleting the segments (live InvalidObjectException on the first call).
- `edit_curtain_wall`: `removeGridLines` (applied first; unpins type-driven lines). The entrance door came out 1.59 m instead of 3.27 m because the glass type's grid had lines inside the opening. Live on 0.8.51: grid lines + door panel in the manager's glass partition and the entrance bay.
- `snap_wall_ends`: an end that already touches another wall's end or sits inside its band is satisfied (live: two joined facade pieces were pulled 57 cm onto a cubicle divider); ties prefer the wall the end lands on. 7 tests.
- Planner: `planWallAlignment`, `planSlidingDoorsFromCad` (leaf glass strips -> openings with leaf count and host). 13 Node tests.
- Live on 0.8.50: snap_wall_ends by pairs (13 committed), set_view_crop (review view needs phase "Construção nova"), sliding doors from A-DOOR-GLAZ replaced by the office families `_ARQPWR_PORTA Deslizante 1/2 folhas` loaded from the local library, collinear chains merged by delete+create, warnings 33.

## [0.8.50] - 2026-09-11

Tool gaps found while modeling the Ford DWG pilot:
- Add `set_view_crop`: crop a plan view to a model rectangle in meters (or clear) before get_view_image; a whole-site export left doors invisible. Refuses scope-box crops.
- Add `edit_curtain_wall`: list panels, add vertical/horizontal grid lines through points (U/V direction verified and swapped if needed), change the panel at a point to another type (curtain door panel). Hosted door families cannot go into curtain walls; the showroom entrance needs this.
- `create_floor`: `heightOffset` (canopy, steps, paving without a level per height) and `structural`; reports top/bottom elevations.
- `create_wall`: `locationLine` (default centerline, start/end verified on it, wall moved if the curve drifted) and `wallTypeId`. Traced walls kept the template's "finish face exterior" location line.
- Add `snap_wall_ends`: close L/T/collinear gaps and overshoots between wall ends within a tolerance (pure `WallEndSnapper`, 5 tests), allow joins, keep hosted inserts in place. Walls traced from the DWG did not meet and rooms leaked (WC room read 724 m2).
- Add `create_room_separators`: room separation lines on a level plan (showroom glass stops short of the columns).
- `list_families`: `offset` + total/hasMore/nextOffset. Offset was ignored and paging returned the same 500 families.
- `revit_recipe_plan_walls_from_cad`: `singleLineRegions` and `doorLayers` return single-line partitions and doors with the wall plan.

## [0.8.49] - 2026-09-11

- Fix `create_column`: the family type resolver kept only the last category of its list (architectural columns), so every structural column type answered "No FamilySymbol found". Found live placing facade columns; `place_family_instance` with category OST_StructuralColumns was the workaround.
- Planner (`cadWalls.ts`, 11 tests): `planSingleLineWalls` (partitions drawn with one line: unpaired lines inside regions, trimmed where they overlap planned walls or a twin line, which Revit refuses as overlapping walls) and `planDoorsFromCad` (quarter swing arcs -> host wall, opening center, width, hinge, swing side). `unpairedLimit`.
- `scripts/build-cad-walls.mjs`: single-line groups, explicit `extraWalls`, rule `baseOffset`, `maxLength`, vertical `bands` (glass below/above a transom), doors stage skipping low or curtain hosts. `scripts/build-cad-structure.mjs`: levels, column/beam types (b/h in meters), facade columns in the gaps between glass runs with per-facade size/offset/rotation, transom beams.
- Live on 0.8.49: create_column finds structural types; service block (workshop shed, parts room) built by `scripts/cad/teste-dwg-servico.mjs`, warnings 33.
- Live on 0.8.48 ("teste dwg", warnings 33 throughout): rebuilt with the user's facade images - 25 walls, 22 glass panels in bands, 18 single-line partitions, 9 columns matching the DWG rectangles to the millimetre, 2 beams, 8 doors, floor and roof slab, save_document after each stage.

## [0.8.48] - 2026-09-11

- Add `import_cad`: link (default) or import a DWG/DXF into a view with unit, custom scale, placement and color mode; plan views get a view-specific underlay. Reports the instance, layers and extents in meters. Asked for to model a project that only exists as a DWG.
- Add `inspect_cad` (read-only): list CAD instances; per instance a layer/block summary or paginated plan curves in meters (lines, arcs, circles, polylines, tessellated curves) filtered by layer and bounding box.
- Add `create_wall_type_variant`: duplicate a basic wall type with a total width (structural core layer absorbs the difference) or explicit layer widths; same name and width is reused.
- Add Node recipe `revit_recipe_plan_walls_from_cad` with a pure planner (`cadWalls.ts`, 6 tests): pairs parallel face lines into centerline walls, drops finish lines nested inside thicker walls, snaps thicknesses, bridges T junctions and openings, extends ends to L/T corners, flags doors from swing arcs or door blocks, lists unpaired lines. Proposal only; tuning it needs no Revit restart.
- Planner refined on the live DWG (Node only, no add-in change): overlapping bands merge into one wall (drywall drawn with four lines), nearest thickness snap with `snapTolerance`, `minWallLength` with `shortPieces` (columns), `excludeRegions`. 8 tests.
- `scripts/build-cad-walls.mjs` + `scripts/cad/teste-dwg.json`: plan, classify (thickness/region/outside building -> wall type + height), create per group in one batch (dryRun first), teardown. See docs/CAD_TRACING.md.
- Live on 0.8.48: save_document (save as + save), import_cad (unit auto gave 0.08 m; unit m + center = 78.5 x 110.8 m), inspect_cad summary/curves, 24 walls + 5 glass runs created from the plan through batch, warnings unchanged (33).
- Live on 0.8.47: list_recent_documents read the UTF-16 Revit.ini (query "teste").

## [0.8.47] - 2026-09-11

- Add `save_document` (self-managed, not batchable): saves the target document to its file, or a copy with `saveAsPath` (+ `overwrite`, `compact`). Refuses never-saved documents without a path, read-only files, open edit modes and save-as of workshared models; verifies the document is no longer modified and the file was written. Asked for after a whole roof lived only in memory because the agent could not save.
- Add `list_recent_documents` (read-only): Revit's recent file list from Revit.ini of the running version, with existence, size, last write and open state; `query` filters by name ignoring accents, case and separators.
- Add `open_document` (UI action): open a .rvt/.rfa by `path` or by a spoken `name` resolved against the recent list (ambiguous names return candidates), activated or windowless (`activate:false`). Refuses newer-version files, requires `allowUpgrade` for older ones and `detachFromCentral` for central models; an already open file is only activated.
- `scripts/verify-local-addin.ps1` expects the staged manifest version instead of a fixed 0.8.40.

## [0.8.46] - 2026-09-11

- Eave detail from user review of the live section: `plan_purlin_roof` adds `plateAlignment:"outer"` (plate flush with the wall outer face, blocking in that plane with its top at the inner edge), `fasciaRecess` (eave/verge boards set back from the tile edge; rafters end behind the eave board; plates, purlins and edge rafters stop behind the verge boards) and `fasciaAutoHeight` (eave board = rafter plumb depth). Members report `cutByCovering`; plan reports eave data for hanging the gutter.
- Add `join_geometry`: cutter (roof covering) trims intersecting boards; verifies join, cut order and volume decrease.
- `create_roof_edge_sweep`: `horizontalOffset`, `verticalOffset`, `flipHorizontal`, `flipVertical` (HostedSweep), reported back.
- Live on 0.8.45: create_profile_family ridge cap 45 degrees, duplicate FasciaType with new profile, slopeCheck warning on the 20 degree office cap.

## [0.8.45] - 2026-09-11

- Add `create_profile_family` (self-managed transactions, not batchable): builds a profile family from a closed loop or a generated `ridgeCap` whose wings follow any roof pitch, saves it under %TEMP%\RevitMCP\profiles and loads it. The office metal ridge cap profile is fixed at 20 degrees and floated ~17 cm above the 45 degree pilot roof.
- New `ExecutionKind.SelfManagedWrite`: dispatcher opens no transaction; command honours dryRun; rejected in batches.
- `duplicate_family_type` accepts any element type (fascia/gutter/roof/wall types), so a sweep type can get a new profile.
- `create_roof_edge_sweep`: verifies with real solid geometry (element bounding boxes of sweeps are inflated) and reports `slopeCheck` (dominant profile slope vs roof pitch, warning on ridge mismatch).
- `place_curve_family_instance`: reports solid extents and `profileSide` (left/right of start->end).
- Live on 0.8.44: copy_elements_from_document (5 showroom types), create_roof_edge_sweep with office types, create_ceiling perimeterGap, place_curve_family_instance (4 tabicas), batch and UI actions restored.

## [0.8.44] - 2026-09-11

- Fix 0.8.43 regression: documents were compared by object reference, but the Revit API returns a new Document wrapper on each access. Live, every multi-step write batch failed ("must target a single document"), every UI action was refused and list_open_documents reported the active document as inactive. Now uses Document.Equals.
- `activate_document`: `allowOpen:true` with `path` opens a closed file and activates it.
- Live-validated on 0.8.43 (single commands): create_profile_wall with base/height normalization (gables 2.80..6.0097 m on the wall centre plane), create_opening_in_wall on profile walls, create_roof_edge_sweep (ridge fascia and eave gutters, 9.20 m each, removed after check), documentTitle targeting and document stamp.

## [0.8.43] - 2026-09-11

- Document targeting: every command accepts `documentTitle`/`documentPath` (resolved per call, batches must target one document); every envelope reports `document`. UI actions stay on the active document. Found live when the user opened the showroom and reads silently hit it.
- Add `list_open_documents`, `activate_document` (UI action, open saved documents only) and `copy_elements_from_document` (elements or types from another open document, same-name destination types reused) to reuse the office showroom examples.
- Add `create_roof_edge_sweep`: Fascia (ridge caps, flashings) or Gutter sweeps on classified roof edges (ridge/eave/verge), one per edge, verified against the edge.
- `create_profile_wall`: 0.8.42 still produced an ordinary level-to-level wall (solid 0..2.9 m for a 2.8..6.0 m profile). Now pins base offset/unconnected height, reports full diagnostics (solid, bbox, sketch, constraints) and offers `verification:"sketch"` with a mandatory post-commit geometry check.
- `analyze_wall_roof_interface`: faces report their line origin (Wall.Orientation can point inside).
- Add `place_curve_family_instance`: line-based families (e.g. ceiling tabica) hosted on a planar face or on a level; `place_family_instance` only handled point placement.
- Live Revit validation pending reload.

## [0.8.42] - 2026-09-11

- Add `create_framing_member`: structural framing between absolute 3D points (sloped rafters), with z/y justification, cross-section rotation, structural usage, comments tag and joins disallowed by default; verifies the location line after commit. `create_beam` flattens both ends to the level and cannot place rafters.
- Add `create_roof_type_variant`: duplicate a roof type with explicit layers (tile, battens, membrane, boarding) and verify them.
- Add `plan_purlin_roof` (read-only): layout of a timber purlin roof on masonry gables — wall plates, purlins, ridge beam, rafters clear of gable bands, blocking, fascia/verge boards, covering base elevation over the rafters, gable profile walls with perimeter joint gap, purlin pockets and joint trims. Pure planner with unit tests. Layout only, not structural design.
- `set_parameter`: length-dimension specs other than Length (e.g. framing section Largura/Altura) now accept meters/feet; refused live on 11/09.
- Live Revit validation pending reload.

## [0.8.41] - 2026-09-11

- Safety: a preview never commits. The HTTP server now honours `dryRun` from query, envelope, `params` or any batch step; previously `params.dryRun` was ignored and committed (observed live). MCP tools `revit_create_floor_type_variant` and `revit_set_family_orientation` declared dryRun but forwarded it inside params; both now use the write forwarder, and `fwd` strips/honours dryRun too.
- Fix `create_profile_wall` verification: 0.8.40 compared the element bounding box and failed live for every profile (even a 0..3 m rectangle). Now checks the regenerated solid Z range and reports solid and bounding-box values on mismatch.
- Add `analyze_wall_roof_interface` (read-only): exact piecewise underside along both wall faces, eave wedge, uncovered spans and proposed convex profile for gable infill. Revit-independent math with unit tests.
- Add `detach_wall_top` (Revit 2026 `Wall.RemoveAttachment`): reverses a top attachment; reports solid top before/after. Gap found when an attachment committed by mistake could not be undone through the tools.
- Add `inspect_scope_boxes` (read-only): rotation-aware extents, bound datums/views and model elements outside.
- Live Revit validation of these changes pending reload.

## [0.8.40] - 2026-09-11

- Add `create_profile_wall`: native independent basic walls from strictly convex vertical polygons, with finite/coplanar/short-edge checks and elevation readback. Useful for gable infill when roof attachment is unsuitable; no automatic roof linkage.
- Expose profile-wall creation and roof attachment in the architecture tool profile.
- Live Revit validation remains pending reload.

## [0.8.39] - 2026-09-11

- Native wall top attachment to roof (Revit 2026), pending live validation.
- Correct roof slope conversion to rise/run using tangent.

## [0.8.38] - 2026-09-11

- Verify native gable roof boundaries and slopes, reject unsupported roof types, and report creation failures.
- Add rectangular ceiling perimeterGap (geometric setback only). Live Revit validation pending.

## [0.8.37] - 2026-09-10

- Add planar-face inspection with vertical XY probes and idempotent family orientation updates. Live Revit validation pending.

## [0.8.36] - 2026-09-10

- Add create_floor_type_variant: duplicate compound floor layers with explicit widths and optional assignment to selected floors; preserve source type and materials. Native Revit validation pending.

## [0.8.35] — 2026-09-10: local modeling pilot and transaction diagnostics

- Add get_warnings, get_type_structure, rectangular create_gable_roof and local RFA search.
- Add wall baseOffset, ceiling heightOffset, floor-relative insertion, planar wall-face hosting and placement inspection.
- Transactions now capture failures and roll back on warnings/errors instead of silently deleting warnings or waiting for failure resolution UI. This intentionally supersedes SuppressWarningsOnCommit.
- Check transaction commit status; failed batch steps use subtransactions to avoid partial side effects when continuing.
- Warning rollback reports failure GUIDs and element IDs; step IDs in rolled-back batches are not persistent.
- Known limit: dryRun rolls back before commit and does not exercise commit-time warnings. General dialogs, complete roof detailing and all autonomous workflows are not implemented.
- Local development build; new failure handling requires live Revit validation before production use.

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.34] — 2026-09-04: `check_clearance` validates `axis` and `direction`

Found while verifying the 0.8.33 sweep: a test asserted the wrong thing, and chasing why exposed a
real defect that predates the sweep.

### Fixed

- **`axis` and `direction` accepted any value and silently picked a branch.** Both are documented as
  closed sets (`"bbox" | "Z"`, `"below" | "above"`) but each was compared against a single value with
  everything else falling through:

  ```csharp
  var useRaycast = axis.Equals("Z", ...);        // anything else -> bbox
  var isDown = direction.Equals("below", ...);   // anything else -> above
  ```

  So `axis: "vertical"` ran the **bbox** algorithm — a different measurement entirely — while the
  response echoed `"axis": "vertical"` back, and a caller reading their own input reflected would
  believe it had been honoured. Worse, `direction: "belwo"` fired the raycast **upward**: a reversed
  measurement returned as success.

  Both now reject an unlisted value with `invalid_parameter` naming what was allowed. Case-insensitive
  matching is unchanged, so `"BBOX"` and `"z"` still work.

  This is the same defect class as `units: "mm"` in 0.8.29 and the 3D-view dimension in 0.8.32: the
  command reports success for something it did not do.

## [0.8.33] — 2026-09-04: finish the coercion sweep — no command reads JSON unguarded

Closes the debt left by 0.8.28 and 0.8.29, and promised to AutomatedSpatialQC in the v0.8.29 handoff
("28 file command vẫn gọi GetValue<>() trần... sẽ quét nốt").

### Fixed

- **Every remaining direct `JsonNode.GetValue<T>()` in the command files now goes through the guarded
  helpers.** 0.8.28 fixed object keys and 0.8.29 fixed one array, leaving 76 call sites across 28
  files where a type mismatch still threw `InvalidOperationException` — reported as HTTP 500
  `command_failed` with a message about .NET types instead of 400 `invalid_parameter` naming the
  parameter.

  Of the 76, **14 were already safe** and were left alone: `TryGetValue<T>` calls, and the deliberate
  try-each-type fallbacks in `WhereSupport.GetNum` and `ImportParametersCommand`. The other **62 were
  converted**, in four shapes:

  | Shape | Where | Became |
  |---|---|---|
  | `n.GetValue<long>()` over an `ids` array | 10 commands incl. `delete_elements`, `select_elements`, `zoom_to_elements` | `P.LongFrom(arr[i], $"ids[{i}]")` |
  | a single required node | `apply_view_template`, `check_clearance`, `export_view_pdf`, `get_element_rooms`, `list_spaces`, `set_parameter_batch`, `copy_parameters`, `override_element_graphics` | `P.LongFrom` / `P.StrFrom` / `P.DblFrom` |
  | `obj["k"]?.GetValue<T>() ?? default` | `check_clearance`, `create_aligned_dimension`, `import_parameters`, `override_element_graphics` | `P.StrOrNull` / `P.IntOr` / `P.BoolOr` |
  | the parameter-write switch | `set_parameter`, `set_parameter_batch`, `update_where` | `P.StrFrom` / `P.IntFrom` / `P.BoolFrom` / `P.DblFrom` / `P.LongFrom` |

  The last shape is the one that changes behaviour most usefully. Those switches pick a type from
  Revit's `StorageType` and then read the JSON as exactly that, so writing a String parameter with a
  JSON number threw. `set_parameter {value: 101}` on a text parameter such as Mark now writes "101"
  instead of returning a 500 — `import_parameters` already coerced this way on purpose, and the three
  write paths now agree with it.

- Removed `ImportParametersCommand.RequireLong`, a private duplicate of what `P.Long` does, and does
  better: it also accepts numeric strings and integral doubles, and its error names the expected type
  rather than saying "cannot parse as long".

### Added

- `P.StrFrom`, `P.IntFrom`, `P.BoolFrom` complete the bare-node set alongside `LongFrom` / `DblFrom`.
  Each takes a label (`"ids[2]"`, `"set.value"`) that appears verbatim in the error, so a caller is
  told which element of which array was wrong.
- 10 tests covering the new helpers, including that a null node is rejected rather than defaulted —
  the failure mode that makes a mistyped optional silently change a query's scope. 170 C# tests total.

## [0.8.32] — 2026-09-04: `create_aligned_dimension` refuses a 3D view instead of faking success

From an AutomatedSpatialQC handoff (`HANDOFF_dimension_in_3d_view.md`, 2026-09-04), which framed it
using the rule this repo set for itself at `spatial_create_model_line`: *a silent skip looks
identical to success from the caller's side*.

### Fixed

- **A dimension created in a 3D view returned `ok: true`, a real `dimensionId` and a correct `value`
  — and Revit never drew it.** `doc.Create.NewDimension` does not enforce the restriction the Revit
  UI does, so the element existed (`get_element_info` confirmed `OST_Dimensions`) while the view did
  not contain it.

  Their evidence had a control, which is what made it conclusive rather than "we couldn't see it": a
  view-scoped `FilteredElementCollector` reported **0** dimensions in a 3D view and **1** in an
  otherwise identical plan view, while two model lines created the same way reported **2/2 in both**
  and were visually confirmed in the 3D view.

  **The handoff proposed gating on `view is View3D && !view.IsLocked`. Measured on the live model,
  locking makes no difference — the dimension stays invisible after `View > Lock 3D View`.** So the
  guard is on **any** `View3D`, and the error message says the lock will not help, which saves the
  next caller the same experiment.

  Option (a) from the handoff (reject) over (b) (warn): with locking ruled out there is no path on
  which the element becomes useful, so returning an id for it is only ever misleading. No
  `lock_3d_view` command was added — it would open a door that leads nowhere.

### Added

- **`valueMetres` in the response.** `value` is Revit internal units (feet) whatever `units` says,
  because `units` governs the input coordinates only. That asymmetry is easy to misread as metres — a
  silent 3.28x error — and the repo was already inconsistent about it: `spatial_create_model_line`
  returns `length` always in metres. Both numbers are now returned so neither can be mistaken.

### Changed

- Docstring and MCP tool description state the 3D-view restriction, that `units` applies to input
  coordinates only, and the two-reference minimum (already enforced, now documented — the natural
  case of measuring across a single chord gives only one reference).
- `references[].elementId` is read through `P.LongFrom`. It used `refObj["elementId"]!`, so a missing
  key was a NullReferenceException surfacing as a bare 500 rather than a named parameter error.
- Fixed a malformed row in `docs/COMMANDS.md`: the 0.8.31 edit to `get_view_image` left a stray pipe,
  rendering it as a five-column row in a four-column table.

## [0.8.31] — 2026-09-03: `get_view_image` can change resolution; `create_perspective_view`

Both from a World Labs probe handoff (`_probes/worldlabs`, 2026-09-03) building a Revit -> world-model
render loop. Every claim in it checked out against the source.

### Fixed

- **`get_view_image` ignored `dpi` for sizing — every export came back 512 px wide.** The command set
  `ImageResolution` but never `PixelSize`, and under `ExportRange.SetOfViews` `ImageResolution` only
  writes print-DPI metadata into the PNG; Revit's default `PixelSize` of 512 decided the actual
  dimensions. Worse than inert: `dpi` was validated (clamped 36–300, snapped to 72/150/300) and echoed
  back in the response, so it looked like it worked.

  New **`pixelSize`** (default **512**, clamp 128–4096) sets the image width; height follows the view's
  aspect ratio. The default is deliberately the value Revit was already producing, so consumers parsing
  the old small images — bim-orchestrator, RevitAssistant — get byte-identical behaviour until they ask
  for more. `dpi` is kept and now documented as metadata-only.

  The response also carries **`width`** and **`height`**, read from the PNG IHDR chunk, so a caller
  never has to decode the image to learn its size or to check that `pixelSize` was honoured.

  One correction to the handoff's sketch: the property is `ZoomType` of type **`ZoomFitType`**, not
  `ZoomType.FitToPage` as written there.

### Added

- **`create_perspective_view`** (MCP tool `revit_create_perspective_view`) — perspective 3D views with
  the camera at explicit coordinates. `create_3d_view` only makes an isometric view or duplicates the
  active one; nothing in the surface let a caller say where the camera stands.
  - `azimuthsDeg` creates N views sharing **one** eye, the view direction rotated about world Z. That
    is the point of the command: image-based reconstruction fed renders from different eye heights
    folds the recovered floor plan diagonally, so the eye has to be identical across a set.
  - `up` is world Z projected onto the plane normal to the view direction, with a fallback axis for a
    camera looking straight up or down — `ViewOrientation3D` throws if up is not perpendicular.
  - `units` accepts `meters`/`feet` only, rejecting anything else, for the same reason as
    `spatial_create_model_line` in 0.8.29: `P.Xyz` silently treats every non-`feet` unit as metres.
  - A duplicate `viewName` is reported in `warnings` rather than failing the call — the view is created
    and usable either way.
  - Echoes each view's `eye` (in the requested units) and unit `forward` vector, so a caller can verify
    placement without repeating the unit conversion.

### Changed

- Tool surface 93 → **94**; C# commands 100 → **101** (10 hidden, unchanged).
- `P.DblFrom` added alongside `P.LongFrom`, and `get_view_image` now reads `viewId` through the guarded
  path — continuing the array-element half of the 0.8.28 coercion fix.

## [0.8.30] — 2026-09-03: the dockable panes can recover from "paused"

### Fixed

- **A paused panel stayed paused for the rest of the Revit session, and both of its own recovery
  buttons were inert.** Reported by the BIM Orchestrator team on 2026-08-25 while preparing AU demos
  (`revit_mcp_panel_suspend_gap.md`). Verified in the source rather than by reproduction — the fault
  is structural, not intermittent:
  - `_suspended` is set in exactly one place (`Suspend()`) and cleared in exactly one place
    (`Resume()`), and `Resume()` was reachable from exactly one event, `DocumentOpened`.
  - `DocumentClosing` fires for **any** document, including a background one closed while another
    stays open. No `DocumentOpened` follows, so nothing ever cleared the flag. Reproduce: open model
    A, open model B, close A.
  - `EnsureWebViewCore()` returns early while `_suspended` is set, and both "Retry embedded view"
    and "Reload" routed straight into it — so the only affordance offered for the paused state could
    not leave it, silently. `_panelView` is constructed once at startup, so toggling the pane reused
    the same instance and the flag survived that too.

  The reporter's own follow-up smoke on 2026-08-28 did **not** reproduce it, and noted so. That is
  consistent: the smoke left the panel up for 11 minutes without closing a document, which is the
  trigger. Absence of a repro there was not evidence of absence.

  Two changes, matching the report's suggestions:
  - An explicit user action now clears the flag (`ForceRebuild()`): pressing Retry or Reload *is* the
    statement that the transition is over.
  - `ViewActivated` is wired to `Resume()`, so the panel self-heals after a close that leaves another
    document open — Revit activates a view in the survivor. Safe to fire repeatedly: `Resume()`
    collapses bursts through the dispatcher and `EnsureWebViewCore` returns early once the browser
    exists.

  `DocumentClosing -> Suspend()` is unchanged; it is what protects WebView2's interop queue from the
  model-upgrade dialog wedge. The bug was the missing way back, not the suspend.

- **Both panes were affected, not just AutoAudit.** The report covers the AutoAudit pane; the Spatial
  QC pane registered at `App.cs` had the identical event pair and the identical dead-end. Fixed for
  both.

- **The Spatial QC pane called itself "AutoAudit" in every message the user reads.** Both panes are
  instances of `AutoAuditPanelView`, which hardcoded the name in three user-visible strings — so the
  Spatial QC pane announced *"AutoAudit panel is paused"*, offered *"Open AutoAudit in browser"*, and
  said *"AutoAudit keeps working at http://127.0.0.1:8602/ui/"*, pointing at the wrong tool and the
  wrong port in one sentence. The pane name is now a constructor parameter.

## [0.8.29] — 2026-09-03: `isolate_elements_in_view` works again; `spatial_create_model_line`

Both items came from AutomatedSpatialQC handoffs dated 2026-09-03, measured against v0.8.28.

### Fixed

- **`isolate_elements_in_view` failed on every call that passed `ids`** with
  *"Attempt to modify the model outside of transaction"*. The command declared
  `ExecutionKind.UiAction` with the comment *"temporary view mode — no transaction"*, and the
  dispatcher gives UiAction commands no transaction — but `View.IsolateElementsTemporary()` is a model
  change and needs one. The `reset` branch calls `DisableTemporaryViewMode()`, which does not, which is
  why reset kept working and masked the bug. Its sibling commands were unaffected:
  `override_element_graphics` declares `ModelWrite`, `hide_elements_in_view` takes the default.

  **`reset` needed one too.** The handoff reported `reset` as working and concluded
  `DisableTemporaryViewMode()` does not require a transaction. It does — live testing after fixing the
  `ids` branch showed `reset` throwing the identical exception. The earlier "reset works" reading held
  only because reset had never been exercised against a view that isolate had actually modified. Both
  branches now run inside the transaction.

  Fixed by opening a transaction around both branches **while keeping `UiAction`**, rather than by
  promoting the command to `ModelWrite` as the handoff suggested. Promoting it would have made
  `BatchPolicy` reject `[open_view, isolate_elements_in_view, zoom_to_elements]` — a batch may not mix
  ModelWrite and UiAction, and those other two are UiAction, so the natural view-navigation sequence
  (exactly what the reporting consumer's "3D in Revit" button does) would have started failing. Keeping
  UiAction also preserves the dispatcher's dry-run no-op for UI actions.

  Consumer impact: the spatial-qc panel's "Cô lập" button was dead on v0.8.28 and works again.

### Added

- **`spatial_create_model_line`** (HTTP-only, `spatial_*` pack, not an MCP tool) — draws a straight
  `ModelCurve` between two world points. `create_detail_line` cannot serve this: it makes a
  view-specific `DetailCurve` and throws `unsupported_view` in a 3D view by design, so its line does not
  exist in 3D space. A model curve does, and shows in any view that cuts it.
  - `units` accepts **`meters` or `feet` only**. The handoff's spec listed `mm`, but `P.Xyz` treats
    every non-`feet` unit as metres, so `"mm"` would have silently drawn a line 1000x too long. It is
    now rejected with `invalid_parameter` instead.
  - `color` requires `viewId` (a graphic override is per view, and a model curve belongs to no single
    view); `lineStyle` falls back to the default when the named `GraphicsStyle` does not exist. Both
    skips are reported in `warnings` rather than passing silently, since a silently ignored option is
    indistinguishable from an applied one at the call site.
  - Returns `{ id, length }`, length always in metres. The `id` is a real element with a usable
    `GetReference()`, so it can later be passed to `create_aligned_dimension`.

### Changed

- C# commands 99 → **100** (10 hidden — the spatial-QC pack is now 9). MCP tool surface unchanged at
  **93**: the new command is HTTP-only.
- `P.LongFrom(JsonNode, label)` added, extending the 0.8.28 coercion fix to array elements. 29 command
  files still call `GetValue<>()` directly on array items and remain exposed to the bare-500 defect;
  `isolate_elements_in_view` is converted here as the first.

## [0.8.28] — 2026-08-19: a mistyped parameter returns 400, not a bare 500

### Fixed

- **Type-mismatched parameters escaped the error envelope.** Every `P.*` accessor read its value
  through `JsonNode.GetValue<T>()`, which throws `InvalidOperationException` on a mismatch. That
  exception is not a `RevitCommandException`, so it was reported as a generic command failure:
  **HTTP 500** with `code: "command_failed"` and a message naming .NET types rather than the
  offending parameter (*"An element of type 'String' cannot be converted to a 'System.Int64'."*).
  The envelope was present — an earlier revision of this entry claimed it was not, which was wrong;
  the defect is the status and the code, not a missing body. A client fault was being reported as a
  server fault, and nothing in the message said which key to fix. Measured against the live add-in on
  Revit 2027:

  ```
  get_element_info  id=619404     -> ok=True            get_element_info id=999999999 -> 404 + envelope
  get_element_info  id="619404"   -> HTTP 500  command_failed
  get_element_geometry id="..."   -> HTTP 500  command_failed
  list_elements     limit="10"    -> HTTP 500  command_failed
  find_elements     view_id="..." -> HTTP 500  command_failed
  ```

  Genuine domain errors were fine; only the type mismatch broke. Nine accessors were affected
  (`Str`, `StrOrNull`, `Dbl`, `DblOr`, `Int`, `IntOr`, `Long`, `LongOrNull`, `BoolOr`), so in
  practice any command taking a numeric parameter.

  Two things made this worth fixing beyond tidiness. It breaks the envelope contract every consumer
  reads (`DEPENDENCIES.yaml`: *"envelope {ok,data} / {ok,error}"*, *"not_found -> 404, SpatialQC
  degrades on 404"*) — and `spatial_get_room_boundary`, which AutomatedSpatialQC calls directly, was
  one of the affected commands. And an LLM emitting `"5"` instead of `5` is a routine slip, not an
  exotic one; a bodiless 500 gives it nothing to correct from.

  Values now pass through a coercion layer:
  - Conversions that are exact are accepted — a numeric string (`"619404"`) and an integral double
    (`5.0`). Neither can yield a wrong answer, and rejecting them buys nothing.
  - Anything else raises **`invalid_parameter`** (mapped to 400), naming the key, the expected type
    and the offending value.
  - A mistyped value is **never** swallowed into `null` or a default. The unmerged branch version of
    `LongOrNull` did exactly that, and it is the more dangerous behaviour: a dropped `view_id`
    silently widens a view-scoped query to the whole document.

- 20 tests added (`ParamUtilCoercionTests`) covering both directions; the existing 140 still pass.

## [0.8.27] — 2026-08-12: `query_where` / `update_where` / `import_parameters` land on main

Recovered from `feat/clearance-envelope`, the last unmerged work on that branch. Everything else the
two local feature branches carried — the AutoAudit and SpatialQC panes, `get_doors` with swing
geometry, `create_detail_line`, and the whole spatial-QC pack — had already been ported to main by
hand; a content comparison showed only these three commands (plus their shared `WhereSupport` helper)
were still branch-only. `git cherry` reported all 12 commits as unmerged, but that compares patch-ids
and the earlier work was hand-ported, not cherry-picked — the registry comparison is what settled it.

Ported file-by-file rather than merged: the branch predates the 0.8.17 security hardening and still
carries the `REVIT_MCP_AUTH=false` escape hatch in `App.cs`, so a merge would have resurrected it.

### Added

- **`query_where`** (`revit_query_where`, read-only) — deterministic query returning a true `count`
  plus rows. A superset of `find_elements` filtering: adds `regex`, `not_regex`, `starts_with`,
  `ends_with`, `gte`, `lte`, `is_empty`, `not_empty` (13 operators vs 5), and an explicit per-condition
  `scope` (`auto` | `instance` | `type`). `count` stays exact when `limit` truncates the rows.
- **`update_where`** (`revit_update_where`, write, risk `medium`) — sets one parameter on every element
  matching where-conditions, selecting by condition instead of by id list, then **re-reads each written
  value to confirm it actually took**. `atomic` defaults to **true**: a single failed read-back rolls
  the whole call back. Nothing else in the repo verifies its own writes.
- **`import_parameters`** (`revit_import_parameters`, write, risk `medium`) — spreadsheet-shaped import
  where each row is one `(elementId, parameterName, value, units?)`, so different rows may write
  different parameters. Complements `set_parameter_batch`, which writes the *same* parameter to many
  elements and cannot express a per-row parameter. All rows share one transaction = one undo step.
- **`WhereSupport`** — shared helper behind the two where-based commands: scope-aware parameter
  resolution (instance, then Type — many parameters such as "Fire Rating" live on the type, and an
  instance-only lookup silently matches nothing) and a single predictable operator set. Tolerant of
  key aliases small models emit (`parameterName`/`param`/`field`, `op`, `=`/`>`/`!=`).

### Changed

- Tool surface 90 → **93**; C# commands 96 → **99** (9 hidden, unchanged). `revit_query_where` joins
  the `inspection` profile; the two write commands join `editing`.

## [0.8.26] — 2026-08-10: `spatial_get_paths_of_travel` emits the full route `polyline`

Requested by AutomatedSpatialQC (`revit_addin/HANDOFF_get_paths_of_travel_polyline.md`). Their
benchmark now agrees with Revit within ±10% on 69/78 routes; of the 9 outliers, 4 were explained
from existing data (grid quantisation, 8-direction error) and **5 are stuck** — Revit's bounding
box is up to 5.6 m wider, so it demonstrably detours, but a bounding box cannot say *where*.
Furniture was ruled out (88 obstacles vs 10 changed 0/78 routes). The route vertices are the one
missing input, and the command already had them in hand.

### Added

- **`polyline`** on every element in `spatial_get_paths_of_travel` — all route vertices, metres,
  world XYZ, same frame as `from`/`to` (shares the same `XyzToJson`). Always emitted; no opt-in
  flag (78 routes × 2–8 curves is a few thousand numbers — a parameter would be YAGNI).

  Built from `Curve.Tessellate()`, not the curves' endpoints: a PathOfTravel can contain an **Arc**
  where Revit rounds a corner around an obstacle, and taking only an arc's two ends would drop the
  exact detour the consumer is measuring. The shared vertex between consecutive curves is
  de-duplicated so no zero-length segment appears.

  Worth knowing before it reads as a rounding bug: summing the polyline's segments matches
  `lengthMeters` almost exactly for an all-`Line` route, but comes out slightly **short** when an
  Arc is present — a tessellated chord is shorter than its arc.

Purely additive: no existing field changed, no other command touched, and the consumer ignores
unknown keys, so an old spatial-qc against this build is unaffected. The **command** contract needed
no version bump; the **add-in** gets one because 0.8.25 is already tagged and pushed, and editing a
released version's changelog would falsify it. RevitAssistant pins by tag, so a new tag is also how
this reaches them.

## [0.8.25] — 2026-08-09: `create_detail_line` honours `color`/`weight`; `spatial_create_path_of_travel`

Two AutomatedSpatialQC handoffs closed in one build.

### Fixed

- **`create_detail_line` no longer silently drops `color` and `weight`.** The command had accepted
  both from callers since `revit_adapter.py::mark_min_in_revit` shipped and had never read either —
  every line it has ever drawn came out in the view's default style. The consumer only tolerated it
  because the call sits inside a bare `try/except: pass`, so the miss was invisible. Colour and
  weight are view-specific graphic overrides rather than curve properties, so they apply via
  `View.SetElementOverrides` — but **in the same transaction that creates the curve**, so one call
  still yields one finished line instead of a follow-up `override_element_graphics` round-trip.

  The two are **independent**: `weight` alone sets just the weight, against the default colour.
  The handoff nested `weight` inside the `color` branch and left the alone-case to the maintainer;
  neither consumer sends it alone today, and silently ignoring a parameter is exactly the bug being
  fixed here. `weight` is validated to Revit's pen range 1-16 — out of range otherwise throws deep
  inside `SetProjectionLineWeight` with a message that never names the parameter.

- **`create_detail_line` returns `id`.** It returned only `detailLineId`, but the consumer reads
  `ln.get("id")` — so `mark_min_in_revit`'s `created["lines"]` has always been a list of `None`.
  `id` is now returned alongside `detailLineId`; every other `create_*` command already uses `id`
  as the primary key. `detailLineId` is kept, so nothing that reads it breaks.

### Added

- **`spatial_create_path_of_travel`** (HTTP-only spatial-QC pack; not an MCP tool) — places Revit's
  native `PathOfTravel` between two points in a floor plan view, the WRITE mirror of
  `spatial_get_paths_of_travel`. Returns `{ id, viewId, lengthMeters, timeSeconds, warning }`.
  Verified live on R27 Snowdon: the pair taken from a hand-placed PoT reproduces Revit's own
  numbers to 0.14% (15.026 m / 11.204 s vs 15.047 m / 11.220 s).

  Everything below was **measured against the live add-in** — each point differs from what the
  handoff's sketch (or the API docs' surface) suggested:

  - **Failure arrives BOTH ways.** `PathOfTravel.Create` has an out-`PathOfTravelCalculationStatus`
    overload, but not every failure routes through it: coincident endpoints and points outside the
    view crop **throw `Autodesk.Revit.Exceptions.InvalidOperationException`** instead of returning
    `StartAndEndPointsTooClose`/`PointOutsideActiveCrop`. Both paths map to one `no_route` contract
    with Revit's own sentence passed through. The catch is at the `Autodesk.Revit.Exceptions` base
    (derives from `ApplicationException`, not the BCL types — 0.8.23's trap, again).
  - **`ResultAffectedByCrop` is a success, not a failure.** Revit computes and places a real route
    and reports the crop influenced it. Measured: the exact endpoints of an EXISTING hand-placed
    PoT come back with this status — rejecting it would fail on essentially every cropped
    life-safety view. Accepted, surfaced as `warning`; a genuinely failed status still deletes the
    partial element before erroring (the dispatcher commits on the way out, and a half-computed
    PathOfTravel would otherwise survive).
  - **The crop warning dialog fires at transaction COMMIT, not inside `Create`** — it is Revit
    failures-processing, so no suppression scoped to the command body can catch it. A modal dialog
    on the UI thread deadlocks the whole add-in for a headless caller (measured: 400+s hung, every
    later request queued, until a human clicked OK). New opt-in
    **`IRevitCommand.SuppressWarningsOnCommit`**: the dispatcher installs an `IFailuresPreprocessor`
    that deletes **warnings** at commit (errors untouched) — for this command and for any batch
    containing it. Default false: the other 95 commands keep Revit's normal warning behaviour.
    The suppressed condition is NOT swallowed — it is exactly the `warning` field, driven by the
    out-status (deterministic), with `DialogBoxShowing` capture kept as a fallback for
    Create-time dialogs.
  - Length/time/level parameters are the ones verified in 0.8.24 (`CURVE_ELEM_LENGTH`,
    `PATH_OF_TRAVEL_TIME`). **Consumer note:** first `Create` in a session can take 90+ s on a
    real model (route-analysis warm-up; ~10 s warm) — HTTP callers should budget a ≥3-minute
    timeout for this command.

## [0.8.24] — 2026-08-09: `spatial_get_paths_of_travel` — read Revit's own Path of Travel elements

Requested by AutomatedSpatialQC (`revit_addin/HANDOFF_get_paths_of_travel.md`, branch
`feat/pot-parity`): the READ side of `bim-nav benchmark-pot` (SPEC_pot-parity.md Block C). A client
who trusts Revit's native `Analyze > Path of Travel` drops a few `PathOfTravel` elements into the
model by hand; the consumer reads them back, reruns the same (from, to) pair through its own
occupancy-grid router, and prints both distances side by side — a credibility benchmark, not a
verdict. Ships independently of the WRITE side (`spatial_create_path_of_travel`, still pending).

### Added

- **`spatial_get_paths_of_travel`** (HTTP-only spatial-QC pack; not an MCP tool) — every
  `PathOfTravel` element with `levelName`, `from`/`to` (route-curve endpoints, world metres,
  Revit frame), `lengthMeters`, `timeSeconds`, all read verbatim from the element.

### Notes — the handoff's two flagged unknowns, resolved against RevitAPI.dll (2027 metadata)

- Route geometry: `PathOfTravel.GetCurves()` (the sketch's `GetCurve()`/`NumberOfCurveLoops` does
  not exist). `from`/`to` = first curve's start / last curve's end; elements whose route failed to
  compute (`GetCurves()` empty) are **skipped** — a 0-length row would read as a real measurement.
- Parameters: there is **no** `PATH_OF_TRAVEL_LENGTH` and no "Actual Length"/"Actual Time".
  Length is `CURVE_ELEM_LENGTH` (the UI "Length"), with a curve-length-sum fallback; time is
  `PATH_OF_TRAVEL_TIME` (internal unit seconds), emitted as `null` when absent — never a fake 0.
  Level comes from `PATH_OF_TRAVEL_LEVEL_NAME` (the UI "Level"), falling back to the owning view's
  `GenLevel`. `HANDOFF_create_path_of_travel.md` §2 asks for exactly these names when the WRITE
  side gets picked up.

## [0.8.23] — 2026-08-05: `configure_schedule` can filter on a numeric field

Reported by bim-orchestrator, which auto-creates the native Revit schedules a reviewer uses to
re-check a compliance run. Four of its five schedules worked; the one carrying a threshold —
`Width less than 900 mm` — produced a schedule listing **every** door.

### Fixed

- **`configure_schedule` filters accept a number** (`filters[].value` is now
  `string | number` on the bridge, was `string` only). A JSON number previously failed MCP input
  validation *above* the bridge, and the SDK returns that as plain text, so the caller got no
  `{ok,error}` envelope at all — just an unparseable result and a schedule left unconfigured.
- **A numeric filter now actually reaches Revit.** `ConfigureScheduleCommand` read the value with
  `GetValue<string>()` *outside* the per-filter `try` (a number threw and failed the whole
  command), then only ever called the `string` `ScheduleFilter` constructor — which Revit refuses
  on a Double/Integer field. The refusal was caught and demoted to a `warnings` entry, so the
  response stayed `ok:true` and the filter was **silently absent**. The value is now read as a raw
  `JsonNode` and offered to Revit as an ordered list of overloads (`double` → `int` → `string` for
  a JSON number; `string` → `double` → `int` for a JSON string), letting Revit's own validation
  pick the one that fits. Measured on R27 Snowdon: `Width < 2.9527559055118114` went from 100 rows
  (the unfiltered read cap over 149 doors) to **13**, identical for the number and the string form.

  The retry wraps `ScheduleDefinition.AddFilter`, **not** the `ScheduleFilter` constructor. The
  constructor happily builds a string filter for a Double field; Revit only refuses it on add —
  its message ends `Parameter name: filter`, naming `AddFilter`'s argument. A ctor-level ladder
  compiles, looks right, and never reaches its second rung (measured: it fixed the number form and
  left the string form failing exactly as before). Each rung also catches `Exception` rather than
  `ArgumentException`: Revit throws from `Autodesk.Revit.Exceptions`, whose `ArgumentException`
  does not derive from the BCL one, so a type-filtered ladder stops at rung one. A rung can only
  succeed by actually adding the filter, and the last refusal is rethrown verbatim — a genuinely
  invalid value still becomes the same `warnings` entry it does today (verified).

  The `int` overload is offered only when the value is integral: truncating 2.95 → 2 on an Integer
  field would yield a filter that applies cleanly and quietly means something else.

Two things that look like bugs and are not, now documented and covered by tests:

- The value is in Revit **internal units** and passes through untouched — callers convert
  (900 mm → 2.9527559055118114 ft) and an addin-side conversion would corrupt every filter.
- Numeric strings parse with **invariant culture**. Current-culture parsing on a de-DE machine
  reads `"2.9527559055118114"` as ~2.95×10¹⁶ — the filter would apply and match nothing.

Filters on text fields are unchanged: the ladder tries the `string` overload first, so
`Mark equals "S10"` still resolves as text and not as a number.

## [0.8.22] — 2026-08-03: resolve elements by `UniqueId`, never by a derived id

Follow-up to 0.8.21. Exposing `uniqueId` let us finally *verify* how callers were turning an
ACC `externalId` back into a Revit element — and the answer was: incorrectly.

### Measured

Against the live Revit 2027 add-in and the Autodesk Model Derivative API (project
"Ken - MCP Testing", models Architectural / Structural / HVAC):

- The trailing 8 hex characters of a `UniqueId`, parsed as hex, **are** the `ElementId`:
  27/27 across three models, 9/9 on a random host sample spanning 9 distinct episode GUIDs.
- The XOR heuristic circulating downstream (`hex[37..45] ^ hex[28..36]`) matched **0/27**.
  It is not a stale formula — it was never correct. Autodesk documents the layout as
  `EpisodeId(8-4-4-4-12) + "-" + hex(ElementId)`, with no XOR anywhere.
- ACC `externalId` is byte-identical to Revit `UniqueId` for the same element (verified on
  ids 619340 and 619404 against the running add-in).

### Added

- **`find_element_by_unique_id`** (MCP tool `revit_find_element_by_unique_id`) — resolves an
  element through `Document.GetElement(string)`, so no id derivation happens at all.
  - `linkId` searches exactly one `RevitLinkInstance`; `searchLinks=true` sweeps every loaded
    link when the host has no match. Returns `foundIn` ("host"/"link") plus link context, and
    transforms link bounding boxes into host coordinates.
  - Guards a sharper hazard than the bad formula: `ElementId` is numbered **per document**, so
    an id lifted from a linked model can silently resolve to a *different* real element in the
    host. (In the Snowdon sample the 5 ids tested happened to miss — chance, not safety: the
    host id range 593k–2.8M fully overlaps the HVAC and Structural ranges.)
- **`get_linked_elements` now returns `uniqueId`** per element. Without it, elements inside a
  link — where clash-driven tools do most of their work — had no cross-document identity at all.

### Changed

- Tool surface 89 → **90** (94 C# commands, 7 hidden). `revit_find_element_by_unique_id` joins
  the `core` profile.

### Fixed

- **CI published unusable release archives.** The `release` job kept its own hand-written copy
  list instead of calling `scripts/build-release.ps1`, and that list was never updated after the
  0.8.15 packaging fix. Because the job only runs on a tag, and no tag was cut between 0.8.19 and
  0.8.22, the drift sat undetected until v0.8.22 was tagged. The 64 KB per-version zips it
  produced were missing:
  - `RevitMCP.Core.dll` — the entire command kernel, so the add-in had no commands at all;
  - `dist/revitClient.js` and `dist/recipes.js` — both imported by `index.js`, so the MCP server
    died at startup with `ERR_MODULE_NOT_FOUND`;
  - the three WebView2 assemblies the AutoAudit panel loads.

  The job now runs `scripts/build-release.ps1`, which already gates on 26 required files *and*
  resolves every relative import in the emitted JS before zipping — so an incomplete bundle now
  fails the build instead of being published. The v0.8.22 assets were replaced with the verified
  1.08 MB bundle (SHA-256 `19C24DFF…3386`); the broken per-version zips were deleted. The same
  broken zips are still attached to **v0.8.19** alongside its good combined bundle.

## [0.8.21] — 2026-07-27: `get_element_info` exposes `uniqueId`

### Added

- **`get_element_info` now returns `uniqueId`** — the element's stable Revit `UniqueId`
  (45-char `<guid-36>-<8 hex>`). Purely additive: no field removed or renamed, envelope
  `{ok,data}` unchanged, input schema unchanged, tool/command count unchanged (gate stays green).
  - **Why:** downstream consumers that receive an ACC / BIM 360 Model Coordination `externalId`
    (which *is* the Revit UniqueId) previously had to reverse-engineer the `ElementId` with the
    XOR heuristic (`parseInt(uid[37:45],16) ^ parseInt(uid[28:36],16)`) and could not verify the
    result — a wrong guess silently targets a different element. Returning `UniqueId` lets a caller
    match the mover element exactly before any mutation. (Handoff: ClashDetection v1.2, 2026-07-27.)
  - Adds ~45 bytes/element to the response; nowhere near the 1 MB envelope ceiling.

## [0.8.20] — 2026-07-25: AutoAudit dockable panel lands on main (installer no longer wipes it)

### Added

- **AutoAudit DockablePane (WebView2) is now part of `main` and the installer.** The panel — a thin
  embedded browser onto the AutoAudit UI (`http://127.0.0.1:8601/ui/`, configurable via
  `revit-mcp-panel.json`) — previously lived only on the unmerged `feat/spatialqc-panel` branch, so
  every run of the v0.8.18/v0.8.19 one-shot installer (built from `main`) overwrote the deployed
  add-in with a panel-less DLL. That regression class is closed: the panel ships in the DLL the
  installer installs.
  - Ported selectively from the branch: `Panel/{AutoAuditPaneProvider, AutoAuditPanelView,
    PanelConfig, ShowAutoAuditPanelCommand}.cs` + registration in `App.cs` + WebView2 refs in the
    csproj — **without** taking the branch's stale `App.cs`/csproj (which predate the 0.8.17
    security hardening and the build-truth stamp; a straight merge would have reverted both).
  - Behaviours preserved from the live-verified branch build: panel registration in its own
    try/catch (a panel failure can never take down the MCP server), the visual-tree-before-init
    WebView2 fix, suspend/resume around document transitions (archi-lab WebView2 gotcha), and the
    AssemblyLoadContext resolver for the loose WebView2 assemblies.
  - New ribbon tab "AutoAudit" with a show-panel button; browser fallback when WebView2 is absent.
  - The SpatialQC pane (:8602) stays on the private branch — deliberately not ported (the handoff
    allows splitting it out).
- **Installer/bundle ship the WebView2 runtime pieces** (`Microsoft.Web.WebView2.Core.dll`,
  `Microsoft.Web.WebView2.Wpf.dll`, `WebView2Loader.dll`) per Revit version, and the artifact gate
  now fails the build if any is missing. `install.ps1` copies them; `uninstall.ps1` removes them.
  **`revit-mcp-panel.json` (the user's panel-URL config) is never written or removed** by either
  script.

Counts unchanged: 89 MCP tools, 91 C# commands (the panel is UI, not an MCP command).
Addresses `MultiAIagents-main/docs/handoff_addin_dockable_panel.md` (AU 2026 demo path).

---

## [0.8.19] — 2026-07-23: Installer configures Codex / Gemini / Cursor too

### Added

- **`install.ps1 -Client <list>`** configures MCP clients beyond Claude Desktop. Accepts one or
  more of `claude` (default), `gemini`, `cursor`, `codex` — e.g. `-Client codex,gemini` or
  `-Client claude,gemini,cursor,codex`. Each client's config is merged in place, backed up first,
  and every other server it already has is preserved:
  - `claude` -> `%APPDATA%\Claude\claude_desktop_config.json` (JSON `mcpServers`)
  - `gemini` -> `%USERPROFILE%\.gemini\settings.json` (JSON `mcpServers`)
  - `cursor` -> `%USERPROFILE%\.cursor\mcp.json` (JSON `mcpServers`)
  - `codex`  -> `%USERPROFILE%\.codex\config.toml` (**TOML** `[mcp_servers.NAME]` tables)
  The three JSON clients share one merge path; Codex gets a TOML writer that strips and re-appends
  only the `[mcp_servers.revit-*]` tables, leaving all other TOML content untouched.
- `-NoClaudeConfig` is kept as an alias of the new `-NoClientConfig`.

The server itself is unchanged and client-agnostic (stdio `node dist/index.js`); this only teaches
the installer where each client keeps its config. Cloud-only clients that can't run a local stdio
process (e.g. web ChatGPT) still can't reach the loopback add-in — use a local client (Codex CLI,
Claude, Cursor, Gemini CLI) on the same machine as Revit.

No functional change to the server: 89 MCP tools, 91 C# commands.

---

## [0.8.18] — 2026-07-19: One-shot installer for all three Revit versions

### Added

- **Single self-contained install bundle.** `RevitMCPServer-v<ver>.zip` now carries the add-in for
  **all three Revit versions** (`addin/2025`, `addin/2026`, `addin/2027`) plus one shared MCP server.
  `install.ps1` with no arguments:
  - **auto-detects** which Revit versions are installed (`Program Files\Autodesk\Revit <year>`) and
    deploys the matching add-in to each `%APPDATA%\Autodesk\Revit\Addins\<ver>`;
  - copies the MCP server to a **stable location** (`%LOCALAPPDATA%\RevitMCPServer`) and runs
    `npm install` there, so the extracted folder can be deleted afterwards;
  - **merges** a `revit-<ver>` entry per version into the Claude Desktop config — backing it up
    first and leaving every other MCP server entry untouched (JSON-parsed, not string-spliced).
  Flags: `-RevitVersions`, `-AllVersions`, `-NoClaudeConfig`, `-ClaudeConfigPath`,
  `-ServerInstallDir`, `-SkipNpm`. Missing Node.js is a warning, not a failure — the add-in works
  without it; only the Claude bridge needs it. `uninstall.ps1` mirrors all of this.
- `build-release.ps1` produces the combined bundle (replacing the per-version ZIPs) and the artifact
  gate now checks the per-version `addin/<ver>/` layout.

### Fixed

- **Installer/build scripts are now pure ASCII.** They contained em-dashes and box-drawing
  characters; Windows PowerShell 5.1 (the default on a clean Windows box) reads a UTF-8 `.ps1` as the
  ANSI code page, so an em-dash inside a string (`E2 80 94`) decoded to `â€"` whose trailing byte is a
  `"` in CP1252 — silently terminating the string and breaking the parse **on the end user's machine**,
  not just ours. All non-ASCII was transliterated to ASCII and both scripts re-verified with the PS
  parser.

No functional change to the server: 89 MCP tools, 91 C# commands. Addresses `project_review_findings`
"ready-to-run installer" for the MCP-only path (no ribbon/.bundle, which are Design-&-Make-marketplace
requirements we are not targeting).

---

## [0.8.17] — 2026-07-17: Security hardening — loopback clamp, unconditional auth, audit clean

### Changed (BREAKING for dev-only escape hatches)

- **`REVIT_MCP_AUTH=false` is removed on both sides.** The add-in always generates and
  requires the bearer token; the Node client always sends one when available and ignores the
  env var. Rationale: the listener is loopback-only, but an *unauthenticated* loopback port
  would still let any local process drive Revit. Verified before removal that nothing on the
  single deployment machine sets it — no `.env`, no system env, no Claude config does
  (Cad2BIM and bim-orchestrator *support* the variable in their clients but do not enable
  it; they are being notified to drop that branch).
- **`REVIT_MCP_HOST` is clamped to loopback** (`127.0.0.1`, `localhost`, `::1`). Any other
  value makes the Node client refuse to start with a clear error. The add-in's HttpListener
  prefix is hard-coded to `http://127.0.0.1:<port>/`, so a non-loopback host could never
  reach a real add-in — the only thing that setting could ever do is hand the bearer token
  and the full command stream to an arbitrary host over plaintext HTTP.

### Fixed

- **`npm audit --omit=dev` is clean: 5 → 0 vulnerabilities** (hono, fast-uri,
  express-rate-limit, ip-address, qs — 2 high / 3 moderate). All five were transitive
  dependencies of `@modelcontextprotocol/sdk`'s **HTTP transports**, which this stdio-only
  server never imports (`server/mcp.js` + `server/stdio.js` are the only SDK entry points),
  so reachability was effectively nil — fixed for hygiene and marketplace review, within
  semver ranges, no code change.

Counts unchanged: 89 MCP tools, 91 C# commands. Addresses `project_review_findings_2026-07-16.md`
P1 (dependency audit + runtime security escape hatches, resolved as B2: remove rather than
document).

---

## [0.8.16] — 2026-07-17: Release package actually runs; MCP submission docs land

### Fixed

- **The release package shipped an add-in and an MCP server that could not start.**
  Three separate omissions, all in the packaging path only:
  - `RevitMCPAddin.dll` was packaged **without `RevitMCP.Core.dll`** — the class-lib that
    carries the command kernel the add-in type-loads against. Revit would throw
    `FileNotFoundException` on start-up. The csproj's dev-deploy target *always* copied Core,
    so every local deploy worked and the bug lived only in the artifact users download —
    which is why it survived so many versions.
  - Only `dist/index.js` was packaged, but it imports `./revitClient.js` and `./recipes.js`
    at run time, so the packaged server died on first import.
  - `uninstall.ps1` left `RevitMCP.Core.dll` orphaned in the Addins folder.
- **`install.ps1`** now copies `RevitMCP.Core.dll` and fails fast with a clear message if it
  is not next to the add-in dll. **`uninstall.ps1`** removes `RevitMCP.Core.dll`/`.pdb` and
  reports the two things it deliberately leaves behind (metadata-only logs, the MCP server
  folder) so they can be deleted in one step.

### Added

- **Artifact completeness gate in `build-release.ps1`.** Before zipping, it asserts the 10
  required files are present and statically resolves every relative import in the packaged
  JS. A missing runtime file now fails the build instead of shipping silently. Verified on
  all three packages (R2025/R2026/R2027), plus an end-to-end check: the ZIP was extracted,
  prod deps installed, and the packaged server booted clean.
- **MCP Publisher submission artifacts** — `mcp-manifest.json` (89 tools, cross-checked
  against `index.ts`) and the filled Publisher Declaration content.

### Changed

- **Publisher Declaration corrected to match the code.** The earlier draft overclaimed:
  it said the server reads only the open Revit model (`revit_load_family` reads a `.rfa`
  from any local path, and PNG/PDF/CSV exports write to disk); it said uninstall removes the
  add-in *and* MCP server (it does not remove the server folder or logs); it implied auth is
  unconditional (token auth is the default but a local env var can disable it). Data
  retention now states plainly that diagnostic logs are append-only daily files with no
  automatic rotation, kept until the user deletes them. Also records that the add-in's HTTP
  listener is hard-bound to `http://127.0.0.1:<port>/` and is therefore unreachable from the
  network under any configuration.
- `mcp-manifest.json` declares `mcp_spec_version: 2025-11-25` — the SDK's
  `LATEST_PROTOCOL_VERSION` and the value in Autodesk's own example (was `2025-06-18`).

Counts unchanged: 89 MCP tools, 91 C# commands. Addresses `project_review_findings_2026-07-16.md`
P0-1 and P0-2.

---

## [0.8.15] — 2026-07-15: `find_elements` view scoping (`view_id`) — closes the last fork gap

### Added

- **`find_elements` accepts `view_id`** — scopes the query to elements visible in that view
  (must be a non-template `View`; a bad id returns a clear `invalid_parameter` error instead
  of a raw collector exception). Ported from the `feat/extract-revit-mcp-core` fork; with this,
  `main` is a strict superset of that branch and downstream submodules can re-pin to `main`.
  Exposed on the MCP tool surface (`revit_find_elements.view_id`).

### Fixed

- **`find_elements` docstring caught up with reality** — it still described the pre-pagination,
  instance-only-parameter behaviour, which misled an external audit
  (`HANDOFF_revitmcp-find-elements-fix.md`) into re-reporting bugs that `main` had already
  fixed: offset pagination landed in v0.8.6 (P2-C) and instance→type parameter fallback in
  v0.8.11. Verified against fresh `origin/main`: both fixes present; only `view_id` was missing.

Counts unchanged: 89 MCP tools, 91 C# commands.

---

## [0.8.14] — 2026-07-13: Build-truth `/health`, hosted placement actually lands

### Added

- **`/health` now reports build-truth and stays auth-exempt.** `version` comes from the
  compiled `AssemblyInformationalVersion` — single-sourced from the csproj `<Version>`, no
  hand-typed literal — plus `gitCommit`, `gitBranch`, `gitState`, `buildTimestampUtc`,
  `commandCount`, and a `capabilityHash` over the live registry. A consumer can verify the
  actual capability without a token, and a build can no longer advertise a version that
  outranks the command surface it really ships. The add-in also logs its build line on
  start-up so the Revit journal shows exactly which dll loaded.

### Fixed

- **Hosted `revit_place_family_instance` is committed for real.** The [0.8.11] entry below
  documented it, but the code was never committed — the RevitMCP.Core class-lib extraction
  carried the non-hosted version, so builds through 0.8.13 placed doors/windows with
  `Host = -1` and no wall cut. Restored the hosted overload (host-phase copy,
  `flipFacing`/`flipHand`) and added a wall-only guard: a `hostId` that is not a `Wall` falls
  back to non-hosted placement with a `hostWarning` instead of throwing.
- **Two-point tool schemas no longer collapse the second point to an unusable `{}`.**
  `revit_create_wall`, `revit_create_beam`, `revit_create_grid`, and the mirror-plane tool
  referenced the shared `xyz` Zod object twice, so `zod-to-json-schema` emitted the second
  point as a `$ref` the MCP bridge flattened away — a direct `create_wall` call rejected
  `end` with *"expected object, received string"*. Giving the second point a distinct
  instance (`.describe(...)`) inlines its `{x,y,z}` schema.

Counts unchanged: 89 MCP tools, 91 C# commands.
Addresses cad2bim gap: `HANDOFF_revitmcp_hosted_family_instance.md`.

---

## [0.8.13] — 2026-07-03: Spatial-QC command pack (HTTP-only, `spatial_*`)

### Added

- **Four pure-geometry commands forward-ported from AutomatedSpatialQC's add-in fork** so
  `spatial-qc check-revit` runs against the live `main`-based add-in again (it was aborting at
  `get_room_boundary` because only `get_doors` had been ported earlier):
  - **`spatial_get_room_boundary`** — room boundary loops (outer ring + holes) at the finish face as
    world-XY polylines in metres (net clear area, matches `IfcSpace`).
  - **`spatial_clearance_envelope`** — volumetric MEP-aware clear-height check over a room footprint,
    boolean-intersecting every overhead element in host **and every linked RVT**, naming each
    obstruction with the clear height it leaves.
  - **`spatial_clearance_envelope_batch`** — the same check for many rooms in one call, extracting
    candidate geometry once over the union of footprints.
  - **`spatial_raycast_headroom`** — vertical headroom raycast returning the lowest overhead soffit
    per `(x,y)` point.

### Namespacing decision

- The four are **registered in C# (HTTP-callable via `/mcp`) but NOT exposed as MCP tools** — they are
  consumed programmatically by the spatial-qc Python client, not by LLM tool routing, so surfacing
  them would only dilute the tool list. Prefixed `spatial_` to keep them clearly apart from the
  curated command surface and avoid any future name collision.
- **`get_doors` was deliberately left unprefixed.** It already shipped (v0.8.12) as the general-purpose
  MCP tool `revit_get_doors` (ADA/egress door-swing, useful to any consumer); renaming it would break
  that tool name and re-churn docs for no benefit. It and the spatial pack are different layers.
- Added `P.LongOrNull` helper (used by `spatial_get_room_boundary`).

Counts: **91 C# commands** registered (86 exposed + 5 hidden: `create_spot_elevation` + the 4 spatial
commands), still **89 MCP tools** (surface unchanged).

---

## [0.8.11] — 2026-06-27: Hosted family-instance placement (doors/windows)

> **Correction (0.8.14):** the code described below was *not* actually committed at 0.8.11 —
> it was lost in the RevitMCP.Core class-lib extraction and only truly landed in [0.8.14].
> Builds 0.8.11–0.8.13 still placed non-hosted (`Host = -1`).

### Changed

- **`revit_place_family_instance`** — adds `hostId`, `flipFacing`, and `flipHand` parameters
  (all optional, fully backward-compatible). When `hostId` is supplied the handler uses the
  Revit API hosted overload `NewFamilyInstance(XYZ, FamilySymbol, Element host, Level, StructuralType)`
  so the door/window is wall-hosted, Revit auto-cuts the opening, and `Host Id ≠ -1`.
  Phase Created is copied from the host wall to avoid the *"infilling wall"* warning.
  Without `hostId` behaviour is unchanged (non-hosted free-standing placement).

Counts: 88 MCP tools (unchanged — no new tool, existing tool extended).
Addresses cad2bim gap: `revit_mcp_hosted_instance_gap.md`.

---

## [0.8.12] — 2026-06-28: get_doors — door swing geometry

### Added

- **`revit_get_doors`** — all placed doors with nominal width (m), plan location (world XY, m),
  level, and **swing geometry**: `facingX/Y` (FacingOrientation — the normal / pull-swing side),
  `handX/Y` (HandOrientation — along the wall), and `facingFlipped/handFlipped`. Orientation is
  geometry, not a parameter, so `find_elements` cannot return it — this command exposes door swing
  for ADA/egress maneuvering-clearance and door-swing checks (consumer: spatial-qc).

  Ported from the `feat/extract-revit-mcp-core` line (commit `0668cf9`) onto `main` — that branch
  was 23 commits behind `main` and building it would have regressed the live add-in, so the command
  was brought forward instead. Read-only; additive; `find_elements`/`list_elements` unchanged.

---

## [0.8.11] — 2026-06-27: find_elements projects type parameters

### Fixed

- **`find_elements` now resolves TYPE parameters, not just instance parameters.** Both the
  `fields` projection and the `filters` matcher used `Element.LookupParameter`, which is
  instance-only, so type-level BIM parameters (Fire Rating, door Width, assembly codes,
  materials…) came back empty and filters on them never matched. They now fall back to the
  element's Type when the instance lookup misses, cached per `(typeId, name)` so N elements
  sharing a type cost one type lookup. Response `fields` shape is unchanged — type values
  simply start appearing. (Reported via a downstream QA rig; a stale copy of this command in
  another repo also reported the pre-P2-C "offset ignored" bug — that one was already fixed
  here in 0.8.6/P2-C.)

---

## [0.8.10] — 2026-06-26: Workflow recipe — clash review (P4)

### Added

- **`revit_recipe_clash_review`** (read-only) — runs a coordination clash sweep across many
  element-set pairs and returns a consolidated, prioritized report (hard clashes first, then
  clearance violations by smallest gap, counted per pair). Each pair is a `check_clearance`
  input, so **linked RVTs are supported** (`setB.source='link'` + `linkId` from
  `get_linked_files`) — the real host-MEP × linked-Arch/Struct coordination case. Composes
  `check_clearance`; a pair that errors is recorded and the sweep continues.

Counts: 88 MCP tools (86 C# commands, 1 hidden, 2 Node-only recipes). Synthesis unit-tested
(Vitest 20/20). **Verified live e2e on Revit 2027** against a federated model: HVAC-link ducts ×
Structural-link framing returned 20 hard clashes (link × link), correctly aggregated and
prioritized per pair.

---

## [0.8.9] — 2026-06-25: Workflow recipes — pilot (P4)

### Added

- **Workflow recipe layer** (`src/McpServer/src/recipes.ts`) — the P4 orchestration layer.
  Recipes live in the Node bridge ABOVE the deterministic C# kernel; they compose verified
  atomic commands into goal-oriented workflows (preconditions, synthesis, and — for writes —
  dry-run/verification). They never touch the Revit API directly.
- **`revit_recipe_model_health_triage`** (read-only pilot) — runs a model-health scan and
  returns a **prioritized, actionable triage list** (each issue + severity + recommended fix),
  instead of raw metrics. Composes `get_model_health`. New `recipes` tool profile.
- `check-version.mjs` now accounts for Node-only `revit_recipe_*` tools (excluded from the
  C#-parity invariant): `server.tool == registered − hidden + 1 batch + recipes`.

Counts: 87 MCP tools (86 C# commands, 1 hidden, 1 Node-only recipe). Synthesis unit-tested
(Vitest); underlying `get_model_health` already verified live.

---

## [0.8.8] — 2026-06-25: Family management + detailing (P3 packs 2 & 3)

### Added

- **`revit_load_family`** — load a family (`.rfa`) from disk into the project with an
  overwrite policy (`IFamilyLoadOptions`). Returns family id, category, and its types.
- **`revit_duplicate_family_type`** — duplicate a FamilySymbol under a new name (set
  parameters afterwards with `set_parameter` on the returned `typeId`).
- **`revit_create_detail_line`** — view-specific detail line in a 2D view; endpoints
  projected onto the view plane (rejects 3D / sheet / schedule views).
- **`revit_create_filled_region`** — filled region from a closed boundary in a 2D view;
  points projected onto the view plane, loop closed automatically, default FilledRegionType.

Counts: 82 → 86 MCP tools (86 C# commands registered, 1 hidden). Compiles for R2025/26/27;
C# 132/132; check-version green. **Verified live on Revit 2027** (24/24 smoke incl. family &
detailing): `load_family` loaded a real .rfa inside the dispatcher transaction; detail-line
and filled-region view-plane projection confirmed; `duplicate_family_type` dry-run confirmed.

---

## [0.8.7] — 2026-06-25: Schedule data reading (P3 pack 1)

### Added

- **`revit_get_schedule_data`** — read the rendered cell text of a ViewSchedule
  (calculated fields, units, and formatting applied — exactly what the user sees).
  Uses `ViewSchedule.GetTableData()` + `GetCellText(SectionType.Body, …)`. Paginated by
  row (`offset`/`limit`; returns `totalRows`, `totalColumns`, `hasMore`, `nextOffset`).
  The first row is normally the column headers. Complements `create_schedule` /
  `configure_schedule` (which author schedules and export CSV) by returning the data
  inline to the agent. Inspection profile.

---

## [0.8.6] — 2026-06-25: Pagination for large element lists (P2-C)

### Added

- **Pagination** for `revit_list_elements` and `revit_find_elements`: new `offset` param,
  and the response now carries `total` (all matches — for `find_elements`, after filters),
  `offset`, `limit`, `hasMore`, and `nextOffset`. Page through arbitrarily large sets by
  passing `offset = nextOffset` — the previous 5000-element ceiling no longer caps total
  reach (per-page `limit` stays ≤ 5000 so each response remains token-bounded).
- `truncated` is kept as an alias of `hasMore` for backward compatibility; `offset`
  defaults to 0, so existing callers are unaffected and simply gain the new fields.

### Changed

- **`create_spot_elevation` re-hidden** from the MCP surface. Live testing on Revit 2027
  showed the `ReferenceIntersector` raycast returns no face hit for floors even at the
  bbox centre (`doc.Regenerate()` on the temporary 3D view did not help), and the earlier
  solid-face approach failed with "Spot Dimension does not lie on its reference". The C#
  command stays registered (HTTP-callable) with a 2D-view guard for future work, but is off
  the tool surface until a reliable face-reference approach lands. `create_aligned_dimension`
  remains live and verified (grid+grid dimension).

---

## [0.8.5] — 2026-06-24: Un-hide create_aligned_dimension + create_spot_elevation

### Fixed

- **`create_aligned_dimension`**: Grid references now use `new Reference(grid)` (element
  reference) instead of the grid curve's geometry reference, which does not resolve in
  `NewDimension`. Wall centreline / core now uses the undocumented `:-9999:` stable
  representation (index 1 = overall centreline, 2 = core exterior, 3 = core interior,
  4 = core centre), confirmed working on Revit 2027. `GetSideFaces` kept as a fallback
  for explicit exterior/interior face requests.
- **`create_spot_elevation`**: Replaced manual solid-face iteration + user-supplied Z
  (caused "Spot Dimension does not lie on its reference") with `ReferenceIntersector`
  downward raycast on a temporary isometric 3D view. The hit gives both the face reference
  and a point guaranteed to lie on it. Temporary view is deleted after placement.

### Changed

- Both tools are now **exposed on the MCP surface** (`revit_create_aligned_dimension`,
  `revit_create_spot_elevation`) and added to the `documentation` profile. Total MCP
  tools: **80 → 82** (81 C# commands + 1 batch; 0 hidden).
- Version bumped: 0.8.4 → 0.8.5.

## [0.8.4] — 2026-06-23: Truth gate + observability & limits

### Fixed

- **Version drift**: `package.json` / `index.ts` / `McpHttpServer.cs` `/health` all
  lagged at `0.8.0` while docs claimed `0.8.3`; README health examples still showed
  `0.7.0`. All version strings now converge (single value, gated by CI).
- **Tool-count drift**: docs claimed **74** MCP tools while the code exposed **80**
  (79 commands + 1 batch; 81 C# commands registered, 2 hidden). README/COMMANDS/
  API_COVERAGE corrected; `revit_override_element_graphics` was missing from the
  README tool list.

### Added (P0 — truth gate)

- **`scripts/check-version.mjs`** now also verifies the tool/command inventory:
  counts `server.tool(...)` (TS) and `Register(new ...)` (C#), enforces the invariant
  *exposed commands − hidden + 1 batch = MCP tools*, and fails CI if the README
  headline counts disagree. Counts can no longer silently drift.

### Added (P1 — observability & limits)

- **Request correlation ID** on every HTTP request (`X-Request-Id`, generated if the
  client doesn't supply one) — returned as a response header and included in logs.
- **Structured request log** (one JSON line per request): timestamp, requestId,
  method, path, command, ok, errorCode, HTTP status, durationMs. Written under
  `%LOCALAPPDATA%\RevitMCP\logs\`.
- **Limits / backpressure**: max request body size, max batch steps, and a cap on
  concurrent in-flight requests (returns `overloaded` → HTTP 503 with retry hint).
- **`GET /stats`** endpoint: total/success/failed request counts, in-flight count,
  average and peak duration.

### Added (P1.5 — live-Revit smoke suite)

- **`scripts/smoke-test.ps1`** — drives the running addin over HTTP and asserts
  end-to-end behaviour unit tests can't (real Revit API). Covers connectivity, the
  read surface, observability, dry-run vs real writes (self-cleaning create→delete),
  batch, and the new limits. Supports `-Snapshot`/`-Golden` fingerprint compare against
  a fixed fixture `.rvt`. See [`docs/SMOKE_TESTING.md`](docs/SMOKE_TESTING.md).
  Verified live on Revit 2027 (18/18 checks; golden compare 23/23).

### Added (P2-A — tool profiles)

- **`REVIT_MCP_PROFILE`** env var — expose only selected tool groups instead of all 80,
  cutting token cost and tool-selection errors. Groups: `core` (always on), `inspection`,
  `model-health`, `coordination`, `architecture`, `documentation`, `editing`, `view`.
  Unset = all tools (default, backward compatible). Implemented as a runtime gate over
  `server.tool` (no change to the registration call sites). Verified: `documentation`
  exposes 20 tools, hides 60.

---

## [0.8.3] — 2026-06-22: Model health — worksets, imports/links, warning ratio

### Added

- **`revit_get_worksets`** — list user worksets with per-workset element counts.
  Flags empty worksets (no instances) and the un-renamed default `"Workset1"`.
  Returns `{ isWorkshared, count, emptyCount, worksets: [{id, name, elementCount,
  isEmpty, isOpen, isEditable, isVisibleByDefault, owner, isDefaultName}] }`.
  Non-workshared models return `isWorkshared=false`.
  Tested live on Revit 2027 (4 worksets, "Workset1" correctly flagged).

### Changed

- **`revit_get_model_health`** enhanced:
  - New **`imports`** section: imported vs linked CAD (with "in views" subcount),
    imported PDFs + raster images (`imagesAndPdfs`), RVT link instances/types,
    point clouds. Imported (non-linked) CAD is flagged (any > 0, warning); imported
    images/PDFs are flagged as info (any > 0).
  - **`warnings.perThousandElements`** — warnings-per-1000-elements ratio, reported
    for context (no published industry standard, so not scored).
  - **`file.worksets` / `file.emptyWorksets`** — workset summary; empty worksets flagged.
  - **`file.isModelInCloud`** — makes it explicit when file size is `null` because the
    model is cloud-hosted (the Revit API exposes no on-disk size for cloud models).
  - Thresholds aligned to published guidance: warnings high **300** / critical 1000;
    file size flag at **~500 MB** (recommended max 400-500); any imported CAD flagged.
  - Per-family file sizes are intentionally **not** measured (no Revit API for a loaded
    family's size; would require EditFamily+save per family) — noted in `notes`.

---

## [0.8.2] — 2026-06-22: Model health report

### Added

- **`revit_get_model_health`** — one-shot, read-only model quality report. Aggregates
  the metrics a BIM manager checks when judging a model, in a single call:
  - **Warnings**: total, error count, and top-N groups by description.
  - **File**: size (MB), worksharing status, workset count. Size is `null` for cloud
    models (`IsModelInCloud`) — reported in `notes`.
  - **Elements**: total count, distinct categories, top-N categories by instance count.
  - **Families**: loadable vs in-place, imported vs linked CAD instances, raster images.
  - **Groups**: model/detail group instances, single-instance group types.
  - **Views**: total views, sheets, placeable views, views not placed on any sheet.
  - **Complexity**: levels, grids, design options, reference planes (+ unnamed).
  - **Purgeable**: `Document.GetUnusedElements` count — only when `deep=true`
    (single-pass estimate; skipped by default as it is slow on large models).
  - **Scorecard**: letter grade (A–F), 0–100 score, and a list of flagged issues
    with severity. Thresholds are tunable constants in `GetModelHealthCommand`.

  Params: `deep` (bool, default false), `topN` (int, default 10).
  Tested live on Revit 2027 (Snowdon Towers sample): grade A/95, 90 warnings,
  42,901 elements, 633 purgeable.

---

## [0.8.1] — 2026-06-21: Annotation — tagging

### Added

- **`revit_tag_all_in_view`** — tag all untagged elements of a category in a view
  (mirrors Revit's "Tag All Not Tagged"). Accepts `category` (display name, e.g. `"Doors"`),
  optional `viewId`, `leader` (bool), `skipTagged` (bool, default true).
  Returns `{ tagged, skipped, failed, tags: [{tagId, elementId}] }`.
  Tested live on Revit 2026 (tagged 3 Doors in L1 - Architectural).
- **`revit_get_tags_in_view`** — list all `IndependentTag` elements in a view.
  Optional `category` filter. Returns `{ viewId, count, tags: [{tagId, elementId,
  category, hasLeader, tagText, location}] }`.
  Tested live: correctly returned 3 Door tags with tagText `"100"`, `"101"`, `"102"`.

### Notes

- `create_aligned_dimension` and `create_spot_elevation` are implemented in C# but
  hidden from the MCP tool surface pending Revit API reference fixes. They remain
  callable via direct HTTP if needed. Dimension works wall-to-wall; grid references
  in mixed `ReferenceArray` are not yet resolved.

---

## [0.8.0] — 2026-06-19: Type change, view templates, parameter copy, schedule/PDF/level, room containment

### Added

- **`revit_change_element_type`** — swap the type of any element (wall type, floor
  type, family symbol) using `Element.ChangeTypeId`. Validates against the element's
  own allowed types; returns old and new type info. Error code `wrong_element_type` → 400.
- **`revit_apply_view_template`** — apply or remove a view template from a view.
  Accepts `templateId` (ElementId) or `templateName` (case-insensitive lookup). Pass
  `templateId: -1` to remove the current template. Use `revit_list_view_templates` to
  discover available templates.
- **`revit_copy_parameters`** — copy parameter values from a source element to N target
  elements in one call. Matches by name and StorageType; only writable, non-None
  parameters are copied. Returns per-target success/failure detail.
- **`revit_configure_schedule`** — add filters and sort/group fields to an existing
  ViewSchedule; supports `clearFilters` / `clearSortFields` to reset first. Optional
  `exportCsv: true` exports the schedule and returns CSV content in the response.
  Uses `ScheduleField.FieldId` for phase-aware resolution; fields are added as hidden
  columns automatically when first referenced.
- **`revit_set_level_elevation`** — change the elevation of a Level element. Supports
  `"meters"`, `"feet"`, `"mm"`, and `"internal"` units. Returns old and new elevation
  in both m and ft.
- **`revit_export_view_pdf`** — export any view or sheet to PDF on disk via
  `Document.Export(folder, viewIds, PDFExportOptions)`. Accepts `outputFolder`,
  `fileName`, `rasterQuality` (Low / Medium / High / Presentation), `colorMode`
  (Color / Grayscale / BlackLine). Returns output path and file size.
- **`revit_get_element_rooms`** — get room containment for one or more family instances
  in a single batch call. Uses `FamilyInstance.get_Room(Phase)` / `get_FromRoom(Phase)`
  / `get_ToRoom(Phase)` — phase-dependent and authoritative, not centroid-in-bbox.
  `fromRoom` + `toRoom` for boundary connectors (Doors, Windows); `room` for
  point-located elements (Furniture, Fixtures, lighting, plumbing, …). Each room is
  `{ id, name, number }` or null. Phase resolved from the element's `PHASE_CREATED`
  parameter. Verified live on Revit 2027 Snowdon Towers Architectural.
- **`RevitMCP.Core` class library** extracted from `RevitMCPAddin` — all
  `IRevitCommand` implementations now live in a separate classlib for cleaner project
  boundaries and faster incremental builds.
- `wrong_element_type` added to the 400 group in `StatusForResult()`.

---

## [0.7.0] — 2026-06-11: Linked-file clash detection, view image export, R2025

### Added

- **`revit_get_linked_elements`** — read elements from inside a linked RVT file.
  Bounding boxes are automatically transformed to host-model coordinates.
  Accepts `linkId` (from `revit_get_linked_files`), optional `category`, optional `limit`.
- **`revit_check_clearance`** — detect hard clashes or clearance violations between two
  element sets. Supports host-vs-host (uses Revit's native `ElementIntersectsElementFilter`
  for exact solid-based detection) and cross-linked-file checks (AABB with clearance inflation).
  Parameters: `setA`, `setB` (each specifying `source: host|link`, optional `categories`),
  `clearanceMm` (0 = hard clash), `maxResults`.
- **`revit_get_view_image`** — export any Revit view (or the active view) to PNG and return
  it as an MCP `Image` content block. Accepts optional `viewId` and `dpi` (72/150/300).
- **Revit 2025 support** — added Nice3point reference assemblies for R2025 (`net8.0-windows`,
  port 7890). CI test matrix and release artifacts now include R2025 alongside R2026/R2027.
  Build/deploy works without installing R2025 locally.
- **7 new `CheckClearanceCommand.BboxIntersects` unit tests** covering overlap, separation,
  face-touch, containment, single-axis separation, and clearance inflation.

---

## [0.6.0] — 2026-06-11: Correctness, API hardening, CI R2027

### Breaking

- **Batch policy**: batches that mix `ModelWrite` and `UiAction` commands are
  now rejected with `bad_request`. Previously, UI actions could silently run
  inside a model transaction. Submit model writes and UI actions as separate
  batches.
- **Unit conversion extended**: `set_parameter` / `set_parameter_batch` now
  require spec-matched unit strings for area and volume parameters.
  - Length: `"meters"` / `"feet"` (unchanged)
  - Area: `"square_meters"` / `"square_feet"` (new)
  - Volume: `"cubic_meters"` / `"cubic_feet"` (new)
  - Passing `"meters"` for an area parameter now returns `invalid_parameter`
    instead of silently applying the wrong conversion.

### Added

- `revit_apply_view_filter` MCP schema now exposes `reuseExisting` (boolean,
  optional) — the C# command already supported it but the field was missing
  from the TypeScript schema.
- RGB channel validation in MCP schema: `r`, `g`, `b` now bounded `[0, 255]`
  for both `revit_apply_view_filter` and `revit_color_override_by_param`.
- `executionKind` field added to every entry in `GET /commands` response
  (`ReadOnly` / `ModelWrite` / `UiAction`). Clients can now distinguish model
  mutation from UI mutation.
- `RevitCommandException(code, message)` — commands now throw typed domain
  exceptions; the dispatcher preserves the `code` field in the error envelope
  instead of collapsing everything to `command_failed`.
- New HTTP status mappings in `StatusForResult`:
  - `invalid_parameter`, `read_only_parameter`, `unsupported_view` → 400
  - `ambiguous_selection` → 409
- CI matrix: `csharp` CI job now builds and tests both R2026 (.NET 8) and
  R2027 (.NET 10) on every pull request.
- `BatchPolicy.ValidateBatchKinds` — pure static helper, testable without Revit.
- New xUnit tests: `StatusForResultTests` (14 cases), `BatchPolicyTests` (7),
  `RevitCommandExceptionTests` (4), `CommandRegistryTests` extended with
  `executionKind` assertions. Total: **83 C# tests** (up from 46).

### Fixed

- README version badge updated from `v0.5.0` to `v0.6.0`; health example
  updated from `0.5.0` to `0.6.0`.
- `check-version.mjs` now validates the README badge and health example so
  future version drift is caught by CI.
- `vitest run` test script now uses explicit `--config ./vitest.config.ts
  --root .` to avoid path-resolution failures on Windows paths with spaces.
- MSB3277 warning suppressed in test project (harmless .NET 8 / Revit assembly
  version conflict that polluted CI output).
- Nice3point reference packages pinned to exact versions (`2026.4.10`,
  `2027.0.20`) for reproducible CI builds.
- Old stale review files (`project_review.md`, `docs/PROJECT_REVIEW.md`) removed.

## [0.5.0] — 2026-06-10: Safety, tests, CI, and production hardening

### Added — Command execution classification
- New `ExecutionKind` enum: `ReadOnly`, `ModelWrite`, `UiAction`.
- `open_view`, `select_elements`, `zoom_to_elements` marked `UiAction` —
  no longer wrapped in a model transaction; dry-run returns a no-op instead
  of silently reverting UI state.

### Added — Unit conversion for numeric parameters
- `set_parameter` and `set_parameter_batch` accept `units:"meters"|"feet"|"internal"`.
  When a Double parameter has measurable units (length, area, volume …),
  `UnitUtils.ConvertToInternalUnits` is called automatically.
  Dimensionless parameters (ratio, slope, etc.) are never converted.
  Response echoes `inputUnits` and `unitConversionApplied`.

### Added — View filter hardening
- `apply_view_filter` checks `AreGraphicsOverridesAllowed()` before
  creating the filter; raises a clear error for schedule/legend views.
- Duplicate filter names now detected before `ParameterFilterElement.Create()`
  — error includes the conflicting filter id. Set `reuseExisting:true` to
  re-apply an existing filter to the view instead.

### Added — `color_override_by_param` view guard
- `AreGraphicsOverridesAllowed()` check with view type in the error message.

### Added — Unambiguous family instance placement
- `place_family_instance` returns `placed:false` + a candidate list when
  both `familyName` and `familyTypeName` are omitted and multiple types
  match. When a partial filter still yields >1 match, places the first but
  includes a `warning` field and `familyTypeId` in the response.

### Added — Auth token auto-refresh
- `revitClient.ts` promotes `AUTH_TOKEN` to a mutable `_authToken` and
  re-reads the token file on `unauthorized` responses — handles Revit
  generating a new token on restart without requiring a server restart.

### Added — Startup health check
- On startup `index.ts` probes `/health` and logs: Revit not reachable,
  auth mismatch (enabled but no token), or successful connection with version.

### Added — Test suite (Phase 2)
- **13 TypeScript tests** (Vitest): `callRevit`, `callRevitBatch`,
  `envelopeToToolResult`, error codes, auth header.
- **46 C# tests** (xUnit): `JsonResult`, `ParamUtil` (all methods + bounds),
  `CommandRegistry` (register, replace, TryGet, ExecutionKind, RiskLevel).
- `Nice3point.Revit.Api` NuGet stubs used as fallback so `dotnet build`
  succeeds on CI machines without a Revit install.
- `scripts/check-version.mjs` — exits non-zero when version strings drift
  across `package.json`, `index.ts`, `McpHttpServer.cs`, and `CHANGELOG.md`.

### Added — GitHub Actions CI
- Three jobs: TypeScript build+test (ubuntu-latest), C# build+test
  (windows-latest, `DeployToRevit=false`), version consistency check.
- Release job on `v*` tags: builds R2026 + R2027 artifacts and uploads
  versioned zip files as GitHub Release assets.

### Added — Release tooling
- `scripts/build-release.ps1` — full release pipeline: version check,
  tests, multi-version C# build, TypeScript build, zip packaging.
- `scripts/install.ps1` — copies addin DLL + manifest to the per-user
  Revit Addins folder; runs `npm install --production` for the MCP server.
- `scripts/uninstall.ps1` — removes addin files from Revit Addins folder.

### Added — Compatibility matrix & troubleshooting guide
- `docs/COMPATIBILITY.md` — Revit × .NET × Node.js matrix with known limits.
- `docs/TROUBLESHOOTING.md` — diagnostic checklist for common failure modes.

### Fixed — HTTP error semantics
- `StatusForResult()` maps error codes to proper HTTP statuses:
  `bad_request`/`bad_json` → 400, `unauthorized` → 401,
  `unknown_command` → 404, name collision → 409, timeout → 408, else 500.

### Fixed — `set_parameter_batch` partial failure
- `partialFailure:true` added to top-level response when any element fails.
- New `atomic` option: `true` → any element failure rolls back the entire batch.

### Fixed — RGB color validation
- `P.ColorByte()` rejects values outside 0-255 with a clear error instead
  of silently wrapping via `(byte)` cast.

### Fixed — Version drift
- `package.json`, `index.ts`, `McpHttpServer.cs` all report `0.5.0`.

## [0.4.2] — 2026-06-09: Revit 2027 support + Family rename

### Added
- **Revit 2027 build** — csproj auto-selects `net10.0-windows` when
  `RevitVersion >= 2027`, `net8.0-windows` for R2025–2026.
  Build with `dotnet build -p:RevitVersion=2027`.
- **Auto-port assignment** — each Revit version gets its own port
  automatically (R2026=7891, R2027=7892, ...). No manual port config
  needed for side-by-side use.
- **Family & FamilySymbol rename** — `revit_rename_element` now handles
  `Family.Name` and `FamilySymbol.Name` (direct property setters), not
  just parameter-based renames. Validates system families, illegal
  characters, and name collisions. Returns `instancesAffected` count.
- Multi-version Claude Desktop config guide.
- Beginner-friendly installation walk-through in README (Step 0–6 +
  troubleshooting table).

### Changed
- `Revit_MCP_Server_Build_Plan.md` moved to `docs/internal/` (gitignored).
- README + docs fully updated for R2026/R2027 dual support.

## [0.4.1] — 2026-05-27

### Fixed — UTF-8 request decoding
- `McpHttpServer.ReadJsonObjectAsync` was decoding incoming bodies with
  `HttpListenerRequest.ContentEncoding`, which falls back to
  `Encoding.Default` when the client omits `charset` in `Content-Type`.
  Under Revit's hosting that produced mojibake for non-ASCII characters
  (e.g. em-dash `—` arrived as `â€"`, section sign `§` as `Â§`),
  corrupting audit-trail Comments written by the bim-orchestrator.
- Force `Encoding.UTF8` on the read path — JSON is canonically UTF-8
  per RFC 8259, so the client's charset declaration is moot.
- Also set `response.ContentEncoding = Encoding.UTF8` on the write
  path for symmetry; the actual bytes were already UTF-8.

## [0.4.0] — Phase 5a: Security + Preview

### Added — Dry-run mode
- Every write command and batch accepts `dryRun: true` (body field or
  `?dryRun=true` query param). The transaction runs normally then rolls
  back — the model is unchanged, but the full result data is returned so
  the AI can preview what *would* happen before committing.

### Added — Structured diffs
- Write commands now return a `changeSummary` one-liner in their data
  payload (e.g. `"Set 'Comments' on element 184239: '' → 'Reviewed'"`).
- Modify commands (`set_parameter`, `rename_element`, `move_element`)
  also return a `changes` object with `before`/`after` values.
- The AI shows the concise summary by default and can expand the full
  diff on request.

### Added — Auth token
- On startup the addin generates a 32-byte random token and writes it to
  `%APPDATA%\Autodesk\Revit\Addins\<version>\revit-mcp-token.txt`.
- All HTTP endpoints (except `GET /health`) require
  `Authorization: Bearer <token>`.
- The TypeScript MCP server reads the token file automatically.
- Disable auth with `REVIT_MCP_AUTH=false` or override with
  `REVIT_MCP_AUTH_TOKEN=<token>`.

### Added — Per-tool risk levels
- `IRevitCommand` now exposes a `RiskLevel` property with default interface
  implementation: `read` (read-only), `low` (creates), `medium` (modifies),
  `high` (deletes/destructive).
- `GET /commands` returns `riskLevel` alongside `isReadOnly` for each
  command, enabling clients to build per-tool permission policies.
- 13 commands classified as `medium` risk, 3 as `high`, rest default to
  `read` or `low`.

### Changed
- `McpHttpServer` version bumped to `0.4.0`.
- TypeScript MCP server v0.4.0 — all write tools now accept `dryRun`
  param; HTTP client sends `Authorization` header when token is available.
- `package.json` version updated to `0.4.0`.

## [0.3.0] — Phase 4: 60 Commands

### Added — Inspection / Introspection (+12 commands)
- `get_document_info` — project metadata, path, worksharing status, active view.
- `find_elements` — generic query DSL: category + parameter filters (equals,
  not_equals, contains, greater, less, etc.) + optional field projections.
- `get_parameter` — single parameter value from one element.
- `get_views` — all non-template views with type, level, scale, detail level.
- `get_active_view` — current active view.
- `get_selected_elements` — elements selected by user in Revit UI.
- `list_families` — loaded Families, optionally by category.
- `list_family_types` — FamilySymbols, optionally by family or category.
- `list_sheets` — all ViewSheets with number, viewport count.
- `list_rooms` — placed rooms with area, perimeter, department.
- `list_materials` — materials with class, category, color.
- `list_phases` — project phases.
- `list_view_templates` — all view templates.
- `get_linked_files` — RevitLinkInstances and their load status.
- `get_element_geometry` — bounding box, centroid, volume, surface area,
  solid/face counts.

### Added — Creation: Architecture (+6 commands)
- `create_room` — NewRoom at a point with optional name/number.
- `create_column` — structural column placement with family type resolution.
- `create_beam` — structural beam between two points.
- `create_ceiling` — Ceiling from closed polygonal profile.
- `create_opening_in_wall` — rectangular opening in a wall.
- `place_family_instance` — generic FamilyInstance placement (non-hosted).

### Added — Creation: Documentation (+8 commands)
- `create_sheet` — ViewSheet with optional title block.
- `place_view_on_sheet` — Viewport placement.
- `create_floor_plan_view` — ViewPlan for a level.
- `create_section_view` — ViewSection with configurable bounding box.
- `create_3d_view` — isometric View3D.
- `create_schedule` — ViewSchedule with optional field columns.
- `tag_element` — IndependentTag on an element in a view.
- `create_text_note` — TextNote in a view.

### Added — Edit: Parameters (+2 commands)
- `set_parameter_batch` — set one parameter on many elements in one call.
- `rename_element` — set Element.Name.

### Added — Edit: Transform (+4 commands)
- `rotate_element` — rotate around vertical axis.
- `copy_element` — copy with translation.
- `mirror_element` — mirror across a plane (copy or move).
- `array_linear` — copy N times along a vector.

### Added — Edit: Grouping (+2 commands)
- `group_elements` — NewGroup.
- `ungroup_elements` — UngroupMembers.

### Added — View Manipulation (+8 commands)
- `open_view` — switch active view.
- `set_view_detail_level` — Coarse / Medium / Fine.
- `hide_elements_in_view` — View.HideElements.
- `unhide_elements_in_view` — View.UnhideElements.
- `select_elements` — set UI selection.
- `zoom_to_elements` — ShowElements.
- `apply_view_filter` — create ParameterFilterElement + apply with overrides.
- `color_override_by_param` — color-code elements by parameter value in a view.

### Summary
- **60 registered C# commands** + 1 batch MCP tool = **61 MCP tools total**.
- C# addin: 0 errors, 3 warnings (MSB3277 noise).
- TypeScript MCP server: 0 errors.

## [0.2.0] — Phase 2: Edit, Create, Batch

### Added
- Batch transaction pattern (`revit_batch` / `POST /mcp/batch`).
- Write commands: `create_floor`, `create_level`, `create_grid`,
  `set_parameter`, `delete_elements`, `move_element`.
- Introspection: `list_levels`, `list_wall_types`, `list_floor_types`,
  `list_categories`.
- `GET /commands` endpoint.

### Changed
- Command framework refactor: `IRevitCommand` + `CommandContext` + dispatcher-
  owned transactions.

## [0.1.0] — MVP

- Initial Revit 2026 addin + TypeScript MCP server.
- 5 commands: `ping`, `get_revit_version`, `list_elements`, `get_element_info`,
  `create_wall`.
