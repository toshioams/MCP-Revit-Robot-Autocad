#!/usr/bin/env node
/**
 * Revit MCP Server v0.8.66 (stdio).
 *
 * 103 tools covering diagnostics, inspection, creation, editing, family,
 * transform, view manipulation, annotation, model health, batch operations, and coordination/clash detection.
 *
 * v0.8.0 additions:
 *   - change_element_type: swap wall/floor/family type of any element.
 *   - apply_view_template: apply or remove a view template from a view.
 *   - copy_parameters: copy parameter values from one element to many targets.
 *   - configure_schedule: add filters, sort/group fields, export to CSV.
 *   - set_level_elevation: change level elevation (meters / feet / mm).
 *   - export_view_pdf: export a view or sheet to PDF.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import {
  callRevit,
  callRevitBatch,
  envelopeToToolResult,
  checkRevitHealth,
  REVIT_BASE_URL,
} from "./revitClient.js";
import { modelHealthTriage, clashReview } from "./recipes.js";
import { searchFamilyLibrary } from "./familyLibrary.js";
import { planWallsFromCad, planSingleLineWalls, planDoorsFromCad, type CadCurve } from "./cadWalls.js";
import { readXyz, extractSegments, detectDuplicateShift, dropShiftedCopy, segmentsToCurves } from "./pointCloud.js";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
const execFileAsync = promisify(execFile);

const server = new McpServer({ name: "revit-mcp-server", version: "0.9.0" });

// Every forwarding tool accepts documentTitle/documentPath: the add-in resolves them per call,
// so a user switching the active window cannot redirect reads or writes. Recipes and batch
// are excluded (batch steps carry their own params; recipes call the active document).
const documentFields = {
  documentTitle: z.string().min(1).optional().describe("Act on this open document (title) instead of the active one."),
  documentPath: z.string().min(1).optional().describe("Act on this open document (full path) instead of the active one."),
};
const idempotencyKeyField = z.string().regex(/^[a-f0-9]{32}:[A-Za-z0-9_-]{1,96}$/).optional().describe("Optional at-most-once key: <sessionId from HTTP /health>:<unique operation id>. Requires a supporting host and explicit document target. Reuse only with identical inputs; use a new key for commit after preview. Recover via authenticated GET /operations?key=... after timeout.");
{
  const register = server.tool.bind(server) as (...args: unknown[]) => unknown;
  (server as unknown as { tool: (...args: unknown[]) => unknown }).tool = (...args: unknown[]) => {
    if (args.length === 4 && typeof args[0] === "string" && !/^revit_(recipe_|batch$)/.test(args[0])
        && args[2] !== null && typeof args[2] === "object")
      args[2] = { ...(args[2] as Record<string, unknown>), ...documentFields,
        ...((args[3] as { supportsIdempotency?: boolean })?.supportsIdempotency ? { idempotencyKey: idempotencyKeyField } : {}) };
    return register(...args);
  };
}

// ── Common schemas ──────────────────────────────────────────────────────────
const xyz = z.object({ x: z.number(), y: z.number(), z: z.number().optional() });
const unitsField = z.enum(["meters", "feet"]).optional().describe("Units. Default 'meters'.");
const idsField = z.array(z.number().int()).min(1).describe("Array of ElementId values.");
const dryRunField = z.boolean().optional().describe("Preview mode: execute but rollback — model is not modified. Default false.");
const colorChannel = z.number().int().min(0).max(255);
const rgbSchema = z.object({ r: colorChannel, g: colorChannel, b: colorChannel });

// Helper: forward a single command. A dryRun field must still reach the transport
// envelope: tools declared with dryRun but wired through fwd used to commit.
const fwd = (cmd: string) => Object.assign(async (params: Record<string, unknown>) => {
  const { dryRun, idempotencyKey, ...rest } = params;
  return envelopeToToolResult(await callRevit(cmd, rest, dryRun === true, idempotencyKey as string | undefined));
}, { supportsIdempotency: true });

// Helper: forward a write command (supports dryRun)
const fwdWrite = fwd;

// ── Tool profiles (P2-A) ──────────────────────────────────────────────────────
// With 151 tools, loading the whole catalog into every conversation wastes tokens
// and hurts tool-selection accuracy. Set REVIT_MCP_PROFILE to a comma-separated
// list (e.g. "documentation,view") to expose only those groups; "core" is always
// included. Unset = all tools (default, fully backward compatible).
const PROFILES: Record<string, string[]> = {
  core: [
    "revit_ping", "revit_get_version", "revit_get_document_info", "revit_save_document",
    "revit_find_elements", "revit_get_element_info", "revit_get_parameter",
    "revit_find_element_by_unique_id",
    "revit_batch",
  ],
  inspection: [
    "revit_query_where",
    "revit_list_elements", "revit_list_levels", "revit_list_wall_types",
    "revit_list_floor_types", "revit_list_categories", "revit_list_families",
    "revit_list_family_types", "revit_list_sheets", "revit_list_rooms",
    "revit_list_spaces", "revit_list_materials", "revit_list_phases",
    "revit_list_view_templates", "revit_get_views", "revit_get_active_view",
    "revit_get_selected_elements", "revit_get_linked_files",
    "revit_get_linked_elements", "revit_get_element_geometry",
    "revit_get_view_image", "revit_get_element_rooms", "revit_get_schedule_data",
    "revit_get_doors", "revit_get_warnings", "revit_get_type_structure", "revit_create_floor_type_variant", "revit_inspect_planar_faces", "revit_set_family_orientation", "revit_analyze_wall_roof_interface", "revit_inspect_scope_boxes", "revit_list_open_documents", "revit_activate_document", "revit_list_recent_documents", "revit_open_document", "revit_inspect_cad", "revit_inspect_point_cloud", "revit_sample_point_cloud",
  ],
  "model-health": ["revit_get_model_health", "revit_get_worksets"],
  recipes: ["revit_recipe_model_health_triage", "revit_recipe_clash_review"],
  coordination: ["revit_check_clearance"],
  architecture: [
    "revit_create_wall", "revit_create_floor", "revit_create_stair", "revit_create_railing", "revit_configure_railing_type", "revit_create_direct_shape", "revit_create_material", "revit_create_level",
    "revit_create_grid", "revit_create_room", "revit_create_column",
    "revit_create_beam", "revit_create_ceiling", "revit_create_opening_in_wall",
    "revit_place_family_instance", "revit_create_gable_roof",
    "revit_attach_wall_top", "revit_detach_wall_top", "revit_create_profile_wall", "revit_analyze_wall_roof_interface", "revit_plan_purlin_roof", "revit_create_framing_member", "revit_create_roof_type_variant", "revit_create_roof_edge_sweep", "revit_copy_elements_from_document", "revit_place_curve_family_instance", "revit_create_profile_family", "revit_join_geometry", "revit_import_cad", "revit_place_image_in_view", "revit_create_wall_type_variant", "revit_recipe_plan_walls_from_cad", "revit_edit_curtain_wall", "revit_snap_wall_ends", "revit_merge_walls", "revit_create_room_separators", "revit_link_point_cloud", "revit_set_point_cloud_display", "revit_recipe_plan_walls_from_point_cloud", "revit_recipe_convert_scans",
  ],
  documentation: [
    "revit_create_sheet", "revit_place_view_on_sheet",
    "revit_create_floor_plan_view", "revit_create_drafting_view", "revit_create_section_view",
    "revit_create_3d_view", "revit_create_perspective_view",
    "revit_create_schedule", "revit_configure_schedule",
    "revit_tag_element", "revit_tag_all_in_view", "revit_get_tags_in_view",
    "revit_create_text_note", "revit_export_view_pdf", "revit_duplicate_view",
    "revit_create_aligned_dimension", "revit_create_detail_line", "revit_create_filled_region", "revit_get_annotation_extents", "revit_set_category_visibility",
  ],
  editing: [
    "revit_set_parameter", "revit_set_parameter_batch", "revit_update_where",
    "revit_import_parameters", "revit_rename_element",
    "revit_change_element_type", "revit_apply_view_template",
    "revit_copy_parameters", "revit_set_level_elevation", "revit_move_element",
    "revit_rotate_element", "revit_copy_element", "revit_mirror_element",
    "revit_array_linear", "revit_delete_elements", "revit_group_elements",
    "revit_ungroup_elements", "revit_load_family", "revit_duplicate_family_type", "revit_recipe_search_family_library",
  ],
  structure: [
    "revit_list_rebar_types", "revit_get_rebars", "revit_get_rebar_host_info", "revit_audit_rebar_template",
    "revit_new_project_from_template", "revit_create_curved_wall", "revit_reinforce_curved_wall",
  ],
  view: [
    "revit_open_view", "revit_set_view_detail_level", "revit_fit_datums_to_crop",
    "revit_hide_elements_in_view", "revit_unhide_elements_in_view",
    "revit_isolate_elements_in_view", "revit_set_section_box",
    "revit_select_elements", "revit_zoom_to_elements", "revit_apply_view_filter",
    "revit_color_override_by_param", "revit_override_element_graphics", "revit_set_view_crop",
  ],
};

const TOOL_PROFILE: Record<string, string> = {};
for (const [profile, names] of Object.entries(PROFILES))
  for (const name of names) TOOL_PROFILE[name] = profile;

function resolveEnabledProfiles(): Set<string> | null {
  const raw = process.env.REVIT_MCP_PROFILE?.trim();
  if (!raw) return null; // null = expose everything (default)
  const set = new Set(raw.split(",").map((s) => s.trim().toLowerCase()).filter(Boolean));
  set.add("core"); // core tools are always available
  for (const p of set)
    if (p !== "core" && !(p in PROFILES))
      console.error(`[revit-mcp-server] WARNING: unknown profile "${p}" in REVIT_MCP_PROFILE`);
  return set;
}

const ENABLED_PROFILES = resolveEnabledProfiles();
let toolsRegistered = 0;
let toolsSkipped = 0;

// Gate server.tool() by profile without touching the registration call sites.
const _registerTool = server.tool.bind(server);
(server as unknown as { tool: (...a: unknown[]) => unknown }).tool = (
  ...args: unknown[]
) => {
  const name = args[0] as string;
  const profile = TOOL_PROFILE[name] ?? "core";
  if (ENABLED_PROFILES === null || ENABLED_PROFILES.has(profile)) {
    toolsRegistered++;
    return (_registerTool as (...a: unknown[]) => unknown)(...args);
  }
  toolsSkipped++;
  return undefined;
};

// ═══════════════════════════════════════════════════════════════════════════
// DIAGNOSTICS
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_ping", "Health check. Reports active doc title.", {}, fwd("ping"));
server.tool("revit_get_version", "Get Revit version, build, language, user.", {}, fwd("get_revit_version"));
server.tool("revit_get_document_info", "Get project info: title, path, worksharing, active view, project metadata.", {}, fwd("get_document_info"));

// ═══════════════════════════════════════════════════════════════════════════
// INSPECTION / INTROSPECTION
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_list_elements",
  "List elements filtered by BuiltInCategory. Paginated: returns total, hasMore, nextOffset — " +
  "page through large sets with offset (no 5000 ceiling).", {
  category: z.string().optional().describe("BuiltInCategory, e.g. 'OST_Walls'."),
  onlyInstances: z.boolean().optional(),
  limit: z.number().int().min(1).max(5000).optional().describe("Page size. Default 200."),
  offset: z.number().int().min(0).optional().describe("Page start index. Default 0. Use nextOffset from the previous page."),
}, fwd("list_elements"));

server.tool("revit_get_element_info", "Full parameters + bbox of one element.", {
  id: z.number().int(),
}, fwd("get_element_info"));

server.tool("revit_find_element_by_unique_id",
  "Resolve an element from its UniqueId — the 45-char identifier ACC/BIM 360 reports as externalId. " +
  "Use this instead of deriving an ElementId from the string: ElementId is numbered per document, so an id " +
  "taken from a linked model can silently address a different element in the host.",
  {
    uniqueId: z.string().describe("45-char '<guid-36>-<8 hex>', e.g. an ACC externalId."),
    linkId: z.number().int().optional().describe("Search ONLY inside this RevitLinkInstance (from revit_get_linked_files)."),
    searchLinks: z.boolean().optional().describe("If the host has no such element, sweep every loaded link. Ignored when linkId is given. Default false."),
  },
  fwd("find_element_by_unique_id"));

server.tool("revit_find_elements",
  "Query elements by category + parameter filters. Returns matched elements with optional field " +
  "projections. Paginated: total reflects all matches after filters; page through with offset (no 5000 ceiling).", {
  category: z.string().describe("BuiltInCategory name (required)."),
  view_id: z.number().int().positive().optional()
    .describe("Scope the query to elements visible in this view (non-template View id). Omit for the whole document."),
  filters: z.array(z.object({
    parameterName: z.string(),
    operator: z.enum(["equals", "eq", "not_equals", "neq", "contains", "greater", "gt", "less", "lt", "greater_equal", "gte", "less_equal", "lte"]).optional(),
    value: z.union([z.string(), z.number(), z.boolean()]),
  })).optional().describe("Parameter filters."),
  fields: z.array(z.string()).optional().describe("Parameter names to project in results."),
  limit: z.number().int().min(1).max(5000).optional().describe("Page size. Default 200."),
  offset: z.number().int().min(0).optional().describe("Page start index. Default 0. Use nextOffset from the previous page."),
}, fwd("find_elements"));

// Shared by revit_query_where / revit_update_where. The C# side (WhereSupport)
// also accepts aliases like parameterName/param/field and "="/">"/"!=", but the
// canonical spelling below is what tools should emit.
const whereField = z.array(z.object({
  parameter: z.string().describe("Parameter name. Resolved on the instance first, then on the element's Type."),
  operator: z.enum([
    "eq", "neq", "contains", "starts_with", "ends_with", "regex", "not_regex",
    "gt", "lt", "gte", "lte", "is_empty", "not_empty",
  ]).optional().describe("Default 'eq'. Text compares use the display value; gt/lt/gte/lte use the raw number."),
  value: z.union([z.string(), z.number(), z.boolean()]).optional()
    .describe("Omit for is_empty / not_empty."),
  scope: z.enum(["auto", "instance", "type"]).optional()
    .describe("Where to read the parameter. Default 'auto' = instance, then Type."),
})).optional().describe("Conditions ANDed together. Omit to match the whole category.");

server.tool("revit_query_where",
  "Deterministic element query: a TRUE total count plus matching rows, each condition resolved at the correct scope " +
  "(instance or element Type). Richer than revit_find_elements: adds regex/not_regex, starts_with, ends_with, gte, lte, " +
  "is_empty, not_empty and explicit instance-vs-type scope. count stays exact even when rows are cut by limit.", {
  category: z.string().describe("BuiltInCategory name, e.g. 'OST_Doors'."),
  where: whereField,
  select: z.array(z.string()).optional().describe("Parameter names to return per row."),
  view_id: z.number().int().positive().optional()
    .describe("Scope to elements visible in this view (non-template View id). Omit for the whole document."),
  limit: z.number().int().min(1).max(1000).optional().describe("Max rows returned. Default 100. Does not affect count."),
}, fwd("query_where"));

server.tool("revit_get_parameter", "Get one parameter's value from an element.", {
  id: z.number().int(),
  parameterName: z.string(),
}, fwd("get_parameter"));

server.tool("revit_get_warnings", "Read persistent Revit model warnings with failure IDs and affected elements. Paginated; optionally filter by element. Does not dismiss or fix warnings.", {
  elementId: z.number().int().positive().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(500).optional(),
}, fwd("get_warnings"));

server.tool("revit_list_levels", "All Levels sorted by elevation.", {}, fwd("list_levels"));
server.tool("revit_create_floor_type_variant", "Duplicate a floor type with explicit layer widths in meters and optional layerMaterialIds (native top-to-bottom order, null preserves a layer material); preserves layer functions. Optionally assign only specified floors using the source type. Does not modify the source type. Inspect layers first. Clears the duplicate type mark. Rejects existing names, pinned and grouped targets.", {
  sourceTypeId: z.number().int(),
  name: z.string().min(1),
  layerWidthsMeters: z.array(z.number().finite().nonnegative()).min(1),
  layerMaterialIds: z.array(z.number().int().positive().nullable()).optional(),
  floorIds: z.array(z.number().int()).optional(),
  dryRun: z.boolean().optional(),
}, fwdWrite("create_floor_type_variant"));

server.tool("revit_inspect_planar_faces", "Inspect planar solid faces in document coordinates: normal, area, material and optional vertical intersections at probe XY. Use to distinguish countertop from backsplash. Returns candidates, not a semantic support decision. Paginated; copied geometry indices cannot be used as hosting references. Curved faces are skipped.", {
  id: z.number().int(),
  includeEdgeLoops: z.boolean().optional(),
  probe: z.object({ x: z.number(), y: z.number(), z: z.number().optional() }).optional(),
  units: z.enum(["meters", "feet"]).optional(),
  offset: z.number().int().nonnegative().optional(),
  limit: z.number().int().min(1).max(500).optional(),
}, fwd("inspect_planar_faces"));

server.tool("revit_create_profile_wall", "Create an independent native basic wall from a strictly convex vertical polygon, useful for gable infill when attachment is unsuitable. Coordinates are absolute elevations, not level offsets. No roof linkage or automatic adaptation. Rejects curved, concave, crossed and nonvertical profiles; inspect wall thickness, roof underside and gaps first.", {
  profile: z.array(z.object({ x:z.number().finite(), y:z.number().finite(), z:z.number().finite() })).min(3).max(100),
  wallTypeId:z.number().int(), levelName:z.string().min(1), structural:z.boolean().optional(),
  flip:z.boolean().optional(), units:unitsField, dryRun:dryRunField,
  verification:z.enum(["solid","sketch"]).optional().describe("sketch: accept when the stored profile sketch matches even if the in-transaction solid does not; then verify geometry after commit (postCommitCheckRequired)."),
}, fwdWrite("create_profile_wall"));

server.tool("revit_attach_wall_top", "Revit 2026: attach an existing wall top to a roof using native attachment. Idempotent for the same roof; rejects conflicting attachments. Does not create structural supports. Review sections after committing.", {
  wallId: z.number().int(), roofId: z.number().int(), dryRun: dryRunField,
}, fwdWrite("attach_wall_top"));

server.tool("revit_detach_wall_top", "Revit 2026: remove a wall top attachment to a roof (reverse of revit_attach_wall_top). Idempotent. Reports solid top before/after; the top then follows the wall's own constraint. Review sections after committing.", {
  wallId: z.number().int(), roofId: z.number().int(), dryRun: dryRunField,
}, fwdWrite("detach_wall_top"));

server.tool("revit_analyze_wall_roof_interface", "Read-only: roof underside elevation along BOTH faces of a straight wall (full thickness), gaps to a base elevation, underside difference across thickness (eave wedge) and a proposed strictly convex profile (base..underside-clearance) ready for revit_create_profile_wall. Planar roof faces only; reports uncovered spans and split/concave profiles. baseElevation defaults to the wall solid top (meaningless once attached). Does not design bearings, plates, purlins or ring beams.", {
  wallId: z.number().int(), roofId: z.number().int(),
  clearance: z.number().finite().nonnegative().optional().describe("Gap below the roof underside, in units. Default 0."),
  baseElevation: z.number().finite().optional().describe("Absolute bottom of the proposed profile, in units."),
  units: unitsField,
}, fwd("analyze_wall_roof_interface"));

const sectionField = z.object({ width: z.number().positive(), height: z.number().positive() });

server.tool("revit_plan_purlin_roof", "Read-only layout (meters) of a timber purlin roof on masonry gables for a rectangular duopitch plan: wall plates, intermediate purlins and ridge beam bearing on gables and cantilevering over the verge, rafters over them, blocking, fascia/verge boards, boarding on rafters with the covering roof base elevation, gable profile walls under the boarding with a perimeter joint gap, purlin pockets and joint trims. Output feeds revit_create_framing_member, revit_create_roof_type_variant, revit_create_profile_wall and revit_create_opening_in_wall. Sections are inputs, NOT a structural design.", {
  x0: z.number().finite(), y0: z.number().finite(), x1: z.number().finite(), y1: z.number().finite(),
  ridgeAxis: z.enum(["x", "y"]).optional(), pitchDegrees: z.number().optional(),
  eaveOverhang: z.number().nonnegative().optional(), gableOverhang: z.number().nonnegative().optional(),
  eaveWallThickness: z.number().positive().optional(), gableWallThickness: z.number().positive().optional(),
  masonryTop: z.number().finite().optional(),
  plate: sectionField.optional(), purlin: sectionField.optional(), ridge: sectionField.optional(),
  intermediatePurlinsPerSide: z.number().int().min(0).max(4).optional(),
  rafter: sectionField.optional(), rafterMaxSpacing: z.number().positive().optional(),
  boarding: z.number().positive().optional(), membrane: z.number().nonnegative().optional(),
  battens: z.number().nonnegative().optional(), tile: z.number().nonnegative().optional(),
  jointGap: z.number().nonnegative().optional(), jointTrim: sectionField.optional(),
  purlinPocketClearance: z.number().nonnegative().optional(),
  blocking: z.boolean().optional(), blockingThickness: z.number().positive().optional(),
  fasciaBoards: z.boolean().optional(), fascia: sectionField.optional(),
  plateAlignment: z.enum(["center", "outer"]).optional().describe("outer: plate flush with the wall outer face and blocking in that plane."),
  fasciaRecess: z.number().nonnegative().optional().describe("Fascia/verge board set back from the roof edge (tile drip overhang)."),
  fasciaAutoHeight: z.boolean().optional().describe("Eave board = rafter plumb depth; verge board = rafter depth."),
}, fwd("plan_purlin_roof"));

server.tool("revit_create_framing_member", "Create a Structural Framing member between two absolute 3D points (sloped allowed, unlike revit_create_beam which flattens to the level). zJustification/yJustification define which face/edge of the section lies on the line (rafters: bottom; purlins/plates: top). Ends are not auto-joined unless allowJoins. Verifies the location line after commit. Placement only, not structural design.", {
  typeId: z.number().int(), start: xyz, end: xyz, levelName: z.string().optional(),
  zJustification: z.enum(["top", "center", "bottom", "origin"]).optional(),
  yJustification: z.enum(["left", "center", "right", "origin"]).optional(),
  crossSectionRotationDegrees: z.number().finite().optional(),
  structural: z.boolean().optional().describe("false sets structural usage Other (trims, boards)."),
  allowJoins: z.boolean().optional(), comments: z.string().optional().describe("Tag for later selection, e.g. MCP-COBERTURA:rafter"),
  units: unitsField, dryRun: dryRunField,
}, fwdWrite("create_framing_member"));

server.tool("revit_create_roof_type_variant", "Duplicate a roof type with an explicit layer list, top (exterior) first: function, widthMeters (0 only for Membrane), materialId or exact materialName, optional core flag. Verifies layers. Does not change any roof.", {
  sourceTypeId: z.number().int(), name: z.string().min(1),
  layers: z.array(z.object({
    function: z.enum(["Structure", "Substrate", "Insulation", "Finish1", "Finish2", "Membrane", "StructuralDeck"]),
    widthMeters: z.number().nonnegative(), materialId: z.number().int().optional(), materialName: z.string().optional(),
    core: z.boolean().optional(),
  })).min(1).max(20),
  dryRun: dryRunField,
}, fwdWrite("create_roof_type_variant"));

server.tool("revit_list_open_documents", "List open (non-linked) Revit documents, which is active, paths and modified state. Every other tool accepts documentTitle/documentPath to target one of them.", {}, fwd("list_open_documents"));

server.tool("revit_activate_document", "UI action: make an open, saved document active; with path + allowOpen:true it opens a closed file (e.g. the office showroom) and activates it. Needed for UI actions; model reads/writes can use documentTitle instead.", {
  title: z.string().optional(), path: z.string().optional(), allowOpen: z.boolean().optional(),
}, fwd("activate_document"));

server.tool("revit_save_document", "Save the target document (active or documentTitle) to its file, or a copy with saveAsPath (.rvt/.rfa, overwrite:true to replace). Refuses never-saved documents without saveAsPath, read-only files and open edit modes. Verifies the document is no longer modified and the file was written. Workshared: saves the local file, does not synchronize. Not batchable; dryRun reports the target only.", {
  saveAsPath: z.string().optional(), overwrite: z.boolean().optional(), compact: z.boolean().optional(), dryRun: dryRunField,
}, fwdWrite("save_document"));

server.tool("revit_list_recent_documents", "List Revit's recent files (Revit.ini of the running version), newest first, with existence, size, last write and whether each is open. query filters by file name (accents/case/separators ignored, every word must appear).", {
  query: z.string().optional(),
}, fwd("list_recent_documents"));

server.tool("revit_open_document", "UI action: open a .rvt/.rfa and activate it (activate:false opens without a window; target it with documentTitle). Pass path, or name as the user said it ('ginásio tapurah R05') to resolve against the recent file list; ambiguous names return the candidates. Already open files are just activated. Refuses newer-version files; older ones need allowUpgrade:true; central models need detachFromCentral:true. Dialogs Revit raises while opening still need the user.", {
  path: z.string().optional(), name: z.string().optional(), activate: z.boolean().optional(),
  allowUpgrade: z.boolean().optional(), detachFromCentral: z.boolean().optional(),
}, fwd("open_document"));

server.tool("revit_import_cad", "Link (default, keeps the DWG external and removable) or import a .dwg/.dxf into a view (viewId or the active view). Plan views get a view-specific underlay unless thisViewOnly:false. unit auto|m|cm|mm|dm|ft|in (or customScale), placement origin|center|shared, colorMode blackAndWhite|preserve|invert. Returns the import instance, layers and extents in meters: check them against the expected building size before tracing. DWG texts/dimensions are not readable.", {
  path: z.string(), mode: z.enum(["link", "import"]).optional(), viewId: z.number().int().optional(),
  unit: z.string().optional(), customScale: z.number().positive().optional(), placement: z.enum(["origin", "center", "shared"]).optional(),
  colorMode: z.enum(["blackAndWhite", "preserve", "invert"]).optional(), thisViewOnly: z.boolean().optional(),
  visibleLayersOnly: z.boolean().optional(), autoCorrectAlmostVHLines: z.boolean().optional(), dryRun: dryRunField,
}, fwdWrite("import_cad"));

server.tool("revit_place_image_in_view", "Link or import a local raster image into a 2D view with an explicit model-space width. Use linked images for map extracts so the source remains replaceable; preserve source attribution in the image.", {
  path: z.string().describe("Existing absolute local image path."),
  viewId: z.number().int().optional(),
  center: xyz.optional().describe("Image center in view/model coordinates. Defaults to origin."),
  width: z.number().positive().describe("Displayed width in the selected units."),
  units: unitsField,
  link: z.boolean().optional().describe("Link the image file instead of embedding it. Default true."),
  dryRun: dryRunField,
}, fwdWrite("place_image_in_view"));

server.tool("revit_inspect_cad", "Read a linked/imported CAD instance. No importInstanceId: list CAD instances. mode summary (default): layers with counts, lengths and extents, blocks, overall extents. mode curves: plan geometry in meters (t line|arc|circle|poly|curve, l layer, b block, p points), filtered by layers and bboxMeters {minX,minY,maxX,maxY}, paginated (limit <= 10000). Use summary first to pick wall/door layers.", {
  importInstanceId: z.number().int().optional(), mode: z.enum(["summary", "curves"]).optional(),
  layers: z.array(z.string()).optional(), bboxMeters: z.object({ minX: z.number(), minY: z.number(), maxX: z.number(), maxY: z.number() }).optional(),
  offset: z.number().int().min(0).optional(), limit: z.number().int().min(1).max(10000).optional(),
}, fwd("inspect_cad"));

server.tool("revit_set_view_crop", "Crop a plan view to a model rectangle in meters (bboxMeters {minX,minY,maxX,maxY}) or clear:true; crop a section or elevation to viewRectMeters {minU,minV,maxU,maxV} in view coordinates (right/up, as get_annotation_extents reports), keeping its depth — room for callouts and dimension chains. Use before revit_get_view_image to inspect a detail (doors, stairs) that is invisible in a whole-site export. Refuses scope-box-driven crops. 3D views: revit_set_section_box.", {
  viewId: z.number().int(), bboxMeters: z.object({ minX: z.number(), minY: z.number(), maxX: z.number(), maxY: z.number() }).optional(),
  viewRectMeters: z.object({ minU: z.number(), minV: z.number(), maxU: z.number(), maxV: z.number() }).optional().describe("Sections/elevations: crop rectangle in view coordinates (metres)."),
  clear: z.boolean().optional(), cropVisible: z.boolean().optional(), dryRun: dryRunField,
}, fwdWrite("set_view_crop"));

server.tool("revit_edit_curtain_wall", "Edit a curtain wall: listPanels (types, bboxes), addGridLines [{orientation vertical|horizontal, point}] through points on the wall, setPanels [{point, typeId}] changes the panel containing the point (e.g. to a curtain door panel type such as a double glass door; hosted door families cannot go into curtain walls). Unpins type-driven panels; verifies the new type.", {
  wallId: z.number().int(), listPanels: z.boolean().optional(),
  removeGridLines: z.array(z.object({ orientation: z.enum(["vertical", "horizontal"]), point: xyz, toleranceMeters: z.number().positive().optional() })).optional()
    .describe("Applied first: delete the grid line through the point (type-driven lines are unpinned). Merge panels back to glass before removing lines around a door panel."),
  addGridLines: z.array(z.object({ orientation: z.enum(["vertical", "horizontal"]), point: xyz })).optional(),
  removeSegments: z.array(z.object({ orientation: z.enum(["vertical", "horizontal"]), point: xyz, toleranceMeters: z.number().positive().optional() })).optional()
    .describe("After addGridLines: remove only the segment of the grid line at the point (vertical: the part at the point's height), merging two panels, e.g. a double door across two columns below the door head while the lights above keep the line."),
  setPanels: z.array(z.object({ point: xyz, typeId: z.number().int() })).optional(),
  units: unitsField, dryRun: dryRunField,
}, fwdWrite("edit_curtain_wall"));

server.tool("revit_snap_wall_ends", "Close wall ends that stop short of or overshoot the wall they meet: L corners and T junctions go to the centerline intersection, collinear gaps close, within toleranceMeters (default 0.6). Allows joins at moved ends and keeps hosted doors/windows in place. Curtain walls skipped unless includeCurtain. Pass wallIds or levelName (default all walls). Always dryRun first and review the moves: free ends may be real (site walls, counters).", {
  wallIds: z.array(z.number().int()).optional(), levelName: z.string().optional(),
  toleranceMeters: z.number().positive().max(3).optional(), minAngleDeg: z.number().positive().optional(),
  includeCurtain: z.boolean().optional(), dryRun: dryRunField,
}, fwdWrite("snap_wall_ends"));

server.tool("revit_merge_walls", "Replace nearly collinear wall segments (same type, within maxOffsetMeters, default 0.1) by one straight wall on the longest segment's centerline spanning their union, extended to the nearest crossing wall at each end (extendToWalls, default true, reach 1 m). Hosted doors/windows are recreated on the new wall with the same type and flips. Prefer this over nudging pieces into line. dryRun first.", {
  wallIds: z.array(z.number().int()).min(2), maxOffsetMeters: z.number().positive().optional(),
  extendToWalls: z.boolean().optional(), extendReachMeters: z.number().positive().optional(), dryRun: dryRunField,
}, fwdWrite("merge_walls"));

server.tool("revit_create_room_separators", "Create room separation lines on a level (plan view found automatically or viewId) to close rooms where the boundary is not a wall: glass gaps at columns, open fronts, circulation limits.", {
  lines: z.array(z.object({ start: xyz, end: xyz })).min(1), levelName: z.string().optional(), viewId: z.number().int().optional(),
  units: unitsField, dryRun: dryRunField,
}, fwdWrite("create_room_separators"));

server.tool("revit_link_point_cloud", "Link a ReCap point cloud (.rcp project or .rcs scan). placement origin keeps cloud coordinates; center puts the plan centre on the internal origin and the lowest point on levelName + elevationOffset. Reuses a loaded type with the same path. Raw scans (.e57/.las/.pts) first go through revit_recipe_convert_scans.", {
  path: z.string(), placement: z.enum(["origin", "center"]).optional(), levelName: z.string().optional(),
  elevationOffset: z.number().finite().optional(), dryRun: dryRunField,
}, fwdWrite("link_point_cloud"));

server.tool("revit_inspect_point_cloud", "Point clouds in the document. No instanceId: instances with path, found status, extents, scan count, supported engines. With instanceId: scans (name, scanner origin), regions and duplicate hints (sameOriginGroups: scans at the same scanner position; sameNameGroups: names equal up to a copy suffix).", {
  instanceId: z.number().int().optional(), toleranceMeters: z.number().positive().optional(),
}, fwd("inspect_point_cloud"));

server.tool("revit_set_point_cloud_display", "View-specific point cloud display: visible and colorMode (noOverride|elevation|intensity|normals|fixedColor with color {r,g,b}) for the whole instance, named scans or regions. Hide duplicated scans in a working view or colour by elevation to read floors/ceilings.", {
  viewId: z.number().int(), instanceId: z.number().int(), scans: z.array(z.string()).optional(), regions: z.array(z.string()).optional(),
  visible: z.boolean().optional(), colorMode: z.enum(["noOverride", "elevation", "intensity", "normals", "fixedColor"]).optional(),
  color: z.object({ r: z.number().int(), g: z.number().int(), b: z.number().int() }).optional(), dryRun: dryRunField,
}, fwdWrite("set_point_cloud_display"));

server.tool("revit_sample_point_cloud", "Read points of a linked point cloud inside boxMeters {minX..maxZ} (default the whole cloud): count, extents, z histogram (floors/ceilings are spikes). outFile writes x y z meters for planners; inlineLimit returns up to 20000 points. averageDistanceMeters controls density (default 0.01), maxPoints caps the read (default 500000).", {
  instanceId: z.number().int(), boxMeters: z.object({ minX: z.number().optional(), minY: z.number().optional(), minZ: z.number().optional(), maxX: z.number().optional(), maxY: z.number().optional(), maxZ: z.number().optional() }).optional(),
  averageDistanceMeters: z.number().positive().optional(), maxPoints: z.number().int().positive().optional(), zBinMeters: z.number().positive().optional(),
  outFile: z.string().optional(), inlineLimit: z.number().int().min(0).max(20000).optional(), includeColor: z.boolean().optional(),
}, fwd("sample_point_cloud"));

server.tool("revit_create_wall_type_variant", "Duplicate a basic wall type with optional widthMeters, layerWidthsMeters or layerMaterialIds. Arrays use native exterior-to-interior layer order; null material IDs preserve the source layer. Material-only changes preserve widths. Reuses an existing name only when layer widths, functions, core boundaries and materials match. Does not change existing wall instances; assign the returned type explicitly.", {
  sourceTypeId: z.number().int(), name: z.string().min(1), widthMeters: z.number().positive().optional(),
  layerWidthsMeters: z.array(z.number().nonnegative()).optional(),
  layerMaterialIds: z.array(z.number().int().positive().nullable()).optional(), dryRun: dryRunField,
}, fwdWrite("create_wall_type_variant"));

server.tool("revit_copy_elements_from_document", "Copy model elements or element types (roof/fascia/gutter/framing types, families) from another open document (e.g. the office showroom) into the target document. Same-name destination types are reused. Optional translation in units. Check hosts and positions of copied instances.", {
  sourceDocumentTitle: z.string().optional(), sourceDocumentPath: z.string().optional(),
  elementIds: z.array(z.number().int()).min(1).max(500), translation: xyz.optional(), units: unitsField, dryRun: dryRunField,
}, fwdWrite("copy_elements_from_document"));

server.tool("revit_create_roof_edge_sweep", "Place roof edge sweeps on classified roof edges: Fascia types (ridge caps 'cumeeira', flashings 'rufo', fascia boards) or Gutter types ('calha'). edgeKinds: ridge (shared by two top/bottom faces), eave (horizontal, unshared), verge (sloped). One element per edge; verifies each sweep lies on its edge. Planar roof faces.", {
  roofId: z.number().int(), typeId: z.number().int(),
  edgeKinds: z.array(z.enum(["ridge", "eave", "verge"])).min(1), faceSide: z.enum(["top", "bottom"]).optional(),
  horizontalOffset: z.number().finite().optional().describe("Profile horizontal offset from the edge, meters (HostedSweep.HorizontalOffset)."),
  verticalOffset: z.number().finite().optional().describe("Profile vertical offset from the edge, meters (HostedSweep.VerticalOffset)."),
  flipHorizontal: z.boolean().optional(), flipVertical: z.boolean().optional(),
  maxProfileReachMeters: z.number().positive().optional(),
  dryRun: dryRunField,
}, fwdWrite("create_roof_edge_sweep"));

server.tool("revit_place_curve_family_instance", "Place a line-based family instance (e.g. ceiling shadow-gap trim 'tabica', linear generic sweeps). mode 'face' hosts it on the bottom/top planar face of hostId nearest the line (line projected onto the face); mode 'level' places it unhosted on a level at the line elevation. Verifies the location curve. Profile side is family-defined: check a section and swap start/end if needed.", {
  typeId: z.number().int(), start: xyz, end: xyz,
  mode: z.enum(["face", "level"]).optional(), hostId: z.number().int().optional(), faceSide: z.enum(["bottom", "top"]).optional(),
  levelName: z.string().optional(), comments: z.string().optional(), units: unitsField, dryRun: dryRunField,
}, fwdWrite("place_curve_family_instance"));

server.tool("revit_create_profile_family", "Create and load a profile family (for roof edge sweeps: ridge caps, flashings, gutters, fascia) from a closed loop [{u,v}] in meters or a generated ridgeCap {pitchDegrees, wingLength, thickness} whose wings follow the roof pitch. Origin = host edge, u across, v up. Then duplicate a fascia/gutter type (revit_duplicate_family_type) and set its 'Perfil' parameter to the new profile type {id}. Runs outside batches; dryRun validates the loop only.", {
  familyName: z.string().min(1), loop: z.array(z.object({ u: z.number(), v: z.number() })).min(3).optional(),
  ridgeCap: z.object({ pitchDegrees: z.number(), wingLength: z.number().positive(), thickness: z.number().positive() }).optional(),
  templatePath: z.string().optional(), dryRun: dryRunField,
}, fwdWrite("create_profile_family"));

server.tool("revit_join_geometry", "Join geometry of a cutter (e.g. the roof covering) with elements that intersect it (square blocking or fascia boards rising into sloped boarding) and ensure the cutter cuts them. Verifies the join, cut order and that each element's solid volume decreased. Geometry only.", {
  cutterId: z.number().int(), elementIds: z.array(z.number().int()).min(1).max(500),
  requireVolumeChange: z.boolean().optional().describe("Default true: fail if an element does not intersect or is not trimmed."),
  dryRun: dryRunField,
}, fwdWrite("join_geometry"));

server.tool("revit_inspect_scope_boxes", "Read-only: scope boxes with rotation-aware extents, bound levels/grids and views, and model elements (default architectural/structural categories) outside each box with the extents required to enclose them. Revit API cannot resize scope box plan extents: move/rotate the box or set its Height, otherwise ask the user. Never use scope box or level extents as construction references.", {
  categories: z.array(z.string()).optional().describe("BuiltInCategory names, e.g. OST_Walls."),
  maxOutsideListed: z.number().int().min(0).max(500).optional(),
}, fwd("inspect_scope_boxes"));

server.tool("revit_set_family_orientation", "Set facing/hand flipped states on an existing family instance without recreation. Idempotent target states. Verifies host and insertion point remain unchanged. Does not infer the real front or exterior: inspect geometry and images before and after.", {
  id: z.number().int(),
  facingFlipped: z.boolean().optional(),
  handFlipped: z.boolean().optional(),
  dryRun: z.boolean().optional(),
}, fwdWrite("set_family_orientation"));

server.tool("revit_get_type_structure", "Read native compound layers of a wall/floor/roof/ceiling type: widths in meters, functions, materials and core boundaries. Does not infer construction from the type name.", {
  typeId: z.number().int().positive(),
}, fwd("get_type_structure"));
server.tool("revit_list_wall_types", "All WallTypes.", {}, fwd("list_wall_types"));
server.tool("revit_list_floor_types", "All FloorTypes.", {}, fwd("list_floor_types"));
server.tool("revit_list_categories", "Categories actually used in the doc, with instance counts.", {}, fwd("list_categories"));

server.tool("revit_list_families", "List loaded Families, optionally by category. Paginated: total, hasMore, nextOffset.", {
  category: z.string().optional(),
  offset: z.number().int().min(0).optional(),
  limit: z.number().int().min(1).max(5000).optional(),
}, fwd("list_families"));

server.tool("revit_list_family_types", "List loaded family types and their placement type. Set includeSystemTypes to also inspect ceiling, wall, floor and other ElementTypes.", {
  familyName: z.string().optional(),
  category: z.string().optional(),
  includeSystemTypes: z.boolean().optional().describe("Include system and other non-loadable ElementTypes. Default false. Use category to narrow results, e.g. OST_Ceilings."),
  limit: z.number().int().min(1).max(5000).optional(),
}, fwd("list_family_types"));

server.tool("revit_list_sheets", "All sheets with number, name, viewport count.", {}, fwd("list_sheets"));
server.tool("revit_list_rooms", "All placed rooms with area, number, level.", {}, fwd("list_rooms"));
server.tool("revit_list_spaces",
  "All placed MEP Spaces (OST_MEPSpaces) with area, volume, number, level. " +
  "Use levelId (from revit_list_levels) to filter to a single level.",
  {
    levelId: z.number().int().optional().describe("ElementId of a Level — return only spaces on that level."),
    limit: z.number().int().min(1).max(2000).optional().describe("Max spaces to return. Default 500."),
  },
  fwd("list_spaces"));
server.tool("revit_list_materials", "All materials with class, category, color.", {}, fwd("list_materials"));
server.tool("revit_create_material", "Create an independent Generic material and appearance. Refuses existing names. Optional local bitmap cutout: white opaque, black empty, tiled at textureScaleMeters. Use for lightweight mesh infill without modeled wires. Realistic/rendered views use the cutout; shaded views use approximate uniform graphicsTransparency. Does not assign the material to elements.", {
  name: z.string().min(1), color: z.string().regex(/^#[0-9a-fA-F]{6}$/).optional(),
  graphicsTransparency: z.number().int().min(0).max(100).optional(), materialClass: z.string().optional(),
  crosshatch: z.boolean().optional(), crosshatchSpacingMeters: z.number().min(0.005).max(1).optional(), crosshatchAngleDegrees: z.number().optional(),
  cutoutImagePath: z.string().optional(), textureScaleMeters: z.number().positive().optional(), dryRun: dryRunField,
}, fwdWrite("create_material"));
server.tool("revit_list_phases", "All phases.", {}, fwd("list_phases"));
server.tool("revit_list_view_templates", "All view templates.", {}, fwd("list_view_templates"));

server.tool("revit_get_views", "All non-template views with type, level, scale.", {}, fwd("get_views"));

server.tool("revit_get_model_health",
  "One-shot model health report (read-only). Aggregates the metrics a BIM manager checks to judge model quality: " +
  "warnings (count + top groups), file size, imported vs linked CAD, in-place families, model/detail groups, " +
  "views not on sheets, element counts by category, and complexity counts (levels, grids, design options, reference planes). " +
  "Returns a scorecard with a letter grade (A-F), a 0-100 score, and flagged issues. " +
  "Use this for a quick 'is this model healthy?' overview before deeper work.",
  {
    deep: z.boolean().optional().describe("Also run the purge scan (Document.GetUnusedElements). Slower on large models. Default false."),
    topN: z.number().int().min(1).max(50).optional().describe("How many top warning groups / categories to list. Default 10."),
  },
  fwd("get_model_health"));

server.tool("revit_get_worksets",
  "List user worksets with per-workset element counts (read-only). " +
  "Flags empty worksets (no instances) and un-renamed defaults ('Workset1'). " +
  "Returns isWorkshared, count, emptyCount, and per-workset details (name, elementCount, isEmpty, isOpen, owner). " +
  "For a non-workshared model returns isWorkshared=false.",
  {},
  fwd("get_worksets"));
server.tool("revit_get_active_view", "Current active view info.", {}, fwd("get_active_view"));
server.tool("revit_get_selected_elements", "Elements currently selected by the user in Revit UI.", {}, fwd("get_selected_elements"));
server.tool("revit_get_linked_files", "List Revit link instances (metadata only — id, name, load status).", {}, fwd("get_linked_files"));

server.tool("revit_get_linked_elements",
  "Read elements that live INSIDE a specific linked RVT file. Bounding boxes are transformed to host-model coordinates. Use this before check_clearance to inspect what's in a link.",
  {
    linkId: z.number().int().describe("ElementId of the RevitLinkInstance (from revit_get_linked_files)."),
    category: z.string().optional().describe("BuiltInCategory name, e.g. 'OST_DuctCurves'. Omit for all elements."),
    limit: z.number().int().min(1).max(2000).optional().describe("Max elements to return. Default 200."),
  },
  fwd("get_linked_elements"));

server.tool("revit_get_element_geometry", "Bounding box, centroid, volume, surface area, face/solid counts.", {
  id: z.number().int(),
}, fwd("get_element_geometry"));

// ═══════════════════════════════════════════════════════════════════════════
// CREATION — ARCHITECTURE
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_create_wall", "Create a single straight wall. start/end lie on locationLine (default wall centerline, verified after creation). Returns changeSummary for diff review.", {
  start: xyz, end: xyz.describe("End point."),
  height: z.number().positive().optional().describe("Default 3.0 m."),
  baseOffset: z.number().finite().optional().describe("Wall base offset relative to level, in units. Default 0. Height is measured from the offset base: use -0.05 and height 2.85 to keep the top at level +2.80."),
  levelName: z.string().optional(),
  wallTypeName: z.string().optional(),
  wallTypeId: z.number().int().optional().describe("Wins over wallTypeName."),
  locationLine: z.enum(["centerline", "coreCenterline", "finishExterior", "finishInterior", "coreExterior", "coreInterior"]).optional(),
  structural: z.boolean().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_wall"));

server.tool("revit_create_curved_wall", "Create a curved wall on a circle: an arc (startAngleDeg..endAngleDeg) or a full circle (360, built as two half walls because Revit walls cannot be closed). radius refers to the centerline, interior or exterior face (radiusReference); the centerline is verified after creation. Structural by default, ready to host rebar.", {
  center: xyz.describe("Circle centre (z ignored; the wall sits on the level)."),
  radius: z.number().positive(),
  radiusReference: z.enum(["centerline", "interior", "exterior"]).optional().describe("Default centerline. For a tank 'Ø10.70 m interno' use interior with radius 5.35."),
  startAngleDeg: z.number().optional().describe("Default 0 (along +X), counter-clockwise."),
  endAngleDeg: z.number().optional().describe("Default 360 (full circle)."),
  height: z.number().positive().optional().describe("Default 3.0 m."),
  baseOffset: z.number().finite().optional(),
  levelName: z.string().optional(),
  wallTypeName: z.string().optional(),
  wallTypeId: z.number().int().optional(),
  structural: z.boolean().optional().describe("Default true."),
  units: unitsField, dryRun: dryRunField,
}, fwdWrite("create_curved_wall"));

server.tool("revit_create_floor", "Create a Floor from a closed polygonal profile (>=3 points). heightOffset raises the top above the level (canopies, steps, paving) without a level per height; reports top/bottom elevations.", {
  profile: z.array(xyz).min(3),
  heightOffset: z.number().finite().optional().describe("Top of floor above the level, in units. Default 0."),
  holes: z.array(z.array(xyz).min(3)).optional().describe("Inner loops cut out of the floor (courtyards, shafts, other floors)."),
  snapNearAxisDegrees: z.number().min(0).max(5).optional().describe("Edges this close to horizontal/vertical become exact (Revit rolls back 'slightly off axis' sketches). Default 0.5; 0 disables."),
  slope: z.object({ from: xyz, to: xyz, rise: z.number().finite() }).optional().describe("Sloped floor (ramps): rises `rise` from the plan point `from` (at the level + heightOffset) to `to`."),
  structural: z.boolean().optional(),
  levelName: z.string().optional(),
  floorTypeName: z.string().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_floor"));

server.tool("revit_create_stair", "Create a component stair between two levels from straight runs (meters) with automatic landings between consecutive runs: straight, L or U layouts by run start points and directions. risers per run add up to the total; dryRun builds the stair inside the edit scope and cancels it, reporting riser height, tread, Blondel (2h+g) and run ends. Type comfort warnings are reported, errors cancel.", {
  baseLevelName: z.string(), topLevelName: z.string(),
  runs: z.array(z.object({ start: xyz, direction: xyz.describe("Plan direction going up."), risers: z.number().int().min(2) })).min(1),
  width: z.number().positive().optional().describe("Run width, default 1.00."),
  treadDepth: z.number().positive().optional().describe("Default: the stair type's tread depth."),
  dryRun: dryRunField,
}, fwdWrite("create_stair"));

server.tool("revit_create_railing", "Create a railing along an open polyline on a level (meters): balcony guards, entrance stair and ramp handrails. hostId makes it follow a sloped ramp/floor or stair top; flip swaps the side of the path.", {
  path: z.array(xyz).min(2), levelName: z.string(),
  railingTypeName: z.string().optional(), railingTypeId: z.number().int().optional(),
  baseOffset: z.number().finite().optional().describe("Meters above the level, default 0."),
  hostId: z.number().int().optional(), flip: z.boolean().optional(),
  dryRun: dryRunField,
}, fwdWrite("create_railing"));

const balusterRef = z.string().describe("'host', 'topRail' or the name of one of the rails.");
server.tool("revit_configure_railing_type", "Configure the build-up of a railing type (meters): top rail, non-continuous rails (e.g. a bottom rail), the main baluster pattern (one baluster at a spacing; dense thin bars read as mesh infill), start/corner/end posts and balusters per tread. These live in RailStructure/BalusterPlacement, not in type parameters. duplicateAs configures a copy. Omitted sections stay unchanged; returns the resulting build-up.", {
  typeId: z.number().int().optional(), typeName: z.string().optional(),
  duplicateAs: z.string().optional().describe("Duplicate the type under this name and configure the copy."),
  topRail: z.object({
    enabled: z.boolean().optional(), typeId: z.number().int().optional(), typeName: z.string().optional(),
    height: z.number().positive().optional(),
  }).optional(),
  rails: z.array(z.object({
    name: z.string(), height: z.number().nonnegative(), offset: z.number().finite().optional(),
    profileId: z.number().int().optional(), profileName: z.string().optional().describe("'Family: Type' or a unique type name."),
    materialName: z.string().optional(),
  })).optional().describe("Replaces all non-continuous rails."),
  balusters: z.object({
    familyTypeId: z.number().int().optional(), familyTypeName: z.string().optional(), spacing: z.number().positive(),
    baseReference: balusterRef.optional(), baseOffset: z.number().finite().optional(),
    topReference: balusterRef.optional(), topOffset: z.number().finite().optional(), offset: z.number().finite().optional(),
    justification: z.enum(["Beginning", "End", "Center", "SpreadPatternToFit"]).optional(),
    breakPattern: z.enum(["EachSegmentEnd", "AnglesGreaterThan", "Never"]).optional(),
  }).optional(),
  posts: z.object({
    familyTypeId: z.number().int().optional(), familyTypeName: z.string().optional(),
    baseReference: balusterRef.optional(), baseOffset: z.number().finite().optional(),
    topReference: balusterRef.optional(), topOffset: z.number().finite().optional(),
    cornerCondition: z.string().optional().describe("BreakCornerCondition name, e.g. EachSegmentEnd, AnglesGreaterThan, Never."),
  }).optional(),
  balusterPerTread: z.object({ enabled: z.boolean(), number: z.number().int().min(1).optional(), familyTypeId: z.number().int().optional(), familyTypeName: z.string().optional() }).optional(),
  dryRun: dryRunField,
}, fwdWrite("configure_railing_type"));

server.tool("revit_create_direct_shape", "Create a DirectShape (default Generic Models) from boxes, cylinders and extrusions in meters, for small built items no family covers: AC enclosures, meter boxes, downpipes, lightning rod masts, gate headers. Optional material by name; name goes to Comments.", {
  name: z.string().optional(), category: z.string().optional(), materialName: z.string().optional(),
  solids: z.array(z.union([
    z.object({ type: z.literal("box"), min: xyz, max: xyz }),
    z.object({ type: z.literal("cylinder"), center: xyz, radius: z.number().positive(), zMin: z.number(), zMax: z.number() }),
    z.object({ type: z.literal("extrusion"), profile: z.array(xyz).min(3), zMin: z.number(), zMax: z.number() }),
    z.object({ type: z.literal("panel"), profile: z.array(xyz).min(3).max(32), thicknessMeters: z.number().positive().max(0.1) }),
  ])).min(1),
  dryRun: dryRunField,
}, fwdWrite("create_direct_shape"));

server.tool("revit_create_level", "Create a Level at given elevation.", {
  elevation: z.number(),
  name: z.string().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_level"));

server.tool("revit_create_grid", "Create a straight Grid line.", {
  start: xyz, end: xyz.describe("End point."),
  name: z.string().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_grid"));

server.tool("revit_create_room", "Place a Room at a given point. phaseName places it in that phase (e.g. 'Existente' for a survey of an existing building); without it Revit uses the last phase.", {
  location: xyz,
  levelName: z.string().optional(),
  name: z.string().optional(),
  number: z.string().optional(),
  phaseName: z.string().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_room"));

server.tool("revit_create_column", "Place a structural column.", {
  location: xyz,
  levelName: z.string().optional(),
  familyName: z.string().optional(),
  familyTypeName: z.string().optional(),
  structural: z.boolean().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_column"));

server.tool("revit_create_beam", "Place a structural beam between two points.", {
  start: xyz, end: xyz.describe("End point."),
  levelName: z.string().optional(),
  familyName: z.string().optional(),
  familyTypeName: z.string().optional(),
  structural: z.boolean().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_beam"));

server.tool("revit_create_gable_roof", "Create a native rectangular footprint roof: two slopes along ridgeAxis, or slopedEdges for a lean-to (one edge, the roof rises away from it), gable (two opposite) or hip (four). min/max XY include desired overhang. Attach walls afterwards with revit_attach_wall_top.", {
  min: xyz, max: xyz,
  ridgeAxis: z.enum(["x", "y"]).optional().describe("Required unless slopedEdges is given."),
  slopedEdges: z.array(z.enum(["xMin", "xMax", "yMin", "yMax"])).min(1).optional().describe("Edges that define slope; e.g. ['xMax'] for a lean-to falling towards xMax."),
  slopeDegrees: z.number().positive().lt(80),
  roofTypeId: z.number().int().positive(),
  levelName: z.string(),
  baseOffset: z.number().finite().describe("Roof base offset above level, in units; geometry thickness affects actual roof surfaces."),
  units: unitsField, dryRun: dryRunField,
}, fwdWrite("create_gable_roof"));

server.tool("revit_create_ceiling", "Create a Ceiling from a closed polygonal profile.", {
  profile: z.array(xyz).min(3),
  levelName: z.string().optional(),
  ceilingTypeName: z.string().optional(),
  perimeterGap: z.number().finite().nonnegative().optional().describe("Inset from the supplied boundary in units; supported only for axis-aligned rectangular profiles. Creates a geometric gap, not trim or a complete joint assembly."),
  heightOffset: z.number().finite().optional().describe("Offset above the selected level, in units (default meters). Omit to preserve Revit's default. Returns the actual offset in feet and meters."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_ceiling"));

server.tool("revit_create_opening_in_wall", "Create a rectangular opening in a wall.", {
  wallId: z.number().int(),
  lower: xyz.describe("Bottom-left corner."),
  upper: xyz.describe("Top-right corner."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_opening_in_wall"));

server.tool("revit_place_family_instance", "Place a FamilyInstance at a point. Pass hostId (wall element id) for doors/windows so Revit uses the hosted overload and auto-cuts the opening. Without hostId the instance is placed non-hosted (no opening cut). When selection is ambiguous returns a candidate list with placed:false.", {
  location: xyz.describe("Insertion point in units. Explicit z is absolute model elevation; omitted z uses the selected level elevation. A constrained family that cannot retain explicit z returns placement_mismatch."),
  familyName: z.string().optional(),
  familyTypeName: z.string().optional(),
  category: z.string().optional().describe("BuiltInCategory to narrow the search."),
  levelName: z.string().optional(),
  hostId: z.number().int().optional().describe("Element id of the host wall/face. Required for doors and windows to cut the opening."),
  hostSide: z.enum(["interior", "exterior", "top"]).optional().describe("Opt into planar face placement for WorkPlaneBased families. interior/exterior: wall side faces (Revit wall sides, not room containment); requires explicit location.z and referenceDirection, point within 50 mm of the face. top: a Floor host's top face (basins, drains); location.z above the face is applied as host offset (e.g. inset basin at countertop height); referenceDirection optional, horizontal."),
  floorId: z.number().int().positive().optional().describe("Explicit finished Floor in the active document. Pair with heightAboveFloor and omit location.z. Reads actual top surface at location XY; holes, missing and ambiguous surfaces are rejected."),
  heightAboveFloor: z.number().finite().optional().describe("Family insertion point height above the selected floor surface, in units. Not necessarily seat/top-of-fixture height. With hostSide, the computed point must lie on the wall face without projection."),
  referenceDirection: xyz.optional().describe("Dimensionless nonzero tangent vector controlling face placement orientation. Required with hostSide; must lie in the face plane."),
  flipFacing: z.boolean().optional().describe("Flip the door/window facing side (which side the door opens toward)."),
  flipHand: z.boolean().optional().describe("Flip the door/window hand side (hinge side)."),
  structural: z.boolean().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("place_family_instance"));

// ═══════════════════════════════════════════════════════════════════════════
// CREATION — DOCUMENTATION
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_create_sheet", "Create a ViewSheet.", {
  sheetNumber: z.string().optional(),
  sheetName: z.string().optional(),
  titleBlockName: z.string().optional(),
  dryRun: dryRunField,
}, fwdWrite("create_sheet"));

server.tool("revit_place_view_on_sheet", "Place a view (Viewport) or native schedule (ScheduleSheetInstance) on a sheet. Refuses a duplicate schedule on the same sheet.", {
  sheetId: z.number().int(),
  viewId: z.number().int(),
  location: xyz.optional().describe("Sheet coordinates in feet: viewport center, or upper-left insertion point for a schedule. Defaults to roughly center."),
  dryRun: dryRunField,
}, fwdWrite("place_view_on_sheet"));

server.tool("revit_create_floor_plan_view", "Create a floor plan view for a level.", {
  levelName: z.string(),
  viewName: z.string().optional(),
  dryRun: dryRunField,
}, fwdWrite("create_floor_plan_view"));

server.tool("revit_create_drafting_view", "Create a drafting view for map extracts, legends and other 2D documentation.", {
  viewName: z.string().optional(),
  scale: z.number().int().min(1).max(24000).optional(),
  dryRun: dryRunField,
}, fwdWrite("create_drafting_view"));

server.tool("revit_create_section_view", "Create a section view.", {
  origin: xyz, direction: xyz.describe("Direction vector."),
  depth: z.number().optional().describe("Cut depth, default 10 m."),
  width: z.number().optional().describe("Half-width, default 10 m."),
  height: z.number().optional().describe("Half-height, default 5 m."),
  viewName: z.string().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_section_view"));

server.tool("revit_create_3d_view", "Create an isometric 3D view.", {
  viewName: z.string().optional(),
  dryRun: dryRunField,
}, fwdWrite("create_3d_view"));

server.tool("revit_create_perspective_view",
  "Create perspective 3D view(s) with the camera at explicit coordinates. Unlike revit_create_3d_view " +
  "(isometric, or a duplicate of the active view) this places the eye and aims it, so a set of renders can " +
  "share one viewpoint. Pass azimuthsDeg to get several views from the SAME eye rotated about vertical — " +
  "images shot from different eye heights reconstruct badly.",
  {
    eye: xyz.describe("Camera position."),
    target: xyz.describe("Point the camera looks at. Must differ from eye."),
    units: unitsField,
    azimuthsDeg: z.array(z.number()).min(1).optional()
      .describe("One view per angle, all from the same eye. 0 = the eye→target direction; positive turns clockwise seen from above."),
    viewName: z.string().optional().describe("Base name. With azimuthsDeg each view gets ' - {az}deg'."),
    viewTemplateId: z.number().int().optional(),
    detailLevel: z.enum(["coarse", "medium", "fine"]).optional(),
    dryRun: dryRunField,
  },
  fwdWrite("create_perspective_view"));

server.tool("revit_create_schedule", "Create a ViewSchedule for a category, optionally adding field columns.", {
  category: z.string().describe("BuiltInCategory, e.g. 'OST_Walls'."),
  name: z.string().optional(),
  fields: z.array(z.string()).optional().describe("Parameter names to add as schedule columns."),
  dryRun: dryRunField,
}, fwdWrite("create_schedule"));

server.tool("revit_tag_element", "Place a tag on an element in the active view.", {
  elementId: z.number().int(),
  location: xyz.optional(),
  addLeader: z.boolean().optional(),
  viewId: z.number().int().optional(),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("tag_element"));

server.tool("revit_create_text_note", "Create a TextNote in a view, optionally with a text type, alignment, rotation and leaders (material callouts in sections, facades and roof plans). Returns the regenerated extents (view right/up, metres) for overlap checks.", {
  text: z.string(),
  location: xyz,
  viewId: z.number().int().optional(),
  width: z.number().optional().describe("Wrap width ON PAPER in the input units (0.06 m = 60 mm at any view scale). Default 0.06 m."),
  typeId: z.number().int().optional().describe("TextNoteType id. Default: first text type."),
  typeName: z.string().optional().describe("TextNoteType name (alternative to typeId)."),
  align: z.enum(["left", "center", "right"]).optional(),
  rotationDegrees: z.number().optional().describe("Counter-clockwise rotation in the view plane."),
  leaders: z.array(z.object({ end: xyz, elbow: xyz.optional() })).optional()
    .describe("Leaders pointing at end; the attachment side follows the end point."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_text_note"));

// ═══════════════════════════════════════════════════════════════════════════
// ANNOTATION — TAGGING / DIMENSIONS
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_tag_all_in_view", "Tag all (untagged) elements of a category in a view — mirrors Revit's 'Tag All Not Tagged'. Returns tagged/skipped/failed counts.", {
  category: z.string().describe("Category display name, e.g. 'Doors', 'Windows', 'Rooms'."),
  viewId: z.number().int().optional().describe("Target view. Defaults to active view."),
  leader: z.boolean().optional().describe("Add leader line to each tag. Default false."),
  skipTagged: z.boolean().optional().describe("Skip elements that already have a tag. Default true."),
  tagTypeName: z.string().optional().describe("Rooms only: room tag type name (e.g. 'Nome do Ambiente e Área'). Rooms are tagged with RoomTag at their location point; category names follow the Revit UI language."),
  dryRun: dryRunField,
}, fwdWrite("tag_all_in_view"));

server.tool("revit_create_aligned_dimension", "Create an aligned dimension chain between two or more element references in a view. Supports Grids, Walls (centreline or face), ReferencePlanes, columns, beams, and FamilyInstances. Wall side: 'auto'/'centre' = wall centreline (default), 'exterior'/'interior' = outer/inner face, 'core' = core centre. The dimension line must cross all references and they must be visible in the target view. NOT usable in a 3D view: Revit never draws a dimension there, locked or not, though the API reports success. Returns value in FEET plus valueMetres.", {
  references: z.array(z.object({
    elementId: z.number().int().describe("ElementId of the grid, wall, column, beam, or other element to dimension to."),
    side: z.enum(["auto", "exterior", "interior", "centre", "core", "left", "right", "front", "back", "top", "bottom", "centerLeftRight", "centerFrontBack", "centerElevation", "face"]).optional()
      .describe("Walls: 'auto'/'centre' centreline, 'exterior'/'interior' faces, 'core'. Family instances (doors, windows): named references left/right (jambs), top/bottom (head/sill), front/back, center*. Any element: 'top'/'bottom' = highest/lowest horizontal face (slab levels in sections); 'face' + normal (+ near) = planar face with that normal. Levels always use their plane."),
    normal: xyz.optional().describe("side 'face': face normal direction (unitless)."),
    near: xyz.optional().describe("side 'face': choose the matching face nearest this point."),
  })).min(2).describe("At least 2 element references to dimension between."),
  line: z.object({
    start: xyz.describe("Start point of the dimension line. Must lie on the opposite side of the references from end."),
    end: xyz.describe("End point of the dimension line. The line must cross all references."),
  }).describe("Position and direction of the dimension witness line. Must be perpendicular to the elements being dimensioned and cross all references."),
  viewId: z.number().int().optional().describe("Target view. Defaults to active view. All references must be visible in this view."),
  typeId: z.number().int().optional().describe("DimensionType id applied to the new dimension."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_aligned_dimension"));

server.tool("revit_get_annotation_extents", "Report the 2D extents (view right/up, metres) of the dimensions, texts, tags and spot dimensions in a view, every overlapping pair, and annotations outside the crop, so documented plans, sections and facades can be checked before going on a sheet. Dimension boxes come from each segment's text position and the type's text size at the view scale. Optional modelIds are tested against the annotations too.", {
  viewId: z.number().int().optional(),
  modelIds: z.array(z.number().int()).optional().describe("Model elements whose view boxes must not be covered by annotations."),
  minOverlapM2: z.number().optional().describe("Ignore smaller overlaps (model m², default 0.0004)."),
}, fwd("get_annotation_extents"));

server.tool("revit_set_category_visibility", "Show or hide whole categories in views (Visibility/Graphics), e.g. section marks ('OST_Viewers', 'OST_Sections') or text notes hidden in plans by a detached office template. Categories by BuiltInCategory name or UI display name. Refuses views controlled by a view template.", {
  viewIds: z.array(z.number().int()).min(1),
  categories: z.array(z.string()).min(1).describe("BuiltInCategory names (OST_Sections) or display names in the UI language."),
  hidden: z.boolean(),
  dryRun: dryRunField,
}, fwdWrite("set_category_visibility"));

// revit_create_spot_elevation — hidden pending a reliable face-reference approach.
// The ReferenceIntersector raycast against a temporary 3D view returns no face hit
// for floors even at the bbox centre (doc.Regenerate() did not help); the prior
// solid-face approach hit "Spot Dimension does not lie on its reference". The C#
// command stays registered (HTTP-callable) for future work but is off the MCP surface.

// Spatial-QC pack — registered in C# (HTTP-callable via /mcp) but deliberately NOT exposed as MCP
// tools. They are consumed programmatically by AutomatedSpatialQC's Python (inputs like loops/points
// come from other calls), so surfacing them to LLM tool routing would only dilute the tool list.
// revit_spatial_get_room_boundary — hidden (HTTP-only spatial-QC pack; C# spatial_get_room_boundary)
// revit_spatial_clearance_envelope — hidden (HTTP-only spatial-QC pack; C# spatial_clearance_envelope)
// revit_spatial_clearance_envelope_batch — hidden (HTTP-only spatial-QC pack; C# spatial_clearance_envelope_batch)
// revit_spatial_raycast_headroom — hidden (HTTP-only spatial-QC pack; C# spatial_raycast_headroom)
// revit_spatial_get_walls — hidden (HTTP-only spatial-QC pack; C# spatial_get_walls)
// revit_spatial_get_stairs — hidden (HTTP-only spatial-QC pack; C# spatial_get_stairs)
// revit_spatial_create_model_line — hidden (HTTP-only spatial-QC pack; C# spatial_create_model_line)
// revit_spatial_get_paths_of_travel — hidden (HTTP-only spatial-QC pack; C# spatial_get_paths_of_travel)
// revit_spatial_create_path_of_travel — hidden (HTTP-only spatial-QC pack; C# spatial_create_path_of_travel)

server.tool("revit_get_tags_in_view", "List all IndependentTag elements in a view. Optionally filter by tagged element category.", {
  viewId: z.number().int().optional().describe("Target view. Defaults to active view."),
  category: z.string().optional().describe("Filter by tagged element category name, e.g. 'Doors'."),
}, fwd("get_tags_in_view"));

server.tool("revit_get_schedule_data",
  "Read the rendered cell data of a ViewSchedule (text exactly as shown — calculated fields, units, " +
  "formatting applied). The first row is normally the column headers. Paginated by row: returns totalRows, " +
  "totalColumns, hasMore, nextOffset; page large schedules with offset.", {
  scheduleId: z.number().int().describe("ElementId of the ViewSchedule (from revit_get_views)."),
  limit: z.number().int().min(1).max(1000).optional().describe("Rows per page. Default 100."),
  offset: z.number().int().min(0).optional().describe("First body row to return. Default 0. Use nextOffset from the previous page."),
}, fwd("get_schedule_data"));

server.tool("revit_get_doors",
  "All placed doors with nominal width (m), plan location (world XY, m), level, and door swing geometry: " +
  "facingX/Y (FacingOrientation — the normal / pull-swing side), handX/Y (HandOrientation — along the wall), " +
  "and facingFlipped/handFlipped. Orientation is geometry, not a parameter, so find_elements cannot return it — " +
  "use this for ADA/egress maneuvering-clearance and door-swing checks. " +
  "For door PARAMETERS (FireRating, Mark, type name, custom Width param) use find_elements(category='Doors'); " +
  "for which rooms a door connects use get_element_rooms; for a bare id list use list_elements(category='OST_Doors').",
  {},
  fwd("get_doors"));

server.tool("revit_recipe_plan_walls_from_cad",
  "Propose walls from a linked DWG plan without modifying the model: reads curves of the given layers (revit_inspect_cad), pairs parallel face lines at wall distance, returns centerline walls (start/end in meters, thickness), openings on bridged gaps (door when a swing arc or door block is nearby), thickness histogram and the longest unpaired lines to review. Review before creating walls (revit_create_wall_type_variant + revit_create_wall with locationLine centerline).", {
    importInstanceId: z.number().int(), layers: z.array(z.string()).min(1).describe("Wall layers (from inspect_cad summary)."),
    evidenceLayers: z.array(z.string()).optional().describe("Door/window layers or blocks used as opening evidence only."),
    bboxMeters: z.object({ minX: z.number(), minY: z.number(), maxX: z.number(), maxY: z.number() }).optional(),
    minThickness: z.number().positive().optional(), maxThickness: z.number().positive().optional(),
    angleTolDeg: z.number().positive().optional(), minOverlap: z.number().positive().optional(),
    minOpening: z.number().positive().optional(), maxOpening: z.number().positive().optional(),
    snapThicknesses: z.array(z.number().positive()).optional(), snapTolerance: z.number().nonnegative().optional(), includeBlocks: z.boolean().optional(),
    minWallLength: z.number().nonnegative().optional().describe("Shorter runs are returned as shortPieces (often columns). Default 0.5."),
    excludeRegions: z.array(z.object({ minX: z.number(), minY: z.number(), maxX: z.number(), maxY: z.number() })).optional()
      .describe("Drop walls whose midpoint falls inside (parking outlines, furniture drawn with double lines)."),
    singleLineRegions: z.array(z.object({ minX: z.number(), minY: z.number(), maxX: z.number(), maxY: z.number() })).optional()
      .describe("Also return partitions drawn with a single line inside these regions (trimmed where they overlap planned walls or a twin line)."),
    doorLayers: z.array(z.string()).optional().describe("Also return doors from quarter swing arcs on these layers, hosted on the planned walls."),
    documentTitle: z.string().min(1).optional(),
  }, async ({ importInstanceId, layers, evidenceLayers, bboxMeters, documentTitle, singleLineRegions, doorLayers, ...options }) => {
    const fetchAll = async (layerList: string[]) => {
      const curves: CadCurve[] = [];
      for (let offset = 0; ; ) {
        const env = await callRevit("inspect_cad", { importInstanceId, mode: "curves", layers: layerList, bboxMeters, offset, limit: 10000, ...(documentTitle ? { documentTitle } : {}) });
        if (!env.ok) return env;
        const data = env.data as { curves: CadCurve[]; hasMore: boolean; nextOffset: number };
        curves.push(...data.curves);
        if (!data.hasMore) return curves;
        offset = data.nextOffset;
      }
    };
    const walls = await fetchAll(layers);
    if (!Array.isArray(walls)) return envelopeToToolResult(walls);
    let evidence: CadCurve[] = [];
    if (evidenceLayers?.length) {
      const ev = await fetchAll(evidenceLayers);
      if (!Array.isArray(ev)) return envelopeToToolResult(ev);
      evidence = ev.map((c) => ({ ...c, b: c.b ?? `layer:${c.l}` }));
    }
    const plan = planWallsFromCad([...walls, ...evidence], singleLineRegions ? { ...options, unpairedLimit: 100000 } : options);
    const singleLines = singleLineRegions ? planSingleLineWalls(plan, { regions: singleLineRegions }) : undefined;
    let doors: ReturnType<typeof planDoorsFromCad> | undefined;
    if (doorLayers?.length) {
      const arcs = await fetchAll(doorLayers);
      if (!Array.isArray(arcs)) return envelopeToToolResult(arcs);
      const hosts = [...plan.walls, ...(singleLines ?? []).map((w) => ({ ...w, thickness: 0.1 }))];
      doors = planDoorsFromCad(arcs, hosts);
    }
    if (singleLineRegions) plan.unpaired = plan.unpaired.slice(0, 50);
    return envelopeToToolResult({ ok: true, data: { importInstanceId, layers, evidenceLayers, ...plan, singleLines, doors,
      note: "Proposal only. Coordinates are model meters (plan). Check the unpaired list and thickness histogram against the drawing before creating walls." } });
  });

server.tool("revit_recipe_plan_walls_from_point_cloud",
  "Propose walls from a horizontal slice of a linked point cloud without modifying the model: samples points between sliceMinZ and sliceMaxZ (meters, e.g. level + 1.10..1.30), extracts wall faces by RANSAC, detects a cloud duplicated with an offset (every face twice at one translation) and keeps one copy, then pairs faces into centerline walls with thickness. Review before creating walls.", {
    instanceId: z.number().int(), sliceMinZ: z.number(), sliceMaxZ: z.number(),
    boxMeters: z.object({ minX: z.number(), minY: z.number(), maxX: z.number(), maxY: z.number() }).optional(),
    averageDistanceMeters: z.number().positive().optional(), maxPoints: z.number().int().positive().optional(),
    cell: z.number().positive().optional(), inlierTol: z.number().positive().optional(), minSegmentLength: z.number().positive().optional(),
    keepDuplicate: z.boolean().optional().describe("Do not drop the shifted copy even if one is detected."),
    minThickness: z.number().positive().optional(), maxThickness: z.number().positive().optional(), snapThicknesses: z.array(z.number().positive()).optional(),
    snapTolerance: z.number().nonnegative().optional(), minWallLength: z.number().nonnegative().optional(),
    documentTitle: z.string().min(1).optional(),
  }, async ({ instanceId, sliceMinZ, sliceMaxZ, boxMeters, averageDistanceMeters, maxPoints, cell, inlierTol, minSegmentLength, keepDuplicate, documentTitle, ...wallOptions }) => {
    const outFile = join(tmpdir(), "RevitMCP", "pointcloud", `slice-${instanceId}-${Date.now()}.xyz`);
    const env = await callRevit("sample_point_cloud", { instanceId, outFile, averageDistanceMeters: averageDistanceMeters ?? 0.01, maxPoints: maxPoints ?? 2000000,
      boxMeters: { ...(boxMeters ?? {}), minZ: sliceMinZ, maxZ: sliceMaxZ }, ...(documentTitle ? { documentTitle } : {}) });
    if (!env.ok) return envelopeToToolResult(env);
    const pts = (await readXyz(outFile)).map(([x, y]) => [x, y] as [number, number]);
    let segments = extractSegments(pts, { cell, inlierTol, minLength: minSegmentLength });
    const duplicate = detectDuplicateShift(segments);
    if (duplicate.duplicated && duplicate.shift && !keepDuplicate) segments = dropShiftedCopy(segments, duplicate.shift);
    // scanned faces: thinner/thicker walls than traced CAD, and noisier thickness
    const options = { ...wallOptions, minThickness: wallOptions.minThickness ?? 0.05, maxThickness: wallOptions.maxThickness ?? 0.6, snapTolerance: wallOptions.snapTolerance ?? 0.02 };
    const plan = planWallsFromCad(segmentsToCurves(segments), options);
    return envelopeToToolResult({ ok: true, data: { instanceId, slice: [sliceMinZ, sliceMaxZ], pointsRead: (env.data as { count: number }).count,
      segments: segments.length, duplicate, ...plan, sampleFile: outFile,
      note: "Proposal only. One-sided walls (only one face scanned) show up in unpaired; windows and doors leave gaps in the faces." } });
  });

server.tool("revit_recipe_convert_scans",
  "Convert raw scans (.e57, .las, .laz, .pts, .ptx, .fls, .zfs, ...) into a ReCap project with ReCap's command-line importer decap.exe (uses the local ReCap license), then link it with revit_link_point_cloud. unify merges scans into one; decimationMm thins points; inputUnit for unitless formats.", {
    inputs: z.array(z.string()).min(1), outputFolder: z.string(), projectName: z.string().min(1),
    unify: z.boolean().optional(), decimationMm: z.number().positive().optional(),
    inputUnit: z.enum(["mm", "cm", "m", "in", "ft", "usft"]).optional(), minRange: z.number().optional(), maxRange: z.number().optional(),
    decapPath: z.string().optional(),
  }, async ({ inputs, outputFolder, projectName, unify, decimationMm, inputUnit, minRange, maxRange, decapPath }) => {
    const exe = decapPath ?? "C:\\Program Files\\Autodesk\\Autodesk ReCap\\decap.exe";
    const args = ["--importWithLicense", outputFolder, projectName];
    if (unify) args.push("--unify");
    if (decimationMm) args.push("--decimation", String(decimationMm));
    if (inputUnit) args.push("--inputUnitType", String(["mm", "cm", "m", "in", "ft", "usft"].indexOf(inputUnit)));
    if (minRange !== undefined) args.push("--minRangeClipping", String(minRange));
    if (maxRange !== undefined) args.push("--maxRangeClipping", String(maxRange));
    args.push(...inputs);
    try {
      const { stdout, stderr } = await execFileAsync(exe, args, { timeout: 6 * 60 * 60 * 1000, maxBuffer: 64 * 1024 * 1024, windowsHide: true });
      const rcp = join(outputFolder, `${projectName}.rcp`);
      return envelopeToToolResult({ ok: true, data: { rcp, exists: existsSync(rcp), args, stdout: stdout.slice(-4000), stderr: stderr.slice(-2000) } });
    } catch (error) {
      const e = error as { message?: string; stdout?: string; stderr?: string };
      return envelopeToToolResult({ ok: false, error: { code: "decap_failed", message: `${e.message ?? String(error)}\n${(e.stdout ?? "").slice(-2000)}\n${(e.stderr ?? "").slice(-1000)}` } });
    }
  });

server.tool("revit_recipe_search_family_library",
  "Search local RFA filenames in the configured library, without opening Revit. All search words must match; accents and case are ignored. Use a returned absolute path with revit_load_family, then inspect loaded types and placementType. Filenames do not verify category, dimensions, hosting or suitability.", {
    query: z.string().optional(),
    offset: z.number().int().min(0).optional(),
    limit: z.number().int().min(1).max(200).optional(),
  }, async ({ query, offset, limit }) => {
    try {
      return envelopeToToolResult({ ok: true, data: await searchFamilyLibrary(query, offset, limit) });
    } catch (error) {
      return envelopeToToolResult({ ok: false, error: { code: "library_read_failed",
        message: error instanceof Error ? error.message : String(error) } });
    }
  });

server.tool("revit_load_family",
  "Load a family (.rfa) from disk into the project. Returns the family id and its types.", {
  filePath: z.string().describe("Absolute path to a .rfa file."),
  overwrite: z.boolean().optional().describe("Overwrite the family (and its parameter values) if already loaded. Default true."),
}, fwdWrite("load_family"));

server.tool("revit_duplicate_family_type",
  "Duplicate any element type (family symbol or system type such as fascia/gutter, roof, wall) under a new name. Set parameters afterwards with revit_set_parameter using the returned typeId (e.g. a fascia type's Perfil {id}).", {
  sourceTypeId: z.number().int().describe("ElementId of the element type to copy."),
  newName: z.string().describe("Name for the new type (unique within the family)."),
}, fwdWrite("duplicate_family_type"));

server.tool("revit_create_detail_line",
  "Create a view-specific detail line in a 2D view (plan/section/elevation/drafting/detail). Endpoints are projected onto the view plane.", {
  start: xyz.describe("Start point."),
  end: xyz.describe("End point."),
  viewId: z.number().int().optional().describe("Target 2D view. Defaults to active view."),
  units: unitsField,
  color: rgbSchema.optional().describe("Projection line color override. Omit to use the view's default line style."),
  weight: z.number().int().min(1).max(16).optional().describe("Projection line weight override (Revit pen number 1-16). Independent of color."),
  lineStyle: z.string().optional().describe("Line style name (e.g. a dashed projection style). Unknown names fail listing the valid styles."),
  dryRun: dryRunField,
}, fwdWrite("create_detail_line"));

server.tool("revit_create_filled_region",
  "Create a filled region (2D annotation) from a closed boundary in a 2D view. Points are projected onto the view plane and the loop is closed automatically.", {
  boundary: z.array(z.object({ x: z.number(), y: z.number(), z: z.number().optional() })).min(3)
    .describe("Boundary points in order (at least 3)."),
  filledRegionTypeId: z.number().int().optional().describe("FilledRegionType id. Defaults to the first available."),
  viewId: z.number().int().optional().describe("Target 2D view. Defaults to active view."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("create_filled_region"));

// ═══════════════════════════════════════════════════════════════════════════
// EDIT — PARAMETERS
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_set_parameter", "Set a parameter on an element. parameterName may also be a BuiltInParameter name (e.g. SPACING_LAYOUT_VERT) when a localized name repeats; ambiguous writable names are refused with the candidates. Returns before/after diff. For numeric (Double) parameters with measurable units pass the matching unit string for automatic conversion; default 'internal' means raw Revit feet.", {
  id: z.number().int(),
  parameterName: z.string(),
  value: z.union([z.string(), z.number(), z.boolean(), z.object({ id: z.number().int() })]),
  units: z.enum(["meters", "feet", "square_meters", "square_feet", "cubic_meters", "cubic_feet", "internal"]).optional()
    .describe("Unit for numeric double parameters. Length: 'meters'/'feet'. Area: 'square_meters'/'square_feet'. Volume: 'cubic_meters'/'cubic_feet'. Default 'internal' (raw Revit feet)."),
  dryRun: dryRunField,
}, fwdWrite("set_parameter"));

server.tool("revit_set_parameter_batch", "Set the same parameter on multiple elements. Returns changeSummary + partialFailure flag. Set atomic:true for all-or-nothing (any failure rolls back the whole call); default is best-effort.", {
  ids: idsField,
  parameterName: z.string(),
  value: z.union([z.string(), z.number(), z.boolean(), z.object({ id: z.number().int() })]),
  units: z.enum(["meters", "feet", "square_meters", "square_feet", "cubic_meters", "cubic_feet", "internal"]).optional()
    .describe("Unit for numeric double parameters. Length: 'meters'/'feet'. Area: 'square_meters'/'square_feet'. Volume: 'cubic_meters'/'cubic_feet'. Default 'internal'."),
  atomic: z.boolean().optional().describe("All-or-nothing. If true, any element failure rolls back the entire batch. Default false (best-effort)."),
  dryRun: dryRunField,
}, fwdWrite("set_parameter_batch"));

server.tool("revit_update_where",
  "Set one parameter on EVERY element matching where-conditions, then re-read each written value to confirm it " +
  "actually took. Selection is by condition, not by id list, so no find-then-write round trip. atomic defaults to " +
  "TRUE: if any element fails read-back verification the whole call rolls back. Use dryRun to preview matched " +
  "count and before/after values without touching the model.", {
  category: z.string().describe("BuiltInCategory name, e.g. 'OST_Doors'."),
  where: whereField,
  set: z.object({
    parameter: z.string().describe("Parameter to write."),
    value: z.union([z.string(), z.number(), z.boolean()]),
    units: z.enum(["meters", "feet", "square_meters", "square_feet", "cubic_meters", "cubic_feet", "internal"]).optional()
      .describe("Unit for numeric double parameters. Default 'internal'."),
    scope: z.enum(["auto", "instance", "type"]).optional()
      .describe("Write on the instance or on the element's Type. Default 'auto'."),
  }).describe("The write applied to every matched element."),
  atomic: z.boolean().optional()
    .describe("All-or-nothing. Default TRUE — any failed verification rolls the whole call back."),
  dryRun: dryRunField,
}, fwdWrite("update_where"));

server.tool("revit_import_parameters",
  "Spreadsheet-shaped import: each row sets ONE parameter on ONE element, so different rows may target different " +
  "elements AND different parameters. (Use revit_set_parameter_batch instead when writing the SAME parameter to many " +
  "elements.) Every row runs inside one Revit transaction, so the whole import is a single undo step.", {
  items: z.array(z.object({
    elementId: z.number().int(),
    parameterName: z.string(),
    value: z.union([z.string(), z.number(), z.boolean()]),
    units: z.enum(["meters", "feet", "square_meters", "square_feet", "cubic_meters", "cubic_feet", "internal"]).optional()
      .describe("Unit for numeric double parameters. Default 'internal'."),
  })).min(1).describe("One entry per (element, parameter, value) — typically one spreadsheet row each."),
  dryRun: dryRunField,
}, fwdWrite("import_parameters"));

server.tool("revit_rename_element", "Rename an element — supports Family, FamilySymbol (type name), and any other element. For Family/FamilySymbol uses direct property setter; validates system families, illegal chars, and name collisions. Returns before/after diff with instancesAffected count.", {
  id: z.number().int(),
  name: z.string(),
  dryRun: dryRunField,
}, fwdWrite("rename_element"));

// ═══════════════════════════════════════════════════════════════════════════
// EDIT — TRANSFORM
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_move_element", "Translate an element by a vector. Returns before/after position.", {
  id: z.number().int(),
  translation: xyz,
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("move_element"));

server.tool("revit_rotate_element", "Rotate an element around a vertical axis.", {
  id: z.number().int(),
  center: xyz,
  angleDeg: z.number().describe("Rotation in degrees, counter-clockwise."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("rotate_element"));

server.tool("revit_copy_element", "Copy elements by a translation vector.", {
  ids: idsField,
  translation: xyz,
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("copy_element"));

server.tool("revit_mirror_element", "Mirror elements across a plane.", {
  ids: idsField,
  origin: xyz,
  normal: xyz.describe("Normal of the mirror plane."),
  copy: z.boolean().optional().describe("Default true = copy+mirror; false = move."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("mirror_element"));

server.tool("revit_array_linear", "Copy elements N times along a vector (linear array).", {
  ids: idsField,
  count: z.number().int().min(1),
  spacing: xyz.describe("Translation per copy."),
  units: unitsField,
  dryRun: dryRunField,
}, fwdWrite("array_linear"));

server.tool("revit_delete_elements", "Delete elements by id. Returns changeSummary.", {
  ids: idsField,
  dryRun: dryRunField,
}, fwdWrite("delete_elements"));

// ═══════════════════════════════════════════════════════════════════════════
// EDIT — GROUPING
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_group_elements", "Group elements together.", {
  ids: idsField,
  name: z.string().optional().describe("Group type name."),
  dryRun: dryRunField,
}, fwdWrite("group_elements"));

server.tool("revit_ungroup_elements", "Ungroup a Group, returning its member ids.", {
  groupId: z.number().int(),
  dryRun: dryRunField,
}, fwdWrite("ungroup_elements"));

// ═══════════════════════════════════════════════════════════════════════════
// VIEW MANIPULATION
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_open_view", "Switch the active view.", {
  viewId: z.number().int(),
  dryRun: dryRunField,
}, fwdWrite("open_view"));

server.tool("revit_fit_datums_to_crop", "Trim the levels and grids visible in a view (section, elevation, plan) to its active crop region using view-specific 2D extents, so viewports on sheets are not stretched by datum lines running far beyond the drawing. insetMeters keeps the ends inside the crop (default 0.3).", {
  viewId: z.number().int().optional(),
  insetMeters: z.number().finite().nonnegative().optional(),
  dryRun: dryRunField,
}, fwdWrite("fit_datums_to_crop"));

server.tool("revit_set_view_detail_level", "Set detail level: Coarse, Medium, or Fine.", {
  viewId: z.number().int().optional(),
  detailLevel: z.enum(["Coarse", "Medium", "Fine"]),
  dryRun: dryRunField,
}, fwdWrite("set_view_detail_level"));

server.tool("revit_hide_elements_in_view", "Hide elements in a view.", {
  ids: idsField,
  viewId: z.number().int().optional(),
  dryRun: dryRunField,
}, fwdWrite("hide_elements_in_view"));

server.tool("revit_unhide_elements_in_view", "Unhide elements in a view.", {
  ids: idsField,
  viewId: z.number().int().optional(),
  dryRun: dryRunField,
}, fwdWrite("unhide_elements_in_view"));

server.tool("revit_isolate_elements_in_view",
  "Temporarily isolate HOST elements in a view (cyan 'Isolate Element' mode). Pass reset=true to clear. " +
  "NOTE: host-document elements only — cannot keep individual linked-file elements (isolating host hides the whole link). " +
  "For a region spanning host + linked geometry, use revit_set_section_box instead.",
  {
    ids: idsField.optional(),
    viewId: z.number().int().optional(),
    reset: z.boolean().optional().describe("Clear the temporary hide/isolate state instead of isolating. Default false."),
  },
  fwd("isolate_elements_in_view"));

server.tool("revit_duplicate_view",
  "Duplicate a view (preserving its template, filters, and overrides). Returns the new view id. " +
  "Use to make a throwaway inspection copy of an already-filtered view before section-boxing it.",
  {
    viewId: z.number().int().describe("Source view to duplicate."),
    duplicateOption: z.enum(["Duplicate", "WithDetailing", "AsDependent"]).optional().describe("Duplicate mode. Default 'Duplicate'."),
    newName: z.string().optional().describe("Rename the new view."),
    dryRun: dryRunField,
  },
  fwdWrite("duplicate_view"));

server.tool("revit_set_section_box",
  "Set or clear the section box of a 3D view, cropping it to a bounding region. " +
  "Crops ALL geometry including linked files — the reliable way to isolate a clearance region spanning host + linked MEP. " +
  "Provide min/max corners (default units 'feet', matching get_linked_elements bboxes); enable=false deactivates the box.",
  {
    viewId: z.number().int().optional().describe("3D view id. Defaults to active view; must be a View3D."),
    min: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe("Lower corner {x,y,z}. Required unless enable=false."),
    max: z.object({ x: z.number(), y: z.number(), z: z.number() }).optional().describe("Upper corner {x,y,z}. Required unless enable=false."),
    units: z.enum(["feet", "mm", "meters"]).optional().describe("Units of min/max. Default 'feet'."),
    paddingMm: z.number().optional().describe("Expand the box outward on all sides, in mm. Default 0."),
    enable: z.boolean().optional().describe("false deactivates the section box. Default true."),
    dryRun: dryRunField,
  },
  fwdWrite("set_section_box"));

server.tool("revit_select_elements", "Set the selection in Revit UI.", {
  ids: idsField,
}, fwdWrite("select_elements"));

server.tool("revit_zoom_to_elements", "Zoom/pan the active view to show elements.", {
  ids: idsField,
}, fwdWrite("zoom_to_elements"));

server.tool("revit_apply_view_filter", "Create a parameter filter rule and apply to a view with optional color override.", {
  viewId: z.number().int().optional(),
  filterName: z.string(),
  category: z.string(),
  parameterName: z.string(),
  value: z.string().describe("Equality match value."),
  colorRGB: rgbSchema.optional(),
  visible: z.boolean().optional().describe("False = hide matching elements."),
  reuseExisting: z.boolean().optional().describe("Reuse filter if a filter with this name already exists. Default false."),
  dryRun: dryRunField,
}, fwdWrite("apply_view_filter"));

server.tool("revit_color_override_by_param", "Color-code elements in a view by a parameter value.", {
  viewId: z.number().int().optional(),
  category: z.string(),
  parameterName: z.string(),
  colorMap: z.record(rgbSchema)
    .describe('Map of parameter value → RGB color. E.g. {"Fire Rated": {r:255,g:0,b:0}}'),
  dryRun: dryRunField,
}, fwdWrite("color_override_by_param"));

// ═══════════════════════════════════════════════════════════════════════════
// COORDINATION / CLASH DETECTION
// ═══════════════════════════════════════════════════════════════════════════

const elementSetSchema = z.object({
  source: z.enum(["host", "link"]).default("host").describe("'host' = current model, 'link' = a linked RVT file."),
  linkId: z.number().int().optional().describe("Required when source='link'. ElementId of the RevitLinkInstance."),
  categories: z.array(z.string()).optional().describe("BuiltInCategory names to include, e.g. ['OST_DuctCurves', 'OST_PipeCurves']. Empty = all element types."),
  limit: z.number().int().min(1).max(2000).optional().describe("Max elements to load from this set. Default 500."),
  scopeId: z.number().int().optional().describe("ElementId of any spatial container — MEP Space (revit_list_spaces), Room (revit_list_rooms), Floor, equipment, or any element with a bounding box. Only elements whose centroid falls inside that element's bbox are included. Omit to check all elements of the category in the model."),
});
server.tool("revit_override_element_graphics",
  "Apply per-element color and fill overrides to specific elements in a view. " +
  "Use to highlight violations (e.g. red for clashing furniture, gray for doors). " +
  "Pass reset=true to clear overrides. Works on any element type in any view.",
  {
    viewId: z.number().int().describe("ElementId of the view to override in."),
    elementIds: z.array(z.number().int()).min(1).describe("Element IDs to override."),
    color: z.object({ r: z.number().int().min(0).max(255), g: z.number().int().min(0).max(255), b: z.number().int().min(0).max(255) }).optional().describe("RGB color. Default red (255,0,0)."),
    transparency: z.number().int().min(0).max(100).optional().describe("Surface transparency 0–100%. Default 0."),
    reset: z.boolean().optional().describe("If true, clear all overrides for the given elements. Default false."),
  },
  fwdWrite("override_element_graphics"));

// Separate object reference for setB to prevent MCP SDK JSON Schema deduplication (setB would render as {} otherwise).
const elementSetSchemaB = elementSetSchema.extend({});

server.tool("revit_check_clearance",
  "Detect hard clashes or clearance violations between two element sets (host or linked files). " +
  "axis='bbox' (default): inflates setA bounding boxes by clearanceMm in all directions — use for omnidirectional proximity checks (e.g. 1m clear zone around a door). " +
  "axis='Z': fires a vertical ReferenceIntersector raycast from each setA element and automatically finds the nearest setB element directly below or above it; " +
  "reports the actual measured clearance — use direction='below' (duct→floor below) or 'above' (duct→ceiling above). " +
  "IMPORTANT for axis='Z': do NOT manually fetch or enumerate setB elements first — just pass a category filter in setB and let the raycast locate the nearest hit automatically. Fetching all setB elements and comparing manually is unnecessary, slow, and wrong. " +
  "clearanceMm=0 + axis='bbox' → hard clash only. " +
  "Example: setA = HVAC ducts (host), setB = floors in Arch link, axis='Z', direction='below', clearanceMm=2400.",
  {
    setA: elementSetSchema,
    setB: elementSetSchemaB,
    axis: z.enum(["bbox", "Z"]).optional().describe("Clearance axis. 'bbox'=inflate AABB (default). 'Z'=vertical raycast (accurate, requires a 3D view)."),
    direction: z.enum(["below", "above"]).optional().describe("Ray direction for axis=Z. 'below'=cast downward from setA bottom (MEP→floor). 'above'=cast upward from setA top (MEP→ceiling). Default 'below'."),
    viewId: z.number().int().optional().describe("ElementId of a 3D view for axis=Z raycast. Uses active view if omitted."),
    clearanceMm: z.number().min(0).optional().describe("Clearance threshold in mm. Violations reported when measured distance < clearanceMm. Default 0."),
    sampleCount: z.number().int().min(1).max(10).optional().describe("axis=Z only: number of points sampled along each element's centreline (LocationCurve). Default 3 (start/mid/end). Increase to 5 for long sloped elements spanning multiple floor slabs."),
    maxResults: z.number().int().min(1).max(2000).optional().describe("Clash pairs cap. Default 200."),
  },
  fwd("check_clearance"));

server.tool("revit_get_view_image",
  "Export a Revit view to PNG and return it as an image. Omit viewId to capture the active view. " +
  "Useful for visual coordination review — capture a section or 3D view showing potential clashes. " +
  "Returns width/height alongside the image.",
  {
    viewId: z.number().int().optional().describe("ElementId of the view to export. Omit for active view."),
    pixelSize: z.number().int().min(128).max(4096).optional()
      .describe("Image WIDTH in pixels; height follows the view's aspect ratio. Default 512. This is the parameter that changes resolution."),
    dpi: z.number().int().min(36).max(300).optional()
      .describe("Print DPI metadata only (snaps to 72/150/300, default 72) — it does NOT change the pixel dimensions. Use pixelSize for that."),
  },
  async (params) => {
    const envelope = await callRevit("get_view_image", params);
    if (!envelope.ok) return envelopeToToolResult(envelope);

    const data = envelope.data as Record<string, unknown> | undefined;
    const base64 = data?.imageBase64 as string | undefined;
    if (base64) {
      const meta = { ...data };
      delete meta.imageBase64;
      return {
        content: [
          { type: "image" as const, data: base64, mimeType: "image/png" as const },
          { type: "text" as const, text: JSON.stringify({ ok: true, data: meta }, null, 2) },
        ],
      };
    }
    return envelopeToToolResult(envelope);
  });

// ═══════════════════════════════════════════════════════════════════════════
// EDIT — TYPE / TEMPLATE / PARAMETER COPY
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_change_element_type",
  "Swap the type of an element (e.g. change a wall from 'Generic - 200mm' to 'Generic - 300mm', " +
  "or change a door family symbol). Uses revit_list_family_types / revit_list_wall_types to find valid typeId values. " +
  "Returns old and new type info.",
  {
    id: z.number().int().describe("ElementId of the element to change."),
    typeId: z.number().int().describe("ElementId of the target type (WallType, FloorType, FamilySymbol, …)."),
    dryRun: dryRunField,
  },
  fwdWrite("change_element_type"));

server.tool("revit_apply_view_template",
  "Apply or remove a view template from a view. " +
  "Provide templateId (ElementId) or templateName; omit both (or pass templateId=-1) to remove the current template. " +
  "Use revit_list_view_templates to discover available templates.",
  {
    viewId: z.number().int().describe("ElementId of the view to modify."),
    templateId: z.number().int().optional().describe("ElementId of the view template. Pass -1 to remove."),
    templateName: z.string().optional().describe("Template name (case-insensitive). Alternative to templateId."),
    dryRun: dryRunField,
  },
  fwdWrite("apply_view_template"));

server.tool("revit_copy_parameters",
  "Copy parameter values from a source element to one or more target elements. " +
  "Only writable parameters with matching storage types on both elements are copied. " +
  "Useful for stamping Mark, Comments, or custom instance parameters across many elements at once.",
  {
    sourceId: z.number().int().describe("ElementId of the source element."),
    targetIds: idsField.describe("ElementIds of target elements."),
    parameterNames: z.array(z.string()).optional()
      .describe("Parameter names to copy. Omit to copy all writable parameters found on both elements."),
    dryRun: dryRunField,
  },
  fwdWrite("copy_parameters"));

// ═══════════════════════════════════════════════════════════════════════════
// ROOM CONTAINMENT
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_get_element_rooms",
  "Get room containment for one or more family instances (doors, windows, furniture, fixtures, etc.). " +
  "Returns room / fromRoom / toRoom — each { id, name, number } or null. " +
  "Uses the Revit API's phase-dependent FamilyInstance.get_Room/get_FromRoom/get_ToRoom, " +
  "NOT centroid-in-bbox — authoritative for wall-hosted elements (doors/windows) whose centroid " +
  "lies inside the wall between two rooms. " +
  "fromRoom + toRoom apply to boundary connectors (OST_Doors, OST_Windows). " +
  "room applies to point-located elements (OST_Furniture, OST_PlumbingFixtures, OST_LightingFixtures, etc.). " +
  "Non-family-instance elements return null for all fields. " +
  "Use revit_list_rooms to discover room ids/names in the model.",
  {
    ids: idsField.describe("ElementIds to query. 1–N elements per call."),
  },
  fwd("get_element_rooms"));

// ═══════════════════════════════════════════════════════════════════════════
// EDIT — SCHEDULE / LEVEL / PDF EXPORT
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_configure_schedule",
  "Add filters, sort fields, and grouping to an existing ViewSchedule, and optionally export it to CSV. " +
  "Use revit_create_schedule to create the schedule first, then call this to configure it. " +
  "Use revit_list_sheets or revit_get_views to find schedule ids.",
  {
    scheduleId: z.number().int().describe("ElementId of the ViewSchedule."),
    isItemized: z.boolean().optional().describe("False combines instances with equal sort keys. Include width/height/peitoril in sort keys when dimensions vary."),
    showTitle: z.boolean().optional(),
    showHeaders: z.boolean().optional(),
    bodyTextTypeId: z.number().int().optional(),
    headerTextTypeId: z.number().int().optional(),
    titleTextTypeId: z.number().int().optional(),
    columns: z.array(z.object({
      field: z.string().describe("Exact existing schedule field name, not its renamed heading."),
      heading: z.string().optional(),
      widthMillimeters: z.number().min(5).max(1000).optional().describe("Column width on paper."),
      hidden: z.boolean().optional(),
      align: z.enum(["left", "center", "right"]).optional(),
      lengthUnit: z.enum(["meters", "centimeters", "millimeters"]).optional(),
      accuracy: z.number().positive().optional().describe("Rounding precision in lengthUnit; requires lengthUnit."),
    })).optional().describe("Configure native columns; unknown or ambiguous fields roll back the command."),
    clearFilters: z.boolean().optional().describe("Remove all existing filters before adding new ones. Default false."),
    clearSortFields: z.boolean().optional().describe("Remove all existing sort/group fields before adding new ones. Default false."),
    filters: z.array(z.object({
      field: z.string().describe("Schedule field name (e.g. 'Area', 'Level', 'Mark')."),
      operator: z.enum([
        "equals", "not_equals", "greater", "greater_equal", "less", "less_equal",
        "contains", "not_contains", "begins_with", "ends_with", "has_value", "has_no_value",
      ]).optional().describe("Filter operator. Default 'equals'."),
      value: z.union([z.string(), z.number()]).optional().describe(
        "Filter value (not used for has_value / has_no_value). Numeric fields take a number " +
        "in Revit internal units (feet for length) — 900 mm is 2.9527559055118114. " +
        "A numeric string is accepted too and parsed with invariant culture."),
    })).optional().describe("Filters to add to the schedule."),
    sortFields: z.array(z.object({
      field: z.string().describe("Schedule field name to sort/group by."),
      ascending: z.boolean().optional().describe("Sort ascending. Default true."),
      groupBy: z.boolean().optional().describe("Add a group header row for each distinct value. Default false."),
    })).optional().describe("Sort and group fields to add."),
    exportCsv: z.boolean().optional().describe("Export the schedule to CSV and include the content in the response."),
    dryRun: dryRunField,
  },
  fwdWrite("configure_schedule"));

server.tool("revit_set_level_elevation",
  "Change the elevation of a Level element. All hosted elements (floors, ceilings, MEP) move with it. " +
  "Use revit_list_levels to find level ids.",
  {
    id: z.number().int().describe("ElementId of the Level."),
    elevation: z.number().describe("New elevation value."),
    units: z.enum(["meters", "feet", "mm", "internal"]).optional()
      .describe("Units for the elevation value. Default 'meters'. 'internal' = Revit internal feet."),
    dryRun: dryRunField,
  },
  fwdWrite("set_level_elevation"));

server.tool("revit_export_view_pdf",
  "Export a view or sheet to a PDF file on disk. " +
  "Returns the output file path and size. Use revit_get_views or revit_list_sheets to find view ids.",
  {
    viewId: z.number().int().optional().describe("ElementId of the view or sheet. Defaults to the active view."),
    outputFolder: z.string().optional().describe("Folder path for the PDF. Defaults to Documents folder."),
    fileName: z.string().optional().describe("File name without extension. Defaults to view name + timestamp."),
    rasterQuality: z.enum(["Low", "Medium", "High", "Presentation"]).optional()
      .describe("Raster image quality. Default 'Medium'."),
    colorMode: z.enum(["Color", "Grayscale", "BlackLine"]).optional()
      .describe("Color mode. Default 'Color'."),
  },
  fwd("export_view_pdf"));

// ═══════════════════════════════════════════════════════════════════════════
// BATCH
// ═══════════════════════════════════════════════════════════════════════════

// ── Reinforcement (rebar) ───────────────────────────────────────────────────
const barSpec = z.object({
  diameterMm: z.number().positive().optional().describe("Nominal diameter in mm (NBR 7480), e.g. 10 or 12.5."),
  grade: z.enum(["CA-50", "CA-60", "CA-25"]).optional().describe("Default CA-50."),
  barTypeName: z.string().optional().describe("Exact RebarBarType name; wins over diameterMm/grade."),
  spacing: z.number().positive().describe("Maximum centre-to-centre spacing, in units (0.15 = c/15)."),
});

server.tool("revit_list_rebar_types", "Read: reinforcement setup of the document: bar types (nominal Ø mm, grade, bend diameters, kg/m), hook types, cover types, rebar shapes, area/path reinforcement types, fabric sheet (tela soldada) types, splice types, rebar tag families and rebar schedules. Use before modelling to map the engineer's notation to the template's types.", {
  includeShapes: z.boolean().optional().describe("Default true; false returns only shapeCount."),
}, fwd("list_rebar_types"));

server.tool("revit_get_rebars", "Read: rebar in the model, a view or some hosts, with quantity, bar length, total length and steel mass (nominal section x 7850 kg/m3), summarised by grade and diameter (byDiameter) plus per-set items (number, partition, shape, layout, spacing). Use it to check quantities against a schedule or IFC.", {
  viewId: z.number().int().optional(), hostIds: z.array(z.number().int()).optional(), partition: z.string().optional(),
  includeItems: z.boolean().optional(), limit: z.number().int().min(1).max(20000).optional(),
}, fwd("get_rebars"));

server.tool("revit_get_rebar_host_info", "Read: whether elements can host rebar (structural concrete), their cover per face (mm) and the rebar they already hold.", {
  elementIds: idsField,
}, fwd("get_rebar_host_info"));

server.tool("revit_audit_rebar_template", "Read: audit the open document (a project created from the office template) against NBR 7480 bar series and NBR 6118 minimums: missing CA-50/CA-60 diameters, bend diameters below tables 9.1/9.2, usual hooks, covers of table 7.2 (CAA I-IV, with/without rigorous control), basic shapes (straight, arc, stirrup), rebar tags, rebar schedule, fabric sheets, splice types. Returns gaps for the user to fix the template.", {}, fwd("audit_rebar_template"));

server.tool("revit_new_project_from_template", "UI action: create a project from a .rte template, save it as saveAsPath (.rvt) and open it (activate:false keeps it windowless; target with documentTitle). The template file is not modified. Use it to audit a template or to get a disposable test model.", {
  templatePath: z.string(), saveAsPath: z.string(), overwrite: z.boolean().optional(), activate: z.boolean().optional(),
}, fwd("new_project_from_template"));

server.tool("revit_reinforce_curved_wall", "Reinforce a circular/arc concrete wall (one or more arc walls of the same circle, e.g. from revit_create_curved_wall) with native, editable rebar: horizontal rings as shape-driven arc sets (bar type, spacing and count editable in Revit), split into bars <= maxBarLength with tension laps staggered half a bar between consecutive rings, and vertical bars as free-form sets on each face, split into bars <= maxBarLength with laps staggered between neighbouring bars when the wall is taller than one bar. Every layer is concentric with the wall faces (constant cover). Ring lap defaults to NBR 6118 9.5.2.2 (fck 30, poor bond for horizontal bars, 50% spliced per section); pass lapLength to impose one. Ask the user the cover (CAA) first. Returns per-set ids, quantities, lengths, kg and the lap calculation.", {
  wallIds: idsField,
  ringBar: barSpec.describe("Horizontal rings, e.g. {diameterMm:10, spacing:0.15}."),
  verticalBar: barSpec.describe("Vertical bars, e.g. {diameterMm:10, spacing:0.15}."),
  cover: z.number().positive().describe("Clear cover in units (0.03 = 3 cm)."),
  faces: z.enum(["both", "interior", "exterior"]).optional().describe("Default both."),
  layerOrder: z.enum(["ringsOutside", "verticalsOutside"]).optional().describe("Which layer is closest to the faces. Default ringsOutside."),
  lapLength: z.number().positive().optional().describe("Imposed ring lap in units; default computed by NBR 6118."),
  verticalLapLength: z.number().positive().optional().describe("Imposed vertical lap (walls taller than maxBarLength); default NBR 6118, good bond, 50% spliced per section."),
  fckMpa: z.number().positive().optional().describe("Default 30."),
  ringBond: z.enum(["good", "poor"]).optional().describe("Bond zone for the ring laps. Default poor (conservative)."),
  maxBarLength: z.number().positive().optional().describe("Commercial bar length. Default 12 m."),
  partition: z.string().optional().describe("Rebar numbering partition (e.g. 'RESERVATORIO')."),
  comment: z.string().optional(),
  show3D: z.boolean().optional().describe("Default true: unobscured in every 3D view (solid at Fine detail)."),
  units: unitsField, dryRun: dryRunField,
}, fwdWrite("reinforce_curved_wall"));

server.tool("revit_batch",
  "Run multiple commands inside ONE Revit Transaction. All steps = one undo entry. Each step: { command, params }. Supports dryRun for preview.",
  {
    idempotencyKey: idempotencyKeyField,
    stopOnError: z.boolean().optional().describe("Default true: rollback on first failure."),
    dryRun: dryRunField,
    steps: z.array(z.object({
      command: z.string(),
      params: z.record(z.unknown()).optional(),
    })).min(1),
  },
  async (params) => {
    const steps = params.steps.map((s) => ({
      command: s.command,
      params: (s.params ?? {}) as Record<string, unknown>,
    }));
    return envelopeToToolResult(
      await callRevitBatch(steps, params.stopOnError ?? true, params.dryRun === true, params.idempotencyKey),
    );
  },
);

// ═══════════════════════════════════════════════════════════════════════════
// WORKFLOW RECIPES (P4) — orchestration over verified kernel commands
// ═══════════════════════════════════════════════════════════════════════════

server.tool("revit_recipe_model_health_triage",
  "WORKFLOW RECIPE (read-only): run a model-health scan and return a prioritized, " +
  "actionable triage list — each issue with its severity and a recommended fix. " +
  "Composes get_model_health; use this instead of get_model_health when you want guidance, not raw metrics.",
  { deep: z.boolean().optional().describe("Include the purge scan (slower). Default false.") },
  async (params) => envelopeToToolResult(await modelHealthTriage(callRevit, params.deep === true)));

const clashPairSchema = z.object({
  label: z.string().optional().describe("Human label for this pair, e.g. 'Ducts × Struct link'."),
  setA: elementSetSchema,
  setB: elementSetSchemaB,
  axis: z.enum(["bbox", "Z"]).optional(),
  direction: z.enum(["below", "above"]).optional(),
  clearanceMm: z.number().min(0).optional(),
  viewId: z.number().int().optional(),
  sampleCount: z.number().int().min(1).max(10).optional(),
  maxResults: z.number().int().min(1).max(2000).optional(),
});

server.tool("revit_recipe_clash_review",
  "WORKFLOW RECIPE (read-only): run a coordination clash sweep across multiple element-set pairs " +
  "(host vs host, or host vs LINKED RVT) and return a consolidated, prioritized clash report — " +
  "hard clashes first, then clearance violations by smallest gap, counted per pair. " +
  "Each pair is a check_clearance input; for links set setB.source='link' + linkId (from revit_get_linked_files). " +
  "Composes check_clearance.",
  { pairs: z.array(clashPairSchema).min(1).describe("Coordination matrix: each entry is a labelled check_clearance pair.") },
  async (params) => envelopeToToolResult(await clashReview(callRevit, params.pairs as never)));

// ═══════════════════════════════════════════════════════════════════════════
// BOOT
// ═══════════════════════════════════════════════════════════════════════════

async function main() {
  const transport = new StdioServerTransport();
  await server.connect(transport);
  console.error(`[revit-mcp-server] v0.9.0 connected to Revit addin at ${REVIT_BASE_URL}`);
  if (ENABLED_PROFILES !== null)
    console.error(
      `[revit-mcp-server] profiles: ${[...ENABLED_PROFILES].sort().join(", ")} ` +
      `— ${toolsRegistered} tools exposed, ${toolsSkipped} hidden`,
    );

  // Startup connectivity probe — log diagnostics but never crash.
  const health = await checkRevitHealth();
  if (!health.reachable) {
    console.error(
      `[revit-mcp-server] WARNING: Revit addin not reachable at ${REVIT_BASE_URL}. ` +
      "Ensure Revit is running with RevitMCPAddin loaded.",
    );
  } else if (health.authEnabled && !health.authTokenPresent) {
    console.error(
      "[revit-mcp-server] WARNING: Revit addin requires auth but no token was found. " +
      "Set REVIT_MCP_AUTH_TOKEN or ensure the token file is readable at " +
      `%APPDATA%/Autodesk/Revit/Addins/${process.env.REVIT_MCP_VERSION ?? "2026"}/revit-mcp-token.txt`,
    );
  } else {
    console.error(
      `[revit-mcp-server] Revit addin reachable (v${health.version ?? "?"}, ` +
      `auth: ${health.authEnabled ? "enabled" : "disabled"})`,
    );
  }
}

main().catch((err) => {
  console.error("[revit-mcp-server] fatal:", err);
  process.exit(1);
});
