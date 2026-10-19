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

const wall = (a, b) => ({
  command: 'create_wall',
  params: {
    start: { x: a[0], y: a[1], z: 0 },
    end: { x: b[0], y: b[1], z: 0 },
    height: 3.35,
    baseOffset: 0,
    levelName: 'N1-QUINTAL',
    wallTypeName: '.ALVENARIA_15CM',
    locationLine: 'centerline',
    structural: false,
    units: 'meters',
  },
});

// Divisas laterais paralelas às faces externas do edifício (largura 9,70 m).
// Os trechos existentes terminam aproximadamente em x=17,10; o KMZ define o fundo.
const wallSteps = [
  wall([17.10, 5.595], [37.395, 5.595]),
  wall([17.00, -3.955], [34.285, -3.955]),
  wall([37.395, 5.595], [34.285, -3.955]),
  wall([-7.837, -3.955], [-4.50, -3.955]),
];

const roadLines = [
  [[2.429, 31.575], [-16.471, -23.377]],
  [[-4.521, 38.419], [-26.321, -23.282]],
];
const xAtY = (line, y) => {
  const [[x1, y1], [x2, y2]] = line;
  return x1 + ((y - y1) * (x2 - x1)) / (y2 - y1);
};
const curbProfile = (line, y0, y1, width) => {
  const a = [xAtY(line, y0), y0];
  const b = [xAtY(line, y1), y1];
  const dx = b[0] - a[0], dy = b[1] - a[1], len = Math.hypot(dx, dy);
  const ox = -dy / len * width / 2, oy = dx / len * width / 2;
  return [
    { x: a[0] + ox, y: a[1] + oy, z: 0 },
    { x: b[0] + ox, y: b[1] + oy, z: 0 },
    { x: b[0] - ox, y: b[1] - oy, z: 0 },
    { x: a[0] - ox, y: a[1] - oy, z: 0 },
  ];
};
const curb = (line, name) => ({
  command: 'create_direct_shape',
  params: {
    name,
    category: 'OST_GenericModel',
    materialName: '.ESTRUTURA_Concreto Moldado no Local sem Formas',
    solids: [{ type: 'extrusion', profile: curbProfile(line, -14, 16, 0.15), zMin: -1.00, zMax: -0.85 }],
  },
});

const steps = [
  ...wallSteps,
  curb(roadLines[0], 'MEIO-FIO PRÓXIMO - IMPLANTAÇÃO KMZ'),
  curb(roadLines[1], 'MEIO-FIO OPOSTO - IMPLANTAÇÃO KMZ'),
];
const preview = must(await batch(steps, true), 'prévia dos muros e meios-fios');
if (preview.committed !== false) throw new Error('Prévia não informou committed:false.');
console.log(`Prévia válida: ${steps.length} operações.`);
if (!commit) process.exit(0);

const applied = must(await batch(steps, false), 'modelar muros e meios-fios');
if (applied.committed === false) throw new Error('Muros e meios-fios não foram confirmados.');
const ids = applied.results.map(row => row.data?.id).filter(Number.isInteger);
if (ids.length !== steps.length) throw new Error(`IDs criados inesperados: ${JSON.stringify(ids)}`);
must(await batch(ids.map(id => ({
  command: 'set_parameter', params: { id, parameterName: 'PHASE_CREATED', value: 12589 },
})), false), 'atribuir fase Existente aos muros e meios-fios');

for (const [viewId, name] of [[VIEW, 'implantacao-kmz-muros'], [SHEET, 'lev05-kmz-muros']]) {
  const capture = must(await call('get_view_image', { viewId, pixelSize: 2800 }), `capturar ${name}`).data;
  writeFileSync(`E:/lidar/trabalho/${name}.png`, Buffer.from(capture.imageBase64, 'base64'));
}
must(await call('save_document'), 'salvar muros KMZ');
console.log(JSON.stringify({ ids }, null, 2));
