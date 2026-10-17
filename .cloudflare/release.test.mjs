import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { publishRelease } from './release.mjs';

const commit = 'a'.repeat(40);
const sha = data => createHash('sha256').update(data).digest('hex');
async function fixture(t) {
  const directory = await mkdtemp(join(tmpdir(), 'jemur-release-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const files = [];
  for (const variant of ['win-x64', 'win-x64-setup', 'win-arm64', 'win-arm64-setup']) {
    const data = Buffer.from(`MZ:${variant}`);
    const name = `Jemur-0.1.1-${variant}.exe`;
    await writeFile(join(directory, name), data);
    files.push({ name, variant, size: data.length, sha256: sha(data), runtimeIncluded: true });
  }
  const manifest = { version: '0.1.1', commit, files };
  await writeFile(join(directory, 'manifest.json'), JSON.stringify(manifest));
  await writeFile(join(directory, 'SHA256SUMS.txt'), files.map(f => `${f.sha256}  ${f.name}\n`).join(''));
  const calls = [], assets = [];
  let release;
  let tag;
  const api = async (method, path, options = {}) => {
    calls.push({ method, path, ...options });
    if (method === 'GET' && path.includes('/releases?')) return release ? [release] : [];
    if (method === 'GET' && path.includes('/git/ref/tags/')) return tag ?? null;
    if (method === 'POST' && path.endsWith('/git/refs')) { tag = { object: { sha: options.json.sha, type: 'commit' } }; return tag; }
    if (method === 'POST' && path.endsWith('/releases')) {
      release = { id: 12, ...options.json, assets, upload_url: 'https://uploads.github.com/repos/revanapriyandi/jemur/releases/12/assets{?name,label}' };
      return release;
    }
    if (method === 'GET' && path.includes('/releases/12/assets')) return assets;
    if (method === 'POST' && path.startsWith('https://uploads.github.com/')) {
      const name = new URL(path).searchParams.get('name');
      const chunks = [];
      for await (const chunk of options.body) chunks.push(chunk);
      const data = Buffer.concat(chunks);
      const asset = { id: assets.length + 1, name, state: 'uploaded', size: data.length, digest: `sha256:${sha(data)}` };
      assets.push(asset);
      return asset;
    }
    if (method === 'PATCH' && path.endsWith('/releases/12')) {
      Object.assign(release, options.json);
      return release;
    }
    throw new Error(`Unexpected API call: ${method} ${path}`);
  };
  return { directory, manifest, calls, assets, api,
    setRelease(value) { release = value; }, setTag(value) { tag = value; } };
}

test('creates an exact commit tag and publishes only after all six asset digests match', async t => {
  const f = await fixture(t);
  const result = await publishRelease({ directory: f.directory, commit, api: f.api });
  assert.equal(result.draft, false);
  assert.equal(f.assets.length, 6);
  assert.equal(f.calls.find(c => c.path.endsWith('/git/refs')).json.sha, commit);
  assert.equal(f.calls.find(c => c.method === 'POST' && c.path.endsWith('/releases')).json.draft, true);
  assert.equal(f.calls.at(-1).method, 'PATCH');
});

test('leaves a published version untouched on later main pushes', async t => {
  const f = await fixture(t);
  f.setRelease({ id: 12, tag_name: 'v0.1.1', draft: false, html_url: 'https://github.com/revanapriyandi/jemur/releases/tag/v0.1.1' });
  const result = await publishRelease({ directory: f.directory, commit: 'b'.repeat(40), api: f.api });
  assert.equal(result.skipped, true);
  assert.equal(f.calls.every(c => c.method === 'GET'), true);
});

test('rejects corrupt local files before creating a tag or release', async t => {
  const f = await fixture(t);
  await writeFile(join(f.directory, f.manifest.files[0].name), 'corrupt');
  await assert.rejects(publishRelease({ directory: f.directory, commit, api: f.api }), /verification/);
  assert.equal(f.calls.every(c => c.method === 'GET'), true);
});

test('refuses to move an existing tag to another commit', async t => {
  const f = await fixture(t);
  f.setTag({ object: { sha: 'b'.repeat(40), type: 'commit' } });
  await assert.rejects(publishRelease({ directory: f.directory, commit, api: f.api }), /different commit/);
  assert.equal(f.calls.every(c => c.method === 'GET'), true);
});

test('an upload failure leaves the release as a draft, then resumes on retry', async t => {
  const f = await fixture(t);
  let fail = true;
  const api = (method, path, options) => {
    if (fail && method === 'POST' && path.startsWith('https://uploads.github.com/') && f.assets.length === 2) {
      throw new Error('Upload unavailable');
    }
    return f.api(method, path, options);
  };
  await assert.rejects(publishRelease({ directory: f.directory, commit, api }), /Upload unavailable/);
  assert.equal(f.assets.length, 2);
  assert.equal(f.calls.some(c => c.method === 'PATCH'), false);
  fail = false;
  const result = await publishRelease({ directory: f.directory, commit, api });
  assert.equal(result.draft, false);
  assert.equal(f.assets.length, 6);
});

test('a wrong remote digest prevents publication', async t => {
  const f = await fixture(t);
  const api = async (method, path, options) => {
    const result = await f.api(method, path, options);
    if (method === 'POST' && path.startsWith('https://uploads.github.com/')) result.digest = `sha256:${'0'.repeat(64)}`;
    return result;
  };
  await assert.rejects(publishRelease({ directory: f.directory, commit, api }), /verification/);
  assert.equal(f.calls.some(c => c.method === 'PATCH'), false);
});
