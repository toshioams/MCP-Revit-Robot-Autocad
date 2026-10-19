// Live smoke test: only an explicitly named disposable copy is accepted.
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { randomUUID } from 'node:crypto';

const testPath = resolve('release/live-idempotency-20260915/MCP Idempotency Validation.rvt');
const documentTitle = 'MCP Idempotency Validation';
const base = 'http://127.0.0.1:7891';
const token = readFileSync(join(process.env.APPDATA, 'Autodesk/Revit/Addins/2026/revit-mcp-token.txt'), 'utf8').trim();
const headers = { 'content-type': 'application/json', authorization: `Bearer ${token}` };
const evidence = { started: new Date().toISOString(), testPath, checks: [] };
const report = resolve('release/live-idempotency-20260915/evidence.json');
const record = (name, result) => { evidence.checks.push({ name, result }); writeFileSync(report, JSON.stringify(evidence, null, 2)); console.log(name); };
async function request(path, body) {
  const res = await fetch(base + path, { headers, ...(body ? { method: 'POST', body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(30000) });
  return { status: res.status, value: await res.json() };
}
const target = { documentTitle, documentPath: testPath };
const post = async (command, params = {}, extra = {}) => {
  const value = (await request('/mcp', { command, params: { ...params, ...target }, ...extra })).value;
  if (value.ok) assert.equal(value.document, documentTitle);
  return value;
};
const requireOk = (r) => { assert.equal(r.ok, true, JSON.stringify(r)); return r; };
const levels = async () => requireOk(await post('list_levels'));

try {
  const health = (await request('/health')).value;
  assert.equal(health.version, '0.8.65');
  assert.equal(health.idempotencySupported, true);
  evidence.health = health;
  const docs = requireOk(await post('list_open_documents'));
  const match = docs.data.documents.find(d => d.title === documentTitle);
  assert.ok(match && resolve(match.pathName) === testPath && !match.isWorkshared, 'Disposable non-workshared test copy must be open');
  record('document-confirmed', docs);
  const before = await levels(); record('levels-before', before);
  const key = (suffix) => `${health.sessionId}:${suffix}-${randomUUID()}`;
  const name = `MCP-IDEMP-${Date.now()}`;
  const previewKey = key('preview');
  const params = { name, elevation: 100, units: 'meters' };
  const preview = requireOk(await post('create_level', params, { dryRun: true, idempotencyKey: previewKey }));
  assert.equal(preview.committed, false);
  const previewReplay = requireOk(await post('create_level', params, { dryRun: true, idempotencyKey: previewKey }));
  assert.equal(previewReplay.committed, false); assert.equal(previewReplay.operation.replayed, true);
  record('preview-and-replay', { preview, previewReplay });
  const conflict = await post('create_level', params, { idempotencyKey: previewKey });
  assert.equal(conflict.error?.code, 'idempotency_conflict'); record('preview-commit-conflict', conflict);
  const commitKey = key('commit');
  const concurrent = await Promise.all([post('create_level', params, { idempotencyKey: commitKey }), post('create_level', params, { idempotencyKey: commitKey })]);
  concurrent.forEach(requireOk);
  assert.equal(concurrent[0].data.id, concurrent[1].data.id);
  assert.equal(concurrent.filter(r => r.operation.replayed).length, 1);
  record('concurrent-single-commit', concurrent);
  const status = requireOk((await request('/operations?key=' + encodeURIComponent(commitKey))).value);
  assert.equal(status.data.state, 'completed'); record('operation-status', status);
  const foreign = await post('create_level', params, { idempotencyKey: `${'0'.repeat(32)}:foreign` });
  assert.equal(foreign.error?.code, 'session_mismatch'); record('foreign-session-refused', foreign);
  const missing = (await request('/mcp', { command: 'create_level', params, idempotencyKey: key('no-target') })).value;
  assert.equal(missing.error?.code, 'bad_request'); record('implicit-document-refused', missing);
  const batchSteps = [101, 102].map((elevation, i) => ({ command: 'create_level', params: { ...target, name: `${name}-batch-${i}`, elevation } }));
  const batchPreview = requireOk((await request('/mcp/batch', { steps: batchSteps, stopOnError: true, dryRun: true, idempotencyKey: key('batch-preview') })).value);
  assert.equal(batchPreview.committed, false); record('batch-preview', batchPreview);
  const batchBody = { steps: batchSteps, stopOnError: true, idempotencyKey: key('batch-commit') };
  // Intentionally discard the original result. Recovery must use the same key,
  // even though the caller has not consumed any result body.
  const discarded = await fetch(base + '/mcp/batch', { method: 'POST', headers, body: JSON.stringify(batchBody), signal: AbortSignal.timeout(30000) });
  await discarded.body?.cancel();
  const recovered = requireOk((await request('/operations?key=' + encodeURIComponent(batchBody.idempotencyKey))).value);
  assert.equal(recovered.data.state, 'completed'); requireOk(recovered.data.result);
  const replay = requireOk((await request('/mcp/batch', batchBody)).value);
  assert.equal(replay.operation.replayed, true); record('discarded-response-batch-recovery', { recovered, replay });
  const after = await levels(); record('levels-after', after);
  const rows = Array.isArray(after.data) ? after.data : after.data.levels;
  assert.ok(Array.isArray(rows), 'Expected level inventory');
  const created = rows.filter(l => l.name?.startsWith(name));
  assert.equal(created.length, 3, 'Only one single level and two batch levels may exist');
  for (const element of created) {
    const info = requireOk(await post('get_element_info', { id: element.id }));
    record('created-element', info);
  }
  const ids = created.map(l => l.id);
  const deletion = requireOk(await post('delete_elements', { ids }, { dryRun: true }));
  assert.equal(deletion.committed, false); record('cleanup-preview', deletion);
  // Cleanup is intentionally not automatic: inspect dependent IDs in the recorded
  // preview before committing a separate deletion.
  evidence.createdIds = ids;
  evidence.status = 'passed-cleanup-pending';
  writeFileSync(report, JSON.stringify(evidence, null, 2));
} catch (error) {
  evidence.status = 'failed'; evidence.error = String(error);
  writeFileSync(report, JSON.stringify(evidence, null, 2));
  console.error(error); process.exitCode = 1;
}
