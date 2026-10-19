import { writeFileSync } from 'node:fs';
import { callRevit } from '../src/McpServer/dist/revitClient.js';

const DOC = process.argv[2] ?? 'Lev-01-260916';
const VIEW = 18670676;
const mm = value => value / 304.8;
const call = (command, parameters = {}, dryRun = false) =>
  callRevit(command, { documentTitle: DOC, ...parameters }, dryRun);
const must = (result, label) => {
  if (!result.ok) throw new Error(`${label}: ${JSON.stringify(result.error ?? result.failures)}`);
  if (result.document && result.document !== DOC)
    throw new Error(`${label}: documento inesperado ${result.document}`);
  return result.data;
};

// A base Google Maps serve apenas para orientação, largura aparente do lote e contexto viário.
// O desenho final abaixo usa elementos existentes do modelo e anotações vetoriais do Revit.
must(await call('set_parameter', { id: VIEW, parameterName: 'VIEW_TEMPLATE', value: -1 }), 'desanexar template');
must(await call('rename_element', { id: VIEW, name: 'AS-BUILT - PLANTA DE IMPLANTAÇÃO' }), 'renomear implantação');
must(await call('set_parameter', { id: VIEW, parameterName: 'VIEW_SCALE_PULLDOWN_METRIC', value: 100 }), 'escala implantação');
must(await call('set_view_crop', {
  viewId: VIEW,
  bboxMeters: { minX: -19.2, minY: -10.2, maxX: 29.0, maxY: 12.2 },
  cropVisible: false,
}), 'recorte implantação');

const cloudVisibility = await call('set_category_visibility', {
  viewIds: [VIEW],
  categories: ['OST_PointClouds'],
  hidden: true,
});
if (!cloudVisibility.ok) {
  must(await call('hide_elements_in_view', {
    viewId: VIEW,
    ids: [19386650, 19386894, 19386895, 19386896],
  }), 'ocultar nuvens na implantação');
}

const dashed = '- LINHA DE PROJEÇÃO';
const line = async (a, b, style = undefined) => must(await call('create_detail_line', {
  viewId: VIEW,
  start: { x: a[0], y: a[1], z: 0 },
  end: { x: b[0], y: b[1], z: 0 },
  units: 'meters',
  ...(style ? { lineStyle: style } : {}),
}), 'linha de implantação');

// Limite aparente do lote: frente alinhada ao fechamento existente e profundidade lida na base aérea.
const lot = [[-4.50, -4.20], [27.00, -4.20], [27.00, 6.00], [-4.50, 6.00]];
for (let i = 0; i < lot.length; i++) await line(lot[i], lot[(i + 1) % lot.length], dashed);

// Eixo viário e separação gráfica do passeio, todos coincidentes com os pisos já modelados.
await line([-11.15, -10.0], [-11.15, 12.0], dashed);
await line([-6.50, -10.0], [-6.50, 12.0]);
await line([-7.15, -10.0], [-7.15, 12.0]);
await line([-15.15, -10.0], [-15.15, 12.0]);
await line([-17.30, -10.0], [-17.30, 12.0]);

const note = async (text, x, y, width = 0.055, rotationDegrees = 0, typeId = 17625865) =>
  must(await call('create_text_note', {
    viewId: VIEW,
    text,
    location: { x, y, z: 0 },
    width,
    rotationDegrees,
    units: 'meters',
    typeId,
  }), `nota ${text}`);

await note('AV. PRESIDENTE MARQUES', -12.00, -4.0, 0.075, 90, 361);
await note('PASSEIO PÚBLICO', -6.10, -3.3, 0.045, 90);
await note('EDIFICAÇÃO EXISTENTE', 5.2, 0.3, 0.060, 0, 361);
await note('ÁREA EXTERNA / QUINTAL', 19.0, 0.4, 0.055);
await note('LIMITE APARENTE DO LOTE — BASE AÉREA GOOGLE MAPS', 14.0, 6.35, 0.100);
await note('DIMENSÕES E DIVISAS SUJEITAS À CONFIRMAÇÃO CADASTRAL', 14.0, 5.90, 0.100);

// Norte verdadeiro aproximado derivado da orientação da avenida na base aérea.
const p0 = [25.4, -7.8], p1 = [22.3, -5.8];
await line(p0, p1);
await line(p1, [22.75, -6.75]);
await line(p1, [23.35, -5.85]);
await note('N', 21.95, -5.45, 0.010, 0, 361);

const sheet = must(await call('create_sheet', {
  sheetNumber: 'LEV-05',
  sheetName: 'AS-BUILT - IMPLANTAÇÃO E QUADRO DE ESQUADRIAS',
  titleBlockName: 'MANÁ A1',
}), 'criar LEV-05');
const placement = must(await call('place_view_on_sheet', {
  sheetId: sheet.id,
  viewId: VIEW,
  location: { x: mm(85), y: mm(35) },
}), 'colocar implantação na LEV-05');
if (placement.viewportId) must(await call('change_element_type', {
  id: placement.viewportId,
  typeId: 4024876,
}), 'tipo do viewport');

for (const [viewId, name] of [[VIEW, 'implantacao-modelada-vista'], [sheet.id, 'lev05-implantacao-modelada']]) {
  const capture = must(await call('get_view_image', { viewId, pixelSize: 2800 }), `capturar ${name}`);
  writeFileSync(`E:/lidar/trabalho/${name}.png`, Buffer.from(capture.imageBase64, 'base64'));
}
must(await call('save_document'), 'salvar implantação modelada');
console.log(JSON.stringify({ document: DOC, viewId: VIEW, sheetId: sheet.id, viewportId: placement.viewportId }, null, 2));
