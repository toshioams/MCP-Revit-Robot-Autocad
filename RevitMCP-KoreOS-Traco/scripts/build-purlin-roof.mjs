#!/usr/bin/env node
// Pilot orchestration: timber purlin roof on masonry gables for the cabin in "teste mcp".
// Requires Revit add-in >= 0.8.46. Every stage runs as dryRun unless --commit is given.
//
//   node scripts/build-purlin-roof.mjs --stage plan
//   node scripts/build-purlin-roof.mjs --stage types    [--commit]   section types per member size
//   node scripts/build-purlin-roof.mjs --stage teardown [--commit]   delete previous framing + gables
//   node scripts/build-purlin-roof.mjs --stage detach   [--commit]
//   node scripts/build-purlin-roof.mjs --stage covering [--commit]
//   node scripts/build-purlin-roof.mjs --stage framing  [--commit]   + join boards the covering trims
//   node scripts/build-purlin-roof.mjs --stage gables   [--commit]
//   node scripts/build-purlin-roof.mjs --stage gutters  [--commit]   hang gutters on the eave boards
//   node scripts/build-purlin-roof.mjs --stage verify
//
// Re-running after a roof change: plan -> types -> teardown -> covering -> framing -> gables -> gutters.
//
// Run stages in order and inspect sections/images between them. Layout only: member sections
// are placeholders to be checked by the engineer (see docs/ROOF_STRUCTURE.md).
import {callRevit as rawCall, callRevitBatch as rawBatch, checkRevitHealth} from '../src/McpServer/dist/revitClient.js';
import {writeFileSync, readFileSync, existsSync, mkdirSync} from 'node:fs';

const args = process.argv.slice(2);
const stage = args[args.indexOf('--stage') + 1];
const commit = args.includes('--commit');
const outDir = new URL('../.roof-build/', import.meta.url);
if (!existsSync(outDir)) mkdirSync(outDir);
const save = (name, data) => writeFileSync(new URL(name, outDir), JSON.stringify(data, null, 1));
const load = name => JSON.parse(readFileSync(new URL(name, outDir), 'utf8'));

const MODEL = {
  title: 'teste mcp',
  roof: {id: 19401056, uid: 'ea0f9cd5-530a-4317-a6fc-e0383cf7c554-01280960'},
  walls: [
    {id: 19389460, uid: '14445dd9-ab79-490f-9b40-1b52a0cf750e-0127dc14', role: 'gable_start'},
    {id: 19389461, uid: '14445dd9-ab79-490f-9b40-1b52a0cf750e-0127dc15', role: 'eave_b'},
    {id: 19389462, uid: '14445dd9-ab79-490f-9b40-1b52a0cf750e-0127dc16', role: 'gable_end'},
    {id: 19389463, uid: '14445dd9-ab79-490f-9b40-1b52a0cf750e-0127dc17', role: 'eave_a'},
  ],
  wallTypeId: 3605618, level: 'N1-AC',
  sectionSourceTypeId: 19401617,          // _ARQPWR_Viga perfil retangular : MDR 5 x 10 cm
  gutterTypeId: 19407743,                 // _ARQPWR_Calha 30 x 20 cm (copied from the showroom)
  gutterIds: [19408765, 19408766],        // gutters placed 11/09 on the tile edge (to be replaced)
  // _ARQPWR_Viga perfil retangular types created 11/09 (Largura x Altura).
  types: {wall_plate: 19402551, purlin: 19402553, ridge_beam: 19402555, rafter: 19402557,
          blocking: 19402559, fascia_eave: 19402561, fascia_verge: 19402561, joint_trim: 19402563},
  nonStructural: new Set(['blocking', 'fascia_eave', 'fascia_verge', 'joint_trim']),
};
const PLAN_INPUT = {x0: 0, y0: 0, x1: 6, y1: 8, ridgeAxis: 'y', pitchDegrees: 45, eaveOverhang: 0.6, gableOverhang: 0.6,
  eaveWallThickness: 0.2, gableWallThickness: 0.2, masonryTop: 2.8,
  plate: {width: 0.15, height: 0.08}, purlin: {width: 0.10, height: 0.32}, ridge: {width: 0.10, height: 0.32},
  rafter: {width: 0.06, height: 0.12}, rafterMaxSpacing: 0.6, boarding: 0.02, membrane: 0, battens: 0.05, tile: 0.10,
  jointGap: 0.015, jointTrim: {width: 0.05, height: 0.02}, purlinPocketClearance: 0.01,
  blocking: true, blockingThickness: 0.025, fasciaBoards: true, fascia: {width: 0.025, height: 0.20},
  // 11/09 user decisions: blocking in the wall outer face plane, boards recessed 5 cm with plumb depth.
  plateAlignment: 'outer', fasciaRecess: 0.05, fasciaAutoHeight: true};
const GUTTER_BELOW_BOARDING = 0.02;
const ROLE_LABEL = {wall_plate: 'Frechal', purlin: 'Terça', ridge_beam: 'Cumeeira', rafter: 'Caibro', blocking: 'Bloqueio',
  fascia_eave: 'Testeira', fascia_verge: 'Testeira lateral', joint_trim: 'Mata-junta'};
const cm = v => (Math.round(v * 1000) / 10).toString().replace('.', ',');
const sizeKey = m => `${Math.round(m.width * 1000)}x${Math.round(m.height * 1000)}`;

// Every call names the document (add-in >= 0.8.43), so switching windows cannot redirect writes.
const callRevit = (cmd, params, dry = false) => rawCall(cmd, {...params, documentTitle: MODEL.title}, dry);
const callRevitBatch = (steps, stop, dry) => rawBatch(steps.map(s => ({...s, params: {...s.params, documentTitle: MODEL.title}})), stop, dry);

function must(r, what) {
  if (!r.ok) { console.error(`${what} FAILED`, JSON.stringify(r.error ?? r, null, 1).slice(0, 3000)); process.exit(1); }
  if (commit && r.committed === false && !r.dryRun) { console.error(`${what}: not committed`); process.exit(1); }
  return r;
}
async function batch(steps, what) {
  const r = await callRevitBatch(steps, true, !commit);
  must(r, what);
  if (!commit && r.committed !== false) { console.error(`${what}: dryRun reported a commit — stop.`); process.exit(1); }
  console.log(`${what}: ${commit ? 'COMMITTED' : 'dryRun ok'} (${steps.length} steps)`);
  return r;
}
async function guardIdentity() {
  const h = await checkRevitHealth();
  if (!h.reachable || !/^0\.8\.(4[6-9]|[5-9]\d)/.test(h.version ?? '')) { console.error('Need add-in >= 0.8.46, got', h.version); process.exit(1); }
  const doc = must(await callRevit('get_document_info', {}), 'document');
  if (doc.data.title !== MODEL.title || doc.document !== MODEL.title) { console.error('Wrong document', doc.data.title, doc.document); process.exit(1); }
  for (const e of [MODEL.roof, ...MODEL.walls]) {
    const r = must(await callRevit('get_element_info', {id: e.id}), `element ${e.id}`);
    if (r.data.uniqueId !== e.uid) { console.error('UniqueId mismatch', e.id); process.exit(1); }
  }
}

await guardIdentity();

if (stage === 'plan') {
  const plan = must(await callRevit('plan_purlin_roof', PLAN_INPUT), 'plan').data;
  save('plan.json', plan);
  console.log(JSON.stringify({counts: plan.memberCounts, covering: plan.covering, checks: {...plan.checks, rafterStations: undefined}, warnings: plan.warnings}, null, 1));
}

if (stage === 'types') {
  // One rectangular section type per distinct member size (Largura x Altura), reused by name.
  const plan = load('plan.json');
  const existing = must(await callRevit('list_family_types', {category: 'OST_StructuralFraming'}), 'framing types').data.familyTypes;
  const sizes = new Map();
  for (const m of plan.members) if (!sizes.has(sizeKey(m))) sizes.set(sizeKey(m), m);
  const map = {};
  for (const [key, m] of sizes) {
    const name = `MCP ${ROLE_LABEL[m.role] ?? m.role} ${cm(m.width)} x ${cm(m.height)} cm`;
    const found = existing.find(t => t.name === name && t.familyName === '_ARQPWR_Viga perfil retangular');
    if (found) { map[key] = found.id; console.log('reuse', name, found.id); continue; }
    if (!commit) { console.log('would create', name); continue; }
    const dup = must(await callRevit('duplicate_family_type', {sourceTypeId: MODEL.sectionSourceTypeId, newName: name}), `type ${name}`).data;
    await batch([
      {command: 'set_parameter', params: {id: dup.typeId, parameterName: 'Largura', value: m.width, units: 'meters'}},
      {command: 'set_parameter', params: {id: dup.typeId, parameterName: 'Altura', value: m.height, units: 'meters'}},
    ], `size ${name}`);
    map[key] = dup.typeId;
  }
  if (commit) save('types.json', map);
}

if (stage === 'teardown') {
  // Previous generated framing and gable walls (openings go with their walls). dryRun lists dependents.
  const framing = existsSync(new URL('framing-created.json', outDir)) ? load('framing-created.json').map(x => x.id).filter(Boolean) : [];
  const gables = existsSync(new URL('gables-created.json', outDir)) ? load('gables-created.json').map(x => x.wallId).filter(Boolean) : [];
  const ids = [...framing, ...gables];
  if (ids.length === 0) { console.log('nothing to tear down'); process.exit(0); }
  // Preview first and classify every dependent: profile walls take their sketch internals and
  // openings with them (seen live: 62 dependents for 2 gables). Anything else stops the teardown.
  const preview = must(await callRevit('delete_elements', {ids}, true), 'teardown preview');
  const requested = new Set(ids);
  const allowed = new Set(['Sketch', 'SketchPlane', 'ReferencePlane', 'ModelLine', 'Dimension', 'LinearDimension', 'Opening', 'AnalyticalMember']);
  const unexpected = [];
  for (const id of preview.data.deletedIds.filter(i => !requested.has(i))) {
    const e = await callRevit('get_element_info', {id});
    if (!e.ok || !allowed.has(e.data.className)) unexpected.push(`${id}:${e.data?.className ?? e.error?.code}:${e.data?.category ?? ''}`);
  }
  if (unexpected.length) { console.error('Unexpected dependents, teardown stopped:', unexpected.slice(0, 20).join(' | ')); process.exit(1); }
  console.log(`teardown preview: requested ${ids.length}, deleted ${preview.data.deleted} (dependents are sketch internals/openings)`);
  if (!commit) process.exit(0);
  const r = must(await callRevit('delete_elements', {ids}), 'teardown');
  console.log(`teardown: COMMITTED requested ${r.data.requested}, deleted ${r.data.deleted}`);
  if (commit) { save('framing-created.previous.json', framing); save('gables-created.previous.json', gables); }
}

if (stage === 'detach') {
  // Plates need the masonry to end at 2.80; gables become separate profile walls.
  await batch(MODEL.walls.map(w => ({command: 'detach_wall_top', params: {wallId: w.id, roofId: MODEL.roof.id}})), 'detach walls');
}

if (stage === 'covering') {
  const plan = load('plan.json');
  const typeName = 'MCP Cobertura capa-canal sobre lambril';
  const layers = [
    {function: 'Finish1', widthMeters: PLAN_INPUT.tile, materialName: '_ARQPWR_Cerâmica Marrom'},
    {function: 'Substrate', widthMeters: PLAN_INPUT.battens, materialName: '.ESTRUTURA_Madeira Genérica'},
    {function: 'Membrane', widthMeters: 0, materialName: '.MEMBRANA_Manta de Polietileno Expandido'},
    {function: 'Structure', widthMeters: PLAN_INPUT.boarding, materialName: '_ARQPWR_Madeira Pinus', core: true},
  ];
  const roof = must(await callRevit('get_element_info', {id: MODEL.roof.id}), 'roof').data;
  if (roof.name === typeName) {
    // Type already applied: only the base elevation follows the new plan.
    await batch([{command: 'set_parameter', params: {id: MODEL.roof.id, parameterName: 'Deslocamento base do nível', value: plan.covering.baseElevationAtFootprint, units: 'meters'}}], 'roof base');
  } else if (!commit) {
    must(await callRevit('create_roof_type_variant', {sourceTypeId: roof.typeId, name: typeName, layers}, true), 'roof type dryRun');
    console.log('covering: dryRun ok; base elevation will be', plan.covering.baseElevationAtFootprint);
  } else {
    const t = must(await callRevit('create_roof_type_variant', {sourceTypeId: roof.typeId, name: typeName, layers}), 'roof type').data;
    save('covering-type.json', t);
    await batch([
      {command: 'change_element_type', params: {id: MODEL.roof.id, typeId: t.typeId}},
      {command: 'set_parameter', params: {id: MODEL.roof.id, parameterName: 'Deslocamento base do nível', value: plan.covering.baseElevationAtFootprint, units: 'meters'}},
    ], 'roof type + base');
  }
}

if (stage === 'framing') {
  const plan = load('plan.json');
  const types = load('types.json');
  const steps = plan.members.map(m => ({command: 'create_framing_member', params: {
    typeId: types[sizeKey(m)] ?? (() => { throw new Error(`no type for ${m.role} ${sizeKey(m)}; run --stage types --commit`); })(),
    start: m.start, end: m.end, levelName: MODEL.level,
    zJustification: m.zJustification, yJustification: 'center', structural: !MODEL.nonStructural.has(m.role),
    comments: `MCP-COBERTURA:${m.role}:${m.side}`, units: 'meters'}}));
  const r = await batch(steps, 'framing');
  if (commit) {
    const created = r.results.map((x, k) => ({role: plan.members[k].role, side: plan.members[k].side, id: x.data?.id, uniqueId: x.data?.uniqueId, cut: plan.members[k].cutByCovering}));
    save('framing-created.json', created);
    // Square boards whose inner top edge touches the sloped boarding: the covering trims the chamfer.
    const cutIds = created.filter(c => c.cut).map(c => c.id);
    if (cutIds.length) {
      const j = must(await callRevit('join_geometry', {cutterId: MODEL.roof.id, elementIds: cutIds}), 'join covering');
      const trimmed = j.data.results.filter(x => x.volumeAfterCubicMeters < x.volumeBeforeCubicMeters).length;
      console.log(`join covering: ${trimmed}/${cutIds.length} boards trimmed`);
    }
  } else console.log('framing dryRun: join_geometry runs only after commit (members must exist).');
}

if (stage === 'gables') {
  const plan = load('plan.json');
  const created = [];
  for (const g of plan.gables) {
    const base = {profile: g.profile, wallTypeId: MODEL.wallTypeId, levelName: MODEL.level, units: 'meters'};
    let w = await callRevit('create_profile_wall', base, true);
    if (!w.ok && w.error?.code === 'verification_failed') {
      console.log(`gable ${g.name}: solid check failed in transaction, diagnostics:`, w.error.message);
      w = await callRevit('create_profile_wall', {...base, verification: 'sketch'}, !commit);
    } else if (commit) w = await callRevit('create_profile_wall', {...base, verification: w.ok ? 'solid' : 'sketch'}, false);
    must(w, `gable ${g.name}`);
    console.log(`gable ${g.name}:`, commit ? `wall ${w.data.id}` : 'dryRun ok', w.data.verifiedBy, JSON.stringify(w.data.diagnostics));
    if (!commit) continue;
    // Post-commit geometry check: the solid must now span the profile.
    const info = must(await callRevit('get_element_info', {id: w.data.id}), 'gable readback').data;
    const zMin = info.boundingBox.min.z * 0.3048, zMax = info.boundingBox.max.z * 0.3048;
    const pMin = Math.min(...g.profile.map(v => v.z)), pMax = Math.max(...g.profile.map(v => v.z));
    console.log(`gable ${g.name} after commit: bbox z ${zMin.toFixed(4)}..${zMax.toFixed(4)} vs profile ${pMin.toFixed(4)}..${pMax.toFixed(4)}`);
    if (Math.abs(zMin - pMin) > 0.002 || Math.abs(zMax - pMax) > 0.002) {
      console.error(`gable ${g.name}: committed wall does not match the profile — inspect/delete wall ${w.data.id} before continuing.`);
      process.exit(1);
    }
    const pockets = g.purlinOpenings.map(o => ({command: 'create_opening_in_wall', params: {wallId: w.data.id, lower: o.lower, upper: o.upper, units: 'meters'}}));
    const r = await batch(pockets, `pockets ${g.name}`);
    created.push({gable: g.name, wallId: w.data.id, uniqueId: w.data.uniqueId, openings: r.results.map(x => x.data?.openingId)});
  }
  if (commit) save('gables-created.json', created);
}

if (stage === 'gutters') {
  // Gutter back wall against the eave board outer face, top just below the boarding. The sign
  // convention of HostedSweep offsets is found by previewing the four combinations.
  const plan = load('plan.json');
  const eave = plan.eave;
  const backA = PLAN_INPUT.x0 + eave.fasciaOuterFaceOffsetFromWallOuterFace;      // west (x0 side)
  const backB = PLAN_INPUT.x1 - eave.fasciaOuterFaceOffsetFromWallOuterFace;      // east
  const targetTop = eave.boardingUndersideAtFasciaOuterFace - GUTTER_BELOW_BOARDING;
  const dh = PLAN_INPUT.fasciaRecess, dz = targetTop - plan.covering.baseElevationAtFootprint;
  const score = created => created.reduce((err, c) => {
    const e = c.solidExtents, west = c.edgeStart.x < (PLAN_INPUT.x0 + PLAN_INPUT.x1) / 2;
    return err + Math.abs((west ? e.max.x : e.min.x) - (west ? backA : backB)) + Math.abs(e.max.z - targetTop);
  }, 0);
  let best = null;
  for (const h of [dh, -dh]) for (const v of [dz, -dz]) {
    const params = {roofId: MODEL.roof.id, typeId: MODEL.gutterTypeId, edgeKinds: ['eave'], faceSide: 'bottom', horizontalOffset: h, verticalOffset: v, maxProfileReachMeters: 0.6};
    const r = await callRevit('create_roof_edge_sweep', params, true);
    if (!r.ok) { console.log('preview failed', h, v, r.error?.message); continue; }
    const err = score(r.data.created);
    console.log(`preview h=${h.toFixed(4)} v=${v.toFixed(4)} error=${err.toFixed(4)} m`);
    if (!best || err < best.err) best = {err, params};
  }
  if (!best || best.err > 0.006) { console.error('No offset combination hangs the gutter on the eave board; inspect previews.'); process.exit(1); }
  console.log('chosen', JSON.stringify(best));
  const steps = [];
  const existingGutters = [];
  for (const id of MODEL.gutterIds) if ((await callRevit('get_element_info', {id})).ok) existingGutters.push(id);
  if (existingGutters.length) steps.push({command: 'delete_elements', params: {ids: existingGutters}});
  steps.push({command: 'create_roof_edge_sweep', params: best.params});
  const r = await batch(steps, 'gutters');
  if (commit) save('gutters-created.json', r.results.at(-1).data.created.map(c => ({id: c.id, uniqueId: c.uniqueId, extents: c.solidExtents})));
}

if (stage === 'verify') {
  const warnings = must(await callRevit('get_warnings', {}), 'warnings').data;
  console.log('warnings', warnings.total);
  for (const w of MODEL.walls) {
    const a = await callRevit('analyze_wall_roof_interface', {wallId: w.id, roofId: MODEL.roof.id, baseElevation: 2.8});
    console.log(w.role, a.ok ? {attached: a.data.wallTopAttachedToRoof, top: a.data.wallSolidElevation, across: a.data.undersideDifferenceAcrossThicknessMax} : a.error);
  }
  const faces = must(await callRevit('inspect_planar_faces', {id: MODEL.roof.id}), 'roof faces').data;
  console.log('roof faces', faces.faces.map(f => ({o: f.originMeters, n: f.normal})));
}
