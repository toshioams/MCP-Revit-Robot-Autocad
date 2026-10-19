// After Revit restart: node scripts/prepare-railing-hatch.mjs [--apply-material]
// Creates NO panels: first exports actual rail boundaries for measured panel placement.
import {callRevit,checkRevitHealth} from '../src/McpServer/dist/revitClient.js';
import{mkdirSync,writeFileSync}from'node:fs';
const documentTitle='projeto Raphael';
const out='E:/lidar/trabalho/guardas-hachura';mkdirSync(out,{recursive:true});
const health=await checkRevitHealth();if(health.version!=='0.8.61')throw Error('Reinicie com 0.8.61. Carregada: '+health.version);
async function call(command,params,dry=false){const r=await callRevit(command,{...params,documentTitle},dry);if(!r.ok||r.document!==documentTitle||(dry&&r.committed!==false))throw Error(JSON.stringify(r));return r;}
const material={name:'TELA METALICA HACHURA - LEVANTAMENTO',color:'#606060',graphicsTransparency:80,crosshatch:true,crosshatchSpacingMeters:.05,crosshatchAngleDegrees:45};
const list=await call('list_materials',{});let found=list.data.materials.find(m=>m.name===material.name);
if(!found){const dry=await call('create_material',material,true);writeFileSync(out+'/material-dry.json',JSON.stringify(dry,null,2));if(process.argv.includes('--apply-material')){const r=await call('create_material',material);writeFileSync(out+'/material-aplicado.json',JSON.stringify(r,null,2));found={id:r.data.materialId};}}
for(const id of [19395195,19395199,19395280,19395284,19395364,19395368]){
 const info=await call('get_element_info',{id});if(info.data.className!=='Railing'||info.data.typeId!==19396700)throw Error('Guarda-corpo mudou: '+id);
 const faces=[];for(let offset=0;;){const r=await call('inspect_planar_faces',{id,includeEdgeLoops:true,limit:500,offset});faces.push(...r.data.faces);if(!r.data.hasMore)break;offset+=r.data.faces.length;}
 writeFileSync(out+'/'+id+'.json',JSON.stringify({info:info.data,faces},null,2));
}
if(process.argv.includes('--apply-material'))await call('save_document',{});
console.log(JSON.stringify({materialId:found?.id,boundariesExported:out,note:'Conferir contornos reais antes de criar painéis; nenhuma tela criada por este script.'}));
