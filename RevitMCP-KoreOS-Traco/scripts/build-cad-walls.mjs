#!/usr/bin/env node
// Trace walls from a linked CAD plan and create them, driven by a JSON config (layers, planner
// options, classification rules -> wall type + height). Requires add-in >= 0.8.48.
// Every write stage is a dryRun unless --commit is given.
//
//   node scripts/build-cad-walls.mjs --config scripts/cad/teste-dwg.json --stage plan
//   node scripts/build-cad-walls.mjs --config ... --stage create [--group walls] [--commit]
//   node scripts/build-cad-walls.mjs --config ... --stage doors [--commit]      doors from swing arcs on created walls
//   node scripts/build-cad-walls.mjs --config ... --stage teardown [--commit]   deletes ids recorded by create/doors
//
// Group kinds: default = paired faces (planWallsFromCad); "single" = unpaired lines of another group
// inside regions (planSingleLineWalls). extraWalls add explicit walls the DWG misses.
//
// plan writes .cad-build/<group>-plan.json and -classified.json; review them (and an image) before create.
import {callRevit, callRevitBatch} from '../src/McpServer/dist/revitClient.js';
import {planWallsFromCad, planSingleLineWalls, planDoorsFromCad} from '../src/McpServer/dist/cadWalls.js';
import {readFileSync, writeFileSync, existsSync, mkdirSync} from 'node:fs';

const argv = process.argv.slice(2);
const args = {};
for (let i = 0; i < argv.length; i++)
  if (argv[i].startsWith('--')) args[argv[i].slice(2)] = argv[i + 1] && !argv[i + 1].startsWith('--') ? argv[++i] : true;
const cfg = JSON.parse(readFileSync(args.config, 'utf8'));
const commit = args.commit === true;
const out = new URL('../.cad-build/', import.meta.url);
mkdirSync(out, {recursive: true});
const file = (n) => new URL(n, out);
const doc = {documentTitle: cfg.documentTitle};

function must(env, what) {
  if (!env.ok) { console.error(`${what} failed:`, JSON.stringify(env.error ?? env, null, 1)); process.exit(1); }
  return env.data;
}

async function findImport() {
  const list = must(await callRevit('inspect_cad', {...doc}), 'inspect_cad list');
  // linked types are named without extension ("teste"), imports keep it ("x.dwg")
  const bare = (s) => s.toLowerCase().replace(/\.(dwg|dxf)$/, '');
  const hits = list.instances.filter((i) => bare(i.name ?? '') === bare(cfg.cadFile));
  if (hits.length !== 1) { console.error(`Expected one CAD instance named ${cfg.cadFile}, got ${hits.length}: ${JSON.stringify(list.instances)}`); process.exit(1); }
  return hits[0].id;
}

async function curves(id, layers) {
  const all = [];
  for (let offset = 0; ;) {
    const d = must(await callRevit('inspect_cad', {...doc, importInstanceId: id, mode: 'curves', layers, bboxMeters: cfg.bbox, offset, limit: 10000}), 'inspect_cad curves');
    all.push(...d.curves);
    if (!d.hasMore) return all;
    offset = d.nextOffset;
  }
}

const inside = (p, b) => p[0] >= b.minX && p[0] <= b.maxX && p[1] >= b.minY && p[1] <= b.maxY;

function classify(wall, rules) {
  const mid = [(wall.start[0] + wall.end[0]) / 2, (wall.start[1] + wall.end[1]) / 2];
  const length = Math.hypot(wall.end[0] - wall.start[0], wall.end[1] - wall.start[1]);
  return rules.find((r) => (!r.thickness || r.thickness.some((t) => Math.abs(t - wall.thickness) < 1e-6))
    && (!r.maxLength || length <= r.maxLength)
    && (!r.region || inside(mid, r.region))
    && (!r.outsideBuilding || !inside(mid, cfg.building)));
}

async function stagePlan() {
  const id = await findImport();
  const plans = {};
  for (const g of cfg.groups) {
    let plan, walls;
    if (g.kind === 'single') {
      const source = plans[g.fromGroup];
      if (!source) { console.error(`[${g.name}] fromGroup ${g.fromGroup} must be planned first`); process.exit(1); }
      walls = planSingleLineWalls(source, {regions: g.regions, minLength: g.minLength, idPrefix: 'S'}).map((w) => ({...w, thickness: 0}));
      plan = {walls, openings: [], shortPieces: [], excluded: 0, stats: {unpairedOver30cm: 0}};
    } else {
      const main = await curves(id, g.layers);
      const ev = g.evidenceLayers ? (await curves(id, g.evidenceLayers)).map((c) => ({...c, b: c.b ?? `layer:${c.l}`})) : [];
      plan = planWallsFromCad([...main, ...ev], {...g.options, unpairedLimit: 100000});
      walls = [...plan.walls, ...(g.extraWalls ?? []).map((x) => ({...x, length: Math.hypot(x.end[0] - x.start[0], x.end[1] - x.start[1])}))];
    }
    plans[g.name] = plan;
    writeFileSync(file(`${g.name}-plan.json`), JSON.stringify(plan, null, 1));
    const classified = walls.map((w) => ({...w, rule: (w.rule && g.rules.find((r) => r.name === w.rule)) || classify(w, g.rules) || null}));
    writeFileSync(file(`${g.name}-classified.json`), JSON.stringify(classified, null, 1));
    console.log(`
[${g.name}] ${walls.length} walls, ${plan.openings.length} openings, ${plan.shortPieces.length} short pieces, ${plan.excluded} excluded, ${plan.stats.unpairedOver30cm} unpaired`);
    for (const w of classified)
      console.log(`  ${w.id.padEnd(4)} ${String(w.thickness).padEnd(5)} ${w.length.toFixed(2).padStart(6)} m  ${(w.rule?.name ?? 'UNCLASSIFIED').padEnd(18)} ${w.rule ? `${w.rule.wallTypeName} h=${w.rule.height}${w.rule.baseOffset ? ` base=${w.rule.baseOffset}` : ''}` : ''}`);
    if (classified.some((w) => !w.rule)) console.log('  !! unclassified walls will not be created');
  }
}

async function stageCreate() {
  const created = existsSync(file('created.json')) ? JSON.parse(readFileSync(file('created.json'), 'utf8')) : {};
  for (const g of cfg.groups.filter((x) => args.group === undefined || x.name === args.group)) {
    if (commit && created[g.name]?.length) { console.error(`[${g.name}] already created (${created[g.name].length}); run teardown first.`); process.exit(1); }
    const classified = JSON.parse(readFileSync(file(`${g.name}-classified.json`), 'utf8')).filter((w) => w.rule);
    // rule.bands [[base, top], ...] splits a wall vertically (glass below and above a transom beam)
    const items = classified.flatMap((w) => (w.rule.bands ?? [[w.rule.baseOffset ?? 0, (w.rule.baseOffset ?? 0) + w.rule.height]])
      .map(([base, top], band) => ({w, band, base, top})));
    const steps = items.map(({w, base, top}) => ({command: 'create_wall', params: {...doc, levelName: cfg.levelName,
      start: {x: w.start[0], y: w.start[1], z: 0}, end: {x: w.end[0], y: w.end[1], z: 0},
      wallTypeName: w.rule.wallTypeName, height: top - base, baseOffset: base}}));
    const env = await callRevitBatch(steps, true, !commit);
    writeFileSync(file(`${g.name}-create-${commit ? 'commit' : 'dry'}.json`), JSON.stringify(env, null, 1));
    if (!env.ok || (commit && env.committed === false)) {
      console.error(`[${g.name}] batch failed/not committed:`, JSON.stringify(env.error ?? env.failures ?? env, null, 1).slice(0, 4000));
      process.exit(1);
    }
    const results = env.results ?? env.data?.results ?? [];
    const ids = results.map((r) => r?.data?.id ?? r?.result?.id).filter(Boolean);
    console.log(`[${g.name}] ${commit ? 'committed' : 'dryRun'}: ${steps.length} steps, ${ids.length} ids${commit ? '' : ' (rolled back)'}`);
    if (commit) {
      created[g.name] = items.map(({w, band}, i) => ({planId: w.id, band, rule: w.rule.name, elementId: ids[i]}));
      writeFileSync(file('created.json'), JSON.stringify(created, null, 1));
    }
  }
}

async function stageDoors() {
  const created = JSON.parse(readFileSync(file('created.json'), 'utf8'));
  if (commit && created.doors?.length) { console.error('doors already created; teardown first'); process.exit(1); }
  const hosts = [];
  for (const g of cfg.groups) {
    const classified = JSON.parse(readFileSync(file(`${g.name}-classified.json`), 'utf8'));
    for (const c of created[g.name] ?? []) {
      const w = classified.find((x) => x.id === c.planId);
      const type = w && (await wallTypeWidth(w.rule.wallTypeName));
      // a hosted door needs a wall taller than the leaf and not a curtain wall (those take door panels)
      const tooLow = (w?.rule?.bands ? Math.max(...w.rule.bands.map((x) => x[1] - x[0])) : w?.rule?.height ?? 0) < (cfg.doors.minHostHeight ?? 2.3);
      const excludedType = (cfg.doors.excludeHostTypes ?? []).includes(w?.rule?.wallTypeName);
      if (w && c.elementId && (c.band ?? 0) === 0) hosts.push({id: String(c.elementId), start: w.start, end: w.end, thickness: type ?? w.thickness, skip: tooLow || excludedType ? (tooLow ? 'host too low' : 'curtain host') : null});
    }
  }
  const id = await findImport();
  const arcs = await curves(id, cfg.doors.layers);
  const {doors, unmatched} = planDoorsFromCad(arcs, hosts);
  writeFileSync(file('doors-plan.json'), JSON.stringify({doors, unmatched}, null, 1));
  console.log(`doors ${doors.length}, unmatched arcs ${unmatched.length}`);
  const typeFor = (w) => cfg.doors.typesByMaxWidth.find(([max]) => w <= max)[1];
  const placed = [];
  for (const d of doors) {
    const host = hosts.find((h) => h.id === d.hostId);
    if (host?.skip) { console.log(`  skip door at ${d.center} width ${d.width}: ${host.skip}`); continue; }
    const params = {...doc, familyName: cfg.doors.familyName, familyTypeName: typeFor(d.width), levelName: cfg.levelName,
      hostId: Number(d.hostId), location: {x: d.center[0], y: d.center[1]}};
    const env = await callRevit('place_family_instance', params, !commit);
    const ok = env.ok && env.data?.placed !== false;
    console.log(`  host ${d.hostId} center ${d.center} width ${d.width} -> ${typeFor(d.width)}: ${ok ? (env.data?.id ?? 'ok') : JSON.stringify(env.error ?? env.data).slice(0, 300)}`);
    if (ok && commit) placed.push({...d, elementId: env.data.id, typeName: typeFor(d.width)});
  }
  if (commit) { created.doors = placed; writeFileSync(file('created.json'), JSON.stringify(created, null, 1)); }
  for (const u of unmatched) console.log(`  unmatched arc ${u.layer} c=${u.center} r=${u.radius}`);
}

const typeWidths = {};
async function wallTypeWidth(name) {
  if (!Object.keys(typeWidths).length) {
    const d = must(await callRevit('list_wall_types', {...doc}), 'list_wall_types');
    for (const t of d.wallTypes ?? d.types ?? d) typeWidths[t.name] = t.widthFeet * 0.3048;
  }
  return typeWidths[name];
}

async function stageTeardown() {
  const created = existsSync(file('created.json')) ? JSON.parse(readFileSync(file('created.json'), 'utf8')) : {};
  const ids = Object.values(created).flat().map((c) => c.elementId).filter(Boolean).map(Number);
  if (!ids.length) { console.log('nothing recorded'); return; }
  const d = must(await callRevit('delete_elements', {...doc, ids}, !commit), 'delete_elements');
  console.log(JSON.stringify(d).slice(0, 1500));
  if (commit) writeFileSync(file('created.json'), JSON.stringify({}, null, 1));
}

const stages = {plan: stagePlan, create: stageCreate, doors: stageDoors, teardown: stageTeardown};
if (!stages[args.stage]) { console.error(`--stage ${Object.keys(stages).join('|')}`); process.exit(1); }
await stages[args.stage]();
