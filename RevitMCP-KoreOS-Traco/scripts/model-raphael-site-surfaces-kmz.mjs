#!/usr/bin/env node
import { writeFileSync } from 'node:fs';
import { callRevit as rawCall, callRevitBatch as rawBatch } from '../src/McpServer/dist/revitClient.js';

const DOC = process.argv[2] ?? 'Lev-01-260916';
const commit = process.argv.includes('--commit');
const VIEW = 18670676;
const SHEET = 19429810;
const VIEWPORT = 19429816;
const call = (command, params = {}, dryRun = false) => rawCall(command, { documentTitle: DOC, ...params }, dryRun);
const batch = (steps, dryRun) => rawBatch(
  steps.map(step => ({ ...step, params: { documentTitle: DOC, ...step.params } })), true, dryRun,
);
const must = (result, label) => {
  if (!result.ok) throw new Error(`${label}: ${JSON.stringify(result.error ?? result.failures ?? result)}`);
  if (result.document && result.document !== DOC) throw new Error(`${label}: documento inesperado ${result.document}`);
  return result;
};

const roadLines = [
  [[4.519, 31.052], [-14.671, -23.903]], // alinhamento do lote / fundo do passeio
  [[2.429, 31.575], [-16.471, -23.377]], // meio-fio próximo
  [[-4.521, 38.419], [-26.321, -23.282]], // meio-fio oposto
  [[-6.281, 39.989], [-27.241, -20.746]], // fundo do passeio oposto
];
const xAtY = (line, y) => {
  const [[x1, y1], [x2, y2]] = line;
  return x1 + ((y - y1) * (x2 - x1)) / (y2 - y1);
};
const strip = (right, left, y0, y1) => [
  { x: xAtY(right, y0), y: y0, z: 0 },
  { x: xAtY(right, y1), y: y1, z: 0 },
  { x: xAtY(left, y1), y: y1, z: 0 },
  { x: xAtY(left, y0), y: y0, z: 0 },
];
const interpolateLine = (a, b, t) => [
  [a[0][0] + (b[0][0] - a[0][0]) * t, a[0][1] + (b[0][1] - a[0][1]) * t],
  [a[1][0] + (b[1][0] - a[1][0]) * t, a[1][1] + (b[1][1] - a[1][1]) * t],
];
const plantingEdge = interpolateLine(roadLines[0], roadLines[1], 0.78);

const old = [19413528, 19413674, 19413682, 19413690, 19413697, 19413706, 19413711, 19413713];
for (const id of old) must(await call('get_element_info', { id }), `conferir elemento viário ${id}`);

const floor = (profile, floorTypeName, heightOffset = 0) => ({
  command: 'create_floor',
  params: {
    profile,
    levelName: 'N1-QUINTAL',
    floorTypeName,
    heightOffset,
    structural: false,
    units: 'meters',
    snapNearAxisDegrees: 0,
  },
});

const steps = [
  { command: 'delete_elements', params: { ids: old } },
  floor(strip(roadLines[0], plantingEdge, -14, 16), 'PISO  5 cm'),
  floor(strip(plantingEdge, roadLines[1], -14, -3.93), '.TERRENO_Gramado', -0.03),
  floor(strip(plantingEdge, roadLines[1], -3.93, 5.67), 'PISO  5 cm'),
  floor(strip(plantingEdge, roadLines[1], 5.67, 16), '.TERRENO_Gramado', -0.03),
  floor(strip(roadLines[1], roadLines[2], -14, 16), '.TERRENO_Asfalto 15cm - LEVANTAMENTO', -0.15),
  floor(strip(roadLines[2], roadLines[3], -14, 16), 'PISO  5 cm'),
  // O lote ganhou 10 m de profundidade na vista. O deslocamento mantém a implantação
  // fora das duas tabelas nativas de esquadrias na LEV-05.
  { command: 'move_element', params: { id: VIEWPORT, translation: { x: -0.02, y: 0, z: 0 }, units: 'meters' } },
];

const preview = must(await batch(steps, true), 'prévia das superfícies KMZ');
if (preview.committed !== false) throw new Error('Prévia das superfícies não informou committed:false.');
console.log(`Prévia válida: ${steps.length} operações.`);
if (!commit) process.exit(0);

const applied = must(await batch(steps, false), 'modelar superfícies KMZ');
if (applied.committed === false) throw new Error('A modelagem das superfícies não foi confirmada.');
const newFloorIds = applied.results.slice(1, 7).map(row => row.data?.id).filter(Number.isInteger);
if (newFloorIds.length !== 6) throw new Error(`IDs de pisos inesperados: ${JSON.stringify(newFloorIds)}`);

const phaseSteps = newFloorIds.map(id => ({
  command: 'set_parameter', params: { id, parameterName: 'PHASE_CREATED', value: 12589 },
}));
must(await batch(phaseSteps, false), 'atribuir fase Existente às superfícies');

for (const [viewId, name] of [[VIEW, 'implantacao-kmz-superficies'], [SHEET, 'lev05-kmz-superficies']]) {
  const capture = must(await call('get_view_image', { viewId, pixelSize: 2800 }), `capturar ${name}`).data;
  writeFileSync(`E:/lidar/trabalho/${name}.png`, Buffer.from(capture.imageBase64, 'base64'));
}
must(await call('save_document'), 'salvar superfícies KMZ');
console.log(JSON.stringify({ newFloorIds }, null, 2));
