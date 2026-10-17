import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { createWorker } from './download.mjs';

const chunks = [Buffer.from('PK\x03\x04first'), Buffer.from('second'), Buffer.from('last')];
const bytes = Buffer.concat(chunks);
const file = { name: 'Jemur-0.1.0-win-x64.zip', variant: 'win-x64', size: bytes.length,
  sha256: createHash('sha256').update(bytes).digest('hex'),
  chunks: chunks.map((c, i) => ({ path: `/_chunks/test.${i}`, size: c.length })) };
const worker = createWorker({ version: '0.1.0', commit: 'a'.repeat(40), files: [file] });
const request = (path, options) => new Request(`https://jemur.example${path}`, options);
const env = { ASSETS: { fetch: async url => {
  const index = file.chunks.findIndex(c => c.path === new URL(url).pathname);
  return new Response(chunks[index], { headers: { 'Content-Length': String(chunks[index].length) } });
} } };

test('streams every chunk in order as one download with its checksum', async () => {
  const response = await worker.fetch(request(`/downloads/${file.name}`), env);
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('Content-Length'), String(bytes.length));
  const downloaded = Buffer.from(await response.arrayBuffer());
  assert.deepEqual(downloaded, bytes);
  assert.equal(response.headers.get('ETag'), `"${createHash('sha256').update(downloaded).digest('hex')}"`);
});
test('HEAD and conditional requests do not read assets', async () => {
  const unavailable = { ASSETS: { fetch() { throw new Error('must not fetch'); } } };
  const head = await worker.fetch(request(`/downloads/${file.name}`, { method: 'HEAD' }), unavailable);
  assert.equal(head.status, 200);
  assert.equal(await head.text(), '');
  const cached = await worker.fetch(request(`/downloads/${file.name}`, { headers: { 'If-None-Match': `"${file.sha256}"` } }), unavailable);
  assert.equal(cached.status, 304);
});
test('missing or truncated chunks fail before sending a ZIP', async () => {
  for (const makeAsset of [() => new Response('missing', { status: 404 }), () => new Response('short', { headers: { 'Content-Length': '5' } })]) {
    const response = await worker.fetch(request(`/downloads/${file.name}`), { ASSETS: { fetch: async () => makeAsset() } });
    assert.equal(response.status, 503);
  }
});
test('only named downloads are public and latest redirects to the current version', async () => {
  assert.equal((await worker.fetch(request('/_chunks/test.0'), env)).status, 404);
  assert.equal((await worker.fetch(request('/downloads/unknown.zip'), env)).status, 404);
  assert.equal((await worker.fetch(request('/'), env)).status, 200);
  const latest = await worker.fetch(request('/latest/win-x64'), env);
  assert.equal(latest.status, 302);
  assert.equal(latest.headers.get('Location'), `/downloads/${file.name}`);
  const manifest = await (await worker.fetch(request('/manifest.json'), env)).json();
  assert.equal(manifest.files[0].url, `https://jemur.example/downloads/${file.name}`);
  assert.equal(manifest.files[0].chunks, undefined);
  assert.equal((await worker.fetch(request('/', { method: 'POST' }), env)).status, 405);
});
