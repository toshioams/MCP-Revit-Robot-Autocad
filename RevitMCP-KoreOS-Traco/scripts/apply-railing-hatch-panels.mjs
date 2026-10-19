// Lightweight crosshatched infill for the six rear-stair railings.
// Reads measured rail face boundaries exported by prepare-railing-hatch.mjs.
// Usage: node scripts/apply-railing-hatch-panels.mjs [--commit]
import {callRevit} from '../src/McpServer/dist/revitClient.js';
import {readFileSync, writeFileSync} from 'node:fs';

const commit = process.argv.includes('--commit');
const documentTitle = 'projeto Raphael';
const source = 'E:/lidar/trabalho/guardas-hachura';
const ids = [19395195,19395199,19395280,19395284,19395364,19395368];
const materialName = 'TELA METALICA HACHURA - LEVANTAMENTO';
const materialId = 19397066;
const ft = 0.3048;
const D = (a,b) => Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
const add = (a,b,s=1) => ({x:a.x+b.x*s,y:a.y+b.y*s,z:a.z+b.z*s});
const sub = (a,b) => ({x:a.x-b.x,y:a.y-b.y,z:a.z-b.z});
const mul = (a,s) => ({x:a.x*s,y:a.y*s,z:a.z*s});
const avg = (a,b) => mul(add(a,b),.5);
const unit = a => mul(a,1/Math.hypot(a.x,a.y,a.z));

async function call(command,params,dry=false) {
  const r=await callRevit(command,{...params,documentTitle},dry);
  if(!r.ok || r.document!==documentTitle || (dry && r.committed!==false)) throw Error(JSON.stringify(r));
  return r;
}

function axes(json) {
  const candidates=[];
  for(const f of json.faces.filter(f=>f.materialId===358247 && f.areaSquareMeters>.006)) {
    const edges=f.edgeLoops.flat().map(e=>e.pointsMeters).filter(p=>p?.length>=2)
      .map(p=>({a:p[0],b:p[p.length-1],l:D(p[0],p[p.length-1])})).sort((a,b)=>b.l-a.l);
    if(edges.length<2 || edges[0].l<.15 || edges[1].l<.15 || Math.abs(edges[0].l-edges[1].l)>.03) continue;
    let [a1,b1,a2,b2]=[edges[0].a,edges[0].b,edges[1].a,edges[1].b];
    if(D(a1,a2)+D(b1,b2)>D(a1,b2)+D(b1,a2)) [a2,b2]=[b2,a2];
    let a=avg(a1,a2), b=avg(b1,b2);
    if(Math.hypot(a.x-b.x,a.y-b.y)<.12) continue; // posts
    const key=p=>`${p.x.toFixed(4)},${p.y.toFixed(4)},${p.z.toFixed(4)}`;
    if(key(a)>key(b)) [a,b]=[b,a];
    candidates.push({a,b});
  }
  const groups=[];
  for(const c of candidates) {
    let g=groups.find(g=>Math.min(D(c.a,g[0].a)+D(c.b,g[0].b),D(c.a,g[0].b)+D(c.b,g[0].a))<.14);
    if(g) g.push(c); else groups.push([c]);
  }
  return groups.map(g=>({
    a:g.reduce((q,c)=>add(q,c.a),{x:0,y:0,z:0}),
    b:g.reduce((q,c)=>add(q,c.b),{x:0,y:0,z:0}), n:g.length
  })).map(c=>({a:mul(c.a,1/c.n),b:mul(c.b,1/c.n)}));
}

function panels(json, label) {
  const lines=axes(json), used=new Set(), solids=[], pairs=[];
  // Pair globally by the exact 0.90 m vertical repetition. Greedy iteration over
  // unsorted faces can consume a rail with the wrong neighbour at stair turns.
  for(let i=0;i<lines.length;i++) for(let j=i+1;j<lines.length;j++) for(const rev of [false,true]) {
    const l=lines[i],u=lines[j],[ua,ub]=rev?[u.b,u.a]:[u.a,u.b];
    const xy=Math.hypot(l.a.x-ua.x,l.a.y-ua.y)+Math.hypot(l.b.x-ub.x,l.b.y-ub.y);
    const dz=Math.abs(Math.abs(ua.z-l.a.z)-.9)+Math.abs(Math.abs(ub.z-l.b.z)-.9);
    if(xy+dz<.08) pairs.push({i,j,rev,score:xy+dz});
  }
  pairs.sort((a,b)=>a.score-b.score);
  for(const pair of pairs) {
    if(used.has(pair.i)||used.has(pair.j)) continue;
    let lo=lines[pair.i], hi=lines[pair.j]; if(pair.rev) hi={a:hi.b,b:hi.a};
    if((hi.a.z+hi.b.z)<(lo.a.z+lo.b.z)) [lo,hi]=[hi,lo];
    used.add(pair.i);used.add(pair.j);
    const tangent=unit(sub(lo.b,lo.a)), inset=Math.min(.03,D(lo.a,lo.b)*.12);
    const la=add(add(lo.a,tangent,inset),{x:0,y:0,z:.035});
    const lb=add(add(lo.b,tangent,-inset),{x:0,y:0,z:.035});
    const ha=add(add(hi.a,tangent,inset),{x:0,y:0,z:-.035});
    const hb=add(add(hi.b,tangent,-inset),{x:0,y:0,z:-.035});
    if(D(la,lb)>.12) solids.push({type:'panel',profile:[la,lb,hb,ha],thicknessMeters:.003});
  }
  if(process.env.RAILING_PANEL_DEBUG) console.error(JSON.stringify({label,axes:lines.length,pairCandidates:pairs.length,used:[...used],unmatched:lines.map((x,i)=>({i,...x})).filter((_,i)=>!used.has(i))}));
  return solids;
}

const material=await call('list_materials',{});
if(!material.data.materials.some(m=>m.id===materialId && m.name===materialName)) throw Error('Material esperado não encontrado.');
const plan=[];
for(const id of ids) {
  const live=await call('get_element_info',{id});
  if(live.data.className!=='Railing'||live.data.typeId!==19396700||live.data.parameters.find(p=>p.name==='Fase criada')?.valueString!=='Existente')
    throw Error(`Guarda-corpo ${id} mudou; revisar antes de aplicar.`);
  const json=JSON.parse(readFileSync(`${source}/${id}.json`));
  const solids=panels(json,id);
  if(solids.length<1) throw Error(`Nenhum painel derivado para ${id}.`);
  plan.push({id,solids});
}
writeFileSync(`${source}/panel-plan.json`,JSON.stringify(plan,null,2));
for(const item of plan) {
  const params={name:`TELA HACHURADA GUARDA-CORPO ${item.id}`,category:'OST_GenericModel',materialName,solids:item.solids};
  const dry=await call('create_direct_shape',params,true);
  if(!commit) { console.log(JSON.stringify({railingId:item.id,panels:item.solids.length,dryRun:dry.data})); continue; }
  const created=await call('create_direct_shape',params);
  await call('set_parameter',{id:created.data.id,parameterName:'PHASE_CREATED',value:'Existente'},true);
  await call('set_parameter',{id:created.data.id,parameterName:'PHASE_CREATED',value:'Existente'});
  console.log(JSON.stringify({railingId:item.id,elementId:created.data.id,panels:item.solids.length}));
}
if(commit) await call('save_document',{});
