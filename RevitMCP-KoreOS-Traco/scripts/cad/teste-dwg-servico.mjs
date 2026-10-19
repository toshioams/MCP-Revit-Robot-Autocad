#!/usr/bin/env node
// Pilot (teste dwg): service block = workshop shed over the 7 bays + parts room, derived from DWG
// coordinates read with inspect_cad (FORD_DIV zone lines, A-WALL NW wall and parts-room line,
// AR-ALV-SLD bay rectangles) and the user's front elevation (20 m service facade, ACM fascia
// 1.50 m at the top, roll-up gate 6.0 x 4.95 m). Design decisions are ours (user gave autonomy):
// shed floor at yard level (-0.45), 25 cm walls NW/rear/east up to 6.00, open front with columns
// at the bay divisions and an ACM fascia band 4.50-6.00, roof slabs at 5.50 hidden by the fascia.
//
//   node scripts/cad/teste-dwg-servico.mjs            dryRun
//   node scripts/cad/teste-dwg-servico.mjs --commit
import {callRevit, callRevitBatch} from '../../src/McpServer/dist/revitClient.js';
import {readFileSync, writeFileSync} from 'node:fs';

const commit = process.argv.includes('--commit');
const doc = {documentTitle: 'teste dwg'};
const created = JSON.parse(readFileSync(new URL('../../.cad-build/created.json', import.meta.url), 'utf8'));
if (commit && created.servico) { console.error('servico already created'); process.exit(1); }
const r4 = (v) => Math.round(v * 1e4) / 1e4;
const add = (a, b, k = 1) => [a[0] + b[0] * k, a[1] + b[1] * k];
const unit = (a, b) => { const d = [b[0] - a[0], b[1] - a[1]], L = Math.hypot(...d); return [d[0] / L, d[1] / L]; };
const cross = (a, u, b, v) => { // intersection of a + t u and b + s v
  const den = u[0] * v[1] - u[1] * v[0];
  const t = ((b[0] - a[0]) * v[1] - (b[1] - a[1]) * v[0]) / den;
  return add(a, u, t);
};
const pt = ([x, y]) => ({x: r4(x), y: r4(y), z: 0});

// ---- DWG geometry (meters, linked centered) ----
const nwWall = [[23.088, 38.08], [26.47, 48.76]];          // A-WALL 25 cm pair (centerline), FORD_DIV NW line
const rearDiv = [[26.5947, 48.7688], [55.3045, 40.0653]];  // FORD_DIV outer rear edge (next to the north site wall)
const eastDiv = [[55.5468, 40.1102], [52.7885, 29.1791]];  // FORD_DIV east edge (next to the east site wall)
const bayFront = [[23.4375, 37.6086], [49.7159, 29.8495]]; // front corners of the first and last bay
const bayCorners = [[27.2737, 36.4759], [31.0141, 35.3715], [34.7544, 34.2671], [38.4948, 33.1627], [42.2352, 32.0583], [45.9755, 30.9539]];
const partsLine = [[12.4372, 41.4234], [23.0164, 38.2671]]; // A-WALL single line closing the parts room
const northSite = [[13.3352, 52.7824], [55.6371, 40.1615]]; // north site wall centerline (already modeled, 2.5 m)

const uRear = unit(...rearDiv), uEast = unit(...eastDiv), uNW = unit(...nwWall), uFront = unit(...bayFront);
const inward = (u, k) => [-u[1] * k, u[0] * k]; // left normal

// shed corners: rear wall 20 cm inside the zone edge, east wall 20 cm inside, front through the NW wall end
const rearA = add(rearDiv[0], inward(uRear, -0.2)), eastA = add(eastDiv[0], inward(uEast, -0.2));
const A = cross(nwWall[0], uNW, rearA, uRear);
const B = cross(rearA, uRear, eastA, uEast);
const D = nwWall[0];
const C = cross(D, uFront, eastA, uEast);
const frontCols = bayCorners.map((p) => cross(D, uFront, p, [-uFront[1], uFront[0]])); // the east corner is the wall itself

// parts room: south wall along the single line from the showroom east wall face to the NW wall,
// rear wall 20 cm inside the north site wall from the showroom to the NW wall top
const P1 = cross(partsLine[0], unit(...partsLine), [13.0, 30], [0, 1]);
const P2 = cross(partsLine[0], unit(...partsLine), nwWall[0], uNW);
const northIn = add(northSite[0], inward(unit(...northSite), -0.2));
const P3 = cross(northIn, unit(...northSite), [13.0, 30], [0, 1]);
const P4 = cross(northIn, unit(...northSite), nwWall[0], uNW);

const base = -0.45, top = 6.0;
const walls = [
  {role: 'oficina-noroeste', a: D, b: A, type: '.ALVENARIA_25CM'},
  {role: 'oficina-fundos', a: A, b: B, type: '.ALVENARIA_25CM'},
  {role: 'oficina-leste', a: B, b: C, type: '.ALVENARIA_25CM'},
  {role: 'oficina-testeira-acm', a: add(D, uFront, 0.125), b: add(C, uFront, -0.125), type: '.ACABAMENTO_ACM', base: 4.5, top: 6.0},
  {role: 'pecas-sul', a: P1, b: P2, type: '.ALVENARIA_15CM'},
  {role: 'pecas-fundos', a: P3, b: P4, type: '.ALVENARIA_25CM'},
];
const steps = walls.map((w) => ({command: 'create_wall', params: {...doc, levelName: 'N1-AC', start: pt(w.a), end: pt(w.b),
  wallTypeName: w.type, baseOffset: w.base ?? base, height: (w.top ?? top) - (w.base ?? base)}}));

console.log('shed A B C D', [A, B, C, D].map((p) => p.map(r4)));
console.log('parts P1 P2 P3 P4', [P1, P2, P3, P4].map((p) => p.map(r4)));
const wEnv = await callRevitBatch(steps, true, !commit);
if (!wEnv.ok || (commit && wEnv.committed === false)) { console.error('walls:', JSON.stringify(wEnv.error ?? wEnv.failures ?? wEnv, null, 1).slice(0, 2500)); process.exit(1); }
const wallIds = (wEnv.results ?? []).map((r) => r?.data?.id);
console.log(`walls ${commit ? 'committed' : 'dry'}: ${wallIds.length}`);

// yard level for the shed floor
const levels = (await callRevit('list_levels', doc)).data.levels;
if (!levels.find((l) => l.name === 'N0-PATIO')) {
  const r = await callRevit('create_level', {...doc, name: 'N0-PATIO', elevation: -0.45}, !commit);
  console.log('level N0-PATIO', r.ok);
}

const out = {walls: walls.map((w, i) => ({role: w.role, elementId: wallIds[i]})), columns: [], floors: []};
const angle = Math.atan2(uFront[1], uFront[0]) * 180 / Math.PI;
for (const c of frontCols) {
  const env = await callRevit('create_column', {...doc, location: pt(c), levelName: 'N1-AC',
    familyName: 'OC_Concreto-Retangular-Coluna', familyTypeName: '25 x 45 cm'}, !commit);
  if (!env.ok) { console.error('column', JSON.stringify(env.error)); process.exit(1); }
  if (!commit) continue;
  const id = env.data.id;
  const cob = (await callRevit('list_levels', doc)).data.levels.find((l) => l.name === 'N1-COB');
  for (const [name, value] of [['Nível superior', {id: cob.id}], ['Deslocamento superior', 0], ['Deslocamento da base', -0.45]])
    await callRevit('set_parameter', {...doc, id, parameterName: name, value, units: typeof value === 'number' ? 'meters' : undefined});
  await callRevit('rotate_element', {...doc, id, center: pt(c), angleDeg: angle - 90});
  out.columns.push({at: c.map(r4), elementId: id});
}
console.log(`columns ${commit ? 'committed' : 'dry'}: ${frontCols.length}`);

if (commit) {
  const inset = (poly, k) => { // shrink a convex CCW/CW quad by k along each edge normal (approximate via centroid pull)
    const cx = poly.reduce((s, p) => s + p[0], 0) / poly.length, cy = poly.reduce((s, p) => s + p[1], 0) / poly.length;
    return poly.map(([x, y]) => { const d = Math.hypot(x - cx, y - cy); return [x + (cx - x) * k / d, y + (cy - y) * k / d]; });
  };
  const shed = [D, C, B, A], parts = [P1, P2, P4, P3];
  const floors = [
    {role: 'oficina-piso', poly: shed, level: 'N0-PATIO', type: '.ESTRUTURA_Laje 10cm'},
    {role: 'oficina-cobertura', poly: inset(shed, 0.3), level: 'N1-COB-LAJE', type: '.ESTRUTURA_Laje 10cm'},
    {role: 'pecas-piso', poly: parts, level: 'N0-PATIO', type: '.ESTRUTURA_Laje 10cm'},
    {role: 'pecas-cobertura', poly: inset(parts, 0.3), level: 'N1-COB-LAJE', type: '.ESTRUTURA_Laje 10cm'},
  ];
  for (const f of floors) {
    const env = await callRevit('create_floor', {...doc, profile: f.poly.map(pt), levelName: f.level, floorTypeName: f.type});
    console.log(`floor ${f.role}: ${env.ok ? env.data.id : JSON.stringify(env.error ?? env.failures).slice(0, 400)}`);
    out.floors.push({role: f.role, elementId: env.data?.id});
  }
  created.servico = out;
  writeFileSync(new URL('../../.cad-build/created.json', import.meta.url), JSON.stringify(created, null, 1));
}
