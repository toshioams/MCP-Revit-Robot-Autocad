#!/usr/bin/env node
import { writeFileSync } from 'node:fs';
import { callRevit as rawCall, callRevitBatch as rawBatch } from '../src/McpServer/dist/revitClient.js';

const DOC = process.argv[2] ?? 'Lev-01-260916';
const commit = process.argv.includes('--commit');
const VIEW = 18670676;
const SHEET = 19429810;
const VIEWPORT = 19429816;
const OLD_ANNOTATIONS = Array.from({ length: 19 }, (_, i) => 19429791 + i);
const call = (command, params = {}, dryRun = false) =>
  rawCall(command, { documentTitle: DOC, ...params }, dryRun);
const batch = (steps, dryRun) => rawBatch(
  steps.map(step => ({ ...step, params: { documentTitle: DOC, ...step.params } })),
  true,
  dryRun,
);
const must = (result, label) => {
  if (!result.ok) throw new Error(`${label}: ${JSON.stringify(result.error ?? result.failures ?? result)}`);
  if (result.document && result.document !== DOC) throw new Error(`${label}: documento inesperado ${result.document}`);
  return result;
};

// KMZ terreno.kmz, convertido para eixos locais:
// U = profundidade do lote; V = transversal. A origem e a largura transversal
// foram calibradas pelo contorno externo do edifício no levantamento Revit.
const lot = [
  [-4.291, 5.670],
  [37.395, 5.670],
  [34.285, -4.030],
  [-7.837, -4.030],
];

// Quatro linhas da via contidas no KMZ. Os pontos abaixo já estão transformados
// para os eixos do modelo; as linhas são recortadas à janela gráfica da vista.
const roadSource = [
  [[4.519, 31.052], [-14.671, -23.903]],
  [[2.429, 31.575], [-16.471, -23.377]],
  [[-4.521, 38.419], [-26.321, -23.282]],
  [[-6.281, 39.989], [-27.241, -20.746]],
];
const yMin = -10.2;
const yMax = 12.2;
const xAtY = (line, y) => {
  const [[x1, y1], [x2, y2]] = line;
  return x1 + ((y - y1) * (x2 - x1)) / (y2 - y1);
};
const clippedRoad = roadSource.map(line => [[xAtY(line, yMin), yMin], [xAtY(line, yMax), yMax]]);
const roadCenter = [
  [(xAtY(roadSource[1], yMin) + xAtY(roadSource[2], yMin)) / 2, yMin],
  [(xAtY(roadSource[1], yMax) + xAtY(roadSource[2], yMax)) / 2, yMax],
];

for (const id of OLD_ANNOTATIONS) {
  const info = must(await call('get_element_info', { id }), `conferir elemento ${id}`).data;
  if (!['DetailCurve', 'DetailLine', 'TextNote'].includes(info.className)) {
    throw new Error(`Elemento ${id} não é anotação prevista: ${info.className}`);
  }
}

const steps = [
  { command: 'delete_elements', params: { ids: OLD_ANNOTATIONS } },
  { command: 'set_view_crop', params: {
    viewId: VIEW,
    bboxMeters: { minX: -18.0, minY: yMin, maxX: 39.0, maxY: yMax },
    cropVisible: false,
  } },
];
const addLine = (a, b, lineStyle) => steps.push({
  command: 'create_detail_line',
  params: {
    viewId: VIEW,
    start: { x: a[0], y: a[1], z: 0 },
    end: { x: b[0], y: b[1], z: 0 },
    units: 'meters',
    ...(lineStyle ? { lineStyle } : {}),
  },
});
const dashed = '- LINHA DE PROJEÇÃO';
for (let i = 0; i < lot.length; i++) addLine(lot[i], lot[(i + 1) % lot.length], dashed);
for (const line of clippedRoad) addLine(line[0], line[1]);
addLine(roadCenter[0], roadCenter[1], dashed);

const addNote = (text, x, y, width = 0.055, rotationDegrees = 0, typeId = 17625865) => steps.push({
  command: 'create_text_note',
  params: { viewId: VIEW, text, location: { x, y, z: 0 }, width, rotationDegrees, units: 'meters', typeId },
});
addNote('AV. PRESIDENTE MARQUES', -12.2, -4.2, 0.075, 70.75, 361);
addNote('PASSEIO PÚBLICO', -6.35, -3.5, 0.045, 70.75);
addNote('EDIFICAÇÃO EXISTENTE', 5.2, 0.3, 0.060, 0, 361);
addNote('ÁREA EXTERNA / QUINTAL', 24.0, 0.4, 0.060);
addNote('LIMITE DO TERRENO — BASE KMZ FORNECIDA', 21.5, 6.35, 0.090);
addNote('DIVISAS SUJEITAS À CONFIRMAÇÃO CADASTRAL', 21.5, 5.98, 0.090);
addNote('IMPLANTAÇÃO MODELADA A PARTIR DO KMZ FORNECIDO', 16.0, -5.10, 0.095);

// Norte calculado a partir das coordenadas geográficas do KMZ e dos eixos locais.
const arrowTail = [29.0, -8.2];
const arrowHead = [26.52, -3.85];
addLine(arrowTail, arrowHead);
addLine(arrowHead, [26.48, -4.92]);
addLine(arrowHead, [27.42, -4.37]);
addNote('N', 26.18, -3.48, 0.010, 0, 361);

const preview = must(await batch(steps, true), 'prévia da correção KMZ');
if (preview.committed !== false) throw new Error('A prévia KMZ não informou committed:false.');
console.log(`Prévia válida: ${steps.length} operações.`);

if (!commit) process.exit(0);

const applied = must(await batch(steps, false), 'aplicar correção KMZ');
if (applied.committed === false) throw new Error('A correção KMZ não foi confirmada.');

for (const [viewId, name] of [[VIEW, 'implantacao-kmz-vista'], [SHEET, 'lev05-kmz-prancha']]) {
  const capture = must(await call('get_view_image', { viewId, pixelSize: 2800 }), `capturar ${name}`).data;
  writeFileSync(`E:/lidar/trabalho/${name}.png`, Buffer.from(capture.imageBase64, 'base64'));
}
must(await call('save_document'), 'salvar documento');
console.log(JSON.stringify({ document: DOC, viewId: VIEW, sheetId: SHEET, viewportId: VIEWPORT }, null, 2));
