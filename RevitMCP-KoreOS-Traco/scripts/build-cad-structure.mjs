#!/usr/bin/env node
// Facade structure for a glass skin traced by build-cad-walls.mjs: columns in the gaps between glass
// runs of the same facade line (panes stop at each column) and one transom beam per facade.
// Uses the same JSON config (levels, structure). dryRun unless --commit.
//
//   node scripts/build-cad-structure.mjs --config scripts/cad/teste-dwg.json --stage plan
//   node scripts/build-cad-structure.mjs --config ... --stage setup   [--commit]   levels + column/beam types
//   node scripts/build-cad-structure.mjs --config ... --stage create  [--commit]   columns (top level, rotation) + beams
import {callRevit} from '../src/McpServer/dist/revitClient.js';
import {readFileSync, writeFileSync, existsSync} from 'node:fs';

const argv = process.argv.slice(2);
const args = {};
for (let i = 0; i < argv.length; i++)
  if (argv[i].startsWith('--')) args[argv[i].slice(2)] = argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[++i] : true;
const cfg = JSON.parse(readFileSync(args.config, 'utf8'));
const S = cfg.structure;
const commit = args.commit === true;
const file = (n) => new URL(`../.cad-build/${n}`, import.meta.url);
const doc = {documentTitle: cfg.documentTitle};
const r4 = (v) => Math.round(v * 1e4) / 1e4;

function must(env, what) {
  if (!env.ok) { console.error(`${what} failed:`, JSON.stringify(env.error ?? env, null, 1).slice(0, 3000)); process.exit(1); }
  return env.data;
}

/** Group glass runs by facade line; columns at gaps inside gapRange; beam spans the line. */
function planStructure() {
  const glass = JSON.parse(readFileSync(file(`${S.fromGroup}-classified.json`), 'utf8')).filter((w) => w.rule?.bands);
  const lines = [];
  for (const w of glass) {
    const d = [w.end[0] - w.start[0], w.end[1] - w.start[1]], L = Math.hypot(...d);
    let u = [d[0] / L, d[1] / L];
    const line = lines.find((l) => Math.abs(l.u[0] * u[1] - l.u[1] * u[0]) < 0.02
      && Math.abs((w.start[0] - l.o[0]) * l.n[0] + (w.start[1] - l.o[1]) * l.n[1]) <= S.lineTolerance);
    const target = line ?? (lines.push({u, n: [-u[1], u[0]], o: w.start, runs: []}), lines[lines.length - 1]);
    const s0 = (w.start[0] - target.o[0]) * target.u[0] + (w.start[1] - target.o[1]) * target.u[1];
    const s1 = (w.end[0] - target.o[0]) * target.u[0] + (w.end[1] - target.o[1]) * target.u[1];
    target.runs.push([Math.min(s0, s1), Math.max(s0, s1), w.id]);
  }
  const at = (l, s) => [r4(l.o[0] + l.u[0] * s), r4(l.o[1] + l.u[1] * s)];
  const columns = [], beams = [];
  for (const l of lines) {
    l.runs.sort((a, b) => a[0] - b[0]);
    const angle = r4(Math.atan2(l.u[1], l.u[0]) * 180 / Math.PI);
    for (let i = 1; i < l.runs.length; i++) {
      const gap = l.runs[i][0] - l.runs[i - 1][1];
      if (gap >= S.gapRange[0] && gap <= S.gapRange[1])
        columns.push({at: at(l, (l.runs[i][0] + l.runs[i - 1][1]) / 2), facadeAngleDeg: angle, between: [l.runs[i - 1][2], l.runs[i][2]], gap: r4(gap)});
    }
    beams.push({start: at(l, l.runs[0][0]), end: at(l, l.runs[l.runs.length - 1][1]), runs: l.runs.length, angleDeg: angle});
  }
  for (const c of S.extraColumns ?? []) columns.push({...c, extra: true, normal: lines.find((l) => Math.abs(Math.atan2(l.u[1], l.u[0]) * 180 / Math.PI - c.facadeAngleDeg) < 2)?.n});
  for (const c of columns) {
    const f = S.facades.find((x) => Math.abs(x.angleDeg - c.facadeAngleDeg) < 2);
    if (!f) { console.error(`no facade spec for angle ${c.facadeAngleDeg}`); process.exit(1); }
    const n = c.normal ?? lines.find((l) => Math.abs(Math.atan2(l.u[1], l.u[0]) * 180 / Math.PI - c.facadeAngleDeg) < 2).n;
    c.typeName = f.typeName;
    c.center = [r4(c.at[0] + n[0] * f.offset), r4(c.at[1] + n[1] * f.offset)];
  }
  return {columns, beams};
}

async function typeByName(familyName, typeName) {
  const d = must(await callRevit('list_family_types', {...doc, familyName}), 'list_family_types');
  return d.familyTypes.find((t) => t.name === typeName);
}

async function ensureType(spec) {
  const existing = await typeByName(spec.familyName, spec.typeName);
  if (existing) { console.log(`type ${spec.typeName} exists (${existing.id})`); return existing.id; }
  const source = await typeByName(spec.familyName, spec.sourceTypeName);
  if (!source) { console.error(`source type ${spec.sourceTypeName} not found`); process.exit(1); }
  if (!commit) { console.log(`would duplicate ${spec.sourceTypeName} -> ${spec.typeName} (b=${spec.b}, h=${spec.h})`); return null; }
  const dup = must(await callRevit('duplicate_family_type', {...doc, sourceTypeId: source.id, newName: spec.typeName}), 'duplicate_family_type');
  const id = dup.typeId ?? dup.id ?? dup.newTypeId;
  for (const [name, value] of [['b', spec.b], ['h', spec.h]]) {
    const r = must(await callRevit('set_parameter', {...doc, id, parameterName: name, value, units: 'meters'}), `set ${name}`);
    console.log(`  ${spec.typeName}.${name} = ${r.newValueString ?? r.after ?? JSON.stringify(r).slice(0, 120)}`);
  }
  return id;
}

async function stageSetup() {
  const levels = must(await callRevit('list_levels', {...doc}), 'list_levels').levels;
  for (const lv of cfg.levels ?? []) {
    const hit = levels.find((l) => l.name === lv.name);
    if (hit) { console.log(`level ${lv.name} exists at ${hit.elevationMeters}`); continue; }
    const r = await callRevit('create_level', {...doc, name: lv.name, elevation: lv.elevation}, !commit);
    console.log(`level ${lv.name}: ${JSON.stringify(must(r, 'create_level')).slice(0, 200)}`);
  }
  for (const f of S.facades) await ensureType({...S.column, ...f});
  await ensureType(S.beam);
}

async function stageCreate() {
  const created = existsSync(file('created.json')) ? JSON.parse(readFileSync(file('created.json'), 'utf8')) : {};
  if (commit && (created.columns?.length || created.beams?.length)) { console.error('structure already created'); process.exit(1); }
  const plan = planStructure();
  const colTypes = {};
  for (const f of S.facades) colTypes[f.typeName] = await typeByName(S.column.familyName, f.typeName);
  const beamType = await typeByName(S.beam.familyName, S.beam.typeName);
  const top = must(await callRevit('list_levels', {...doc}), 'list_levels').levels.find((l) => l.name === S.column.topLevel);
  if (Object.values(colTypes).some((t) => !t) || !beamType || !top) { console.error('run --stage setup --commit first'); process.exit(1); }
  const columns = [], beams = [];
  for (const c of plan.columns) {
    // place_family_instance: create_column <= 0.8.48 only searched architectural columns
    const env = await callRevit('place_family_instance', {...doc, location: {x: c.center[0], y: c.center[1]}, levelName: cfg.levelName,
      familyName: S.column.familyName, familyTypeName: c.typeName, category: 'OST_StructuralColumns'}, !commit);
    const d = must(env, 'place_family_instance');
    if (!commit) { console.log(`  column ${c.typeName} at ${c.center} (facade ${c.facadeAngleDeg} deg) ok`); continue; }
    const id = d.id;
    must(await callRevit('set_parameter', {...doc, id, parameterName: 'Nível superior', value: {id: top.id}}), 'top level');
    must(await callRevit('set_parameter', {...doc, id, parameterName: 'Deslocamento superior', value: 0, units: 'meters'}), 'top offset');
    // family b runs along X and h along Y: turn h (long side) onto the facade direction
    const rot = r4(c.facadeAngleDeg - 90);
    if (Math.abs(rot) > 1e-3) must(await callRevit('rotate_element', {...doc, id, center: {x: c.center[0], y: c.center[1], z: 0}, angleDeg: rot}), 'rotate');
    columns.push({...c, elementId: id, rotationDeg: rot});
    console.log(`  column ${id} at ${c.at} rot ${rot}`);
  }
  for (const b of plan.beams) {
    const env = await callRevit('create_framing_member', {...doc, typeId: beamType.id, levelName: cfg.levelName,
      start: {x: b.start[0], y: b.start[1], z: S.beam.topZ}, end: {x: b.end[0], y: b.end[1], z: S.beam.topZ},
      zJustification: 'top', yJustification: 'center', comments: 'MCP-CAD:transom'}, !commit);
    const d = must(env, 'create_framing_member');
    console.log(`  beam ${commit ? d.id : '(dry)'} ${b.start} -> ${b.end}`);
    if (commit) beams.push({...b, elementId: d.id});
  }
  if (commit) { created.columns = columns; created.beams = beams; writeFileSync(file('created.json'), JSON.stringify(created, null, 1)); }
}

async function stagePlan() {
  const plan = planStructure();
  writeFileSync(file('structure-plan.json'), JSON.stringify(plan, null, 1));
  for (const c of plan.columns) console.log(`column ${c.typeName} at ${c.center} facade ${c.facadeAngleDeg}${c.gap ? ` gap ${c.gap}` : ' (extra)'}`);
  for (const b of plan.beams) console.log(`beam ${b.start} -> ${b.end} over ${b.runs} runs`);
}

const stages = {plan: stagePlan, setup: stageSetup, create: stageCreate};
if (!stages[args.stage]) { console.error(`--stage ${Object.keys(stages).join('|')}`); process.exit(1); }
await stages[args.stage]();
