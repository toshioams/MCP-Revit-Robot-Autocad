// Read-only regression checks for this pilot. IDs are guarded by UniqueId.
import {callRevit} from '../src/McpServer/dist/revitClient.js';
const rules = [
 {id:19394316,uid:'74a2e574-7dbb-4068-a80f-a89bbd692d33-0127ef0c',parameter:'Elevação do nível',expected:0.9/0.3048},
 {id:19396967,uid:'74a2e574-7dbb-4068-a80f-a89bbd692d33-0127f967',parameter:'Largura',expected:2.35/0.3048},
 {id:19394314,rotation:Math.PI},
 {id:19394315,rotation:0},
];
// Family identity guards prevent applying pilot rotation rules to reused IDs.
const chairFamily='Cadeira MS Wood Elle';
const results=[];
for(const rule of rules) {
 const response=await callRevit('get_element_info',{id:rule.id});
 if(!response.ok) {results.push({id:rule.id,status:'unavailable',error:response.error});continue;}
 const e=response.data;
 if(rule.uid && e.uniqueId!==rule.uid) {results.push({id:rule.id,status:'identity_mismatch'});continue;}
 if(rule.rotation!==undefined) {
   // These chairs were created in this pilot session; require the known UID prefix too.
   if(!e.uniqueId?.startsWith('74a2e574-7dbb-4068-a80f-a89bbd692d33-') || e.familyPlacement?.familyName!==chairFamily) {
     results.push({id:rule.id,status:'identity_mismatch'});continue;
   }
   const actual=e.familyPlacement?.rotationRadians;
   const delta=typeof actual==='number'?Math.atan2(Math.sin(actual-rule.rotation),Math.cos(actual-rule.rotation)):NaN;
   results.push({id:rule.id,status:Math.abs(delta)<1e-6?'pass':'changed',actual,expected:rule.rotation});
 } else {
   const matches=e.parameters.filter(p=>p.name===rule.parameter);
   const actual=matches.length===1?matches[0].value:null;
   results.push({id:rule.id,status:typeof actual==='number'&&Math.abs(actual-rule.expected)<1e-6?'pass':'changed',parameter:rule.parameter,actualInternal:actual,expectedInternal:rule.expected});
 }
}
console.log(JSON.stringify({readOnly:true,scope:'Pilot regression only; not full geometric or visual validation',results},null,2));
if(results.some(r=>r.status!=='pass'))process.exitCode=1;
