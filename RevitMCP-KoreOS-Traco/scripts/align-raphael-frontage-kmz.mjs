#!/usr/bin/env node
import { writeFileSync } from 'node:fs';
import { callRevit as rawCall, callRevitBatch as rawBatch } from '../src/McpServer/dist/revitClient.js';

const DOC = process.argv[2] ?? 'Lev-01-260916';
const commit = process.argv.includes('--commit');
const VIEW = 18670676;
const SHEET = 19429810;
const call = (command, params = {}, dryRun = false) => rawCall(command, { documentTitle: DOC, ...params }, dryRun);
const batch = (steps, dryRun) => rawBatch(
  steps.map(step => ({ ...step, params: { documentTitle: DOC, ...step.params } })), true, dryRun,
);
const must = (result, label) => {
  if (!result.ok) throw new Error(`${label}: ${JSON.stringify(result.error ?? result.failures ?? result)}`);
  if (result.document && result.document !== DOC) throw new Error(`${label}: documento inesperado ${result.document}`);
  return result;
};

const roadProperty = [[4.519, 31.052], [-14.671, -23.903]];
const roadCurb = [[2.429, 31.575], [-16.471, -23.377]];
const xAtY = (line, y) => {
  const [[x1, y1], [x2, y2]] = line;
  return x1 + ((y - y1) * (x2 - x1)) / (y2 - y1);
};
const northY = 5.67;
const southY = -4.03;
const northFrontX = xAtY(roadProperty, northY);
const southFrontX = xAtY(roadProperty, southY);
const lot = [
  [northFrontX, northY],
  [37.395, northY],
  [34.285, southY],
  [southFrontX, southY],
];

const interpolateLine = (a, b, t) => [
  [a[0][0] + (b[0][0] - a[0][0]) * t, a[0][1] + (b[0][1] - a[0][1]) * t],
  [a[1][0] + (b[1][0] - a[1][0]) * t, a[1][1] + (b[1][1] - a[1][1]) * t],
];
const plantingEdge = interpolateLine(roadProperty, roadCurb, 0.78);
const strip = (right, left, y0, y1) => [
  { x: xAtY(right, y0), y: y0, z: 0 },
  { x: xAtY(right, y1), y: y1, z: 0 },
  { x: xAtY(left, y1), y: y1, z: 0 },
  { x: xAtY(left, y0), y: y0, z: 0 },
];
const floor = (profile, floorTypeName, heightOffset = 0) => ({
  command: 'create_floor',
  params: {
    profile, levelName: 'N1-QUINTAL', floorTypeName, heightOffset,
    structural: false, units: 'meters', snapNearAxisDegrees: 0,
  },
});
const wall = (a, b, levelName, height) => ({
  command: 'create_wall',
  params: {
    start: { x: a[0], y: a[1], z: 0 }, end: { x: b[0], y: b[1], z: 0 },
    levelName, wallTypeName: '.ALVENARIA_15CM', height, baseOffset: 0,
    locationLine: 'centerline', structural: false, units: 'meters',
  },
});
const line = (a, b) => ({
  command: 'create_detail_line',
  params: {
    viewId: VIEW,
    start: { x: a[0], y: a[1], z: 0 }, end: { x: b[0], y: b[1], z: 0 },
    units: 'meters', lineStyle: '- LINHA DE PROJEÇÃO',
  },
});
const curbProfile = (source, y0, y1, width = 0.15) => {
  const a = [xAtY(source, y0), y0], b = [xAtY(source, y1), y1];
  const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.hypot(dx, dy);
  const ox = -dy / len * width / 2, oy = dx / len * width / 2;
  return [
    { x: a[0] + ox, y: a[1] + oy, z: 0 }, { x: b[0] + ox, y: b[1] + oy, z: 0 },
    { x: b[0] - ox, y: b[1] - oy, z: 0 }, { x: a[0] - ox, y: a[1] - oy, z: 0 },
  ];
};
const curb = (y0, y1, name) => ({
  command: 'create_direct_shape',
  params: {
    name, category: 'OST_GenericModel',
    materialName: '.ESTRUTURA_Concreto Moldado no Local sem Formas',
    solids: [{ type: 'extrusion', profile: curbProfile(roadCurb, y0, y1), zMin: -1.00, zMax: -0.85 }],
  },
});

const deleteIds = [
  19429859, 19429860, 19429861, 19429862, // limite gráfico anterior
  19387945, 19390057, 19430076,             // muros frontais desalinhados
  19430022, 19430031, 19430038,             // faixa externa com acesso largo demais
  19430077,                                  // meio-fio contínuo no acesso
];
for (const id of deleteIds) must(await call('get_element_info', { id }), `conferir elemento ${id}`);

const connector = [
  { x: xAtY(roadProperty, -3.80), y: -3.80, z: 0 },
  { x: -4.50, y: -3.80, z: 0 },
  { x: -4.50, y: 3.00, z: 0 },
  { x: xAtY(roadProperty, 3.00), y: 3.00, z: 0 },
];

const steps = [
  { command: 'delete_elements', params: { ids: deleteIds } },
  ...lot.map((p, i) => line(p, lot[(i + 1) % lot.length])),
  wall([northFrontX, 5.595], [0.98, 5.595], 'N0-AC', 3.05),
  wall([southFrontX, -3.955], [0.98, -3.955], 'N1-QUINTAL', 3.35),
  floor(strip(plantingEdge, roadCurb, -14, -3.80), '.TERRENO_Gramado', -0.03),
  floor(strip(plantingEdge, roadCurb, -3.80, 3.00), 'PISO  5 cm'),
  floor(strip(plantingEdge, roadCurb, 3.00, 16), '.TERRENO_Gramado', -0.03),
  floor(connector, 'PISO  5 cm'),
  curb(-14, -3.80, 'MEIO-FIO PRÓXIMO INFERIOR - IMPLANTAÇÃO KMZ'),
  curb(3.00, 16, 'MEIO-FIO PRÓXIMO SUPERIOR - IMPLANTAÇÃO KMZ'),
];

const preview = must(await batch(steps, true), 'prévia do alinhamento da testada');
if (preview.committed !== false) throw new Error('Prévia não informou committed:false.');
console.log(`Prévia válida: ${steps.length} operações; testada ${southFrontX.toFixed(3)}..${northFrontX.toFixed(3)} m.`);
if (!commit) process.exit(0);

const applied = must(await batch(steps, false), 'alinhar calçadas, rampa, muros e divisa');
if (applied.committed === false) throw new Error('Alinhamento da testada não confirmado.');
const ids = applied.results.slice(1).map(row => row.data?.id).filter(Number.isInteger);
const modelIds = ids.filter(id => !applied.results.some(row => row.data?.id === id && row.command === 'create_detail_line'));
must(await batch(modelIds.map(id => ({
  command: 'set_parameter', params: { id, parameterName: 'PHASE_CREATED', value: 12589 },
})), false), 'atribuir fase Existente aos novos elementos');

for (const [viewId, name] of [[VIEW, 'implantacao-kmz-alinhada'], [SHEET, 'lev05-kmz-alinhada']]) {
  const capture = must(await call('get_view_image', { viewId, pixelSize: 2800 }), `capturar ${name}`).data;
  writeFileSync(`E:/lidar/trabalho/${name}.png`, Buffer.from(capture.imageBase64, 'base64'));
}
must(await call('save_document'), 'salvar alinhamento da testada');
console.log(JSON.stringify({ ids, modelIds, northFrontX, southFrontX }, null, 2));
