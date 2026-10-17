import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { readFile, stat } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const repository = 'revanapriyandi/jemur';
const base = `/repos/${repository}`;
const variants = ['win-arm64', 'win-arm64-setup', 'win-x64', 'win-x64-setup'];

async function checksum(path) {
  const hash = createHash('sha256');
  for await (const block of createReadStream(path)) hash.update(block);
  return hash.digest('hex');
}

function verifiedAsset(remote, local) {
  return remote.state === 'uploaded' && remote.size === local.size && remote.digest === `sha256:${local.sha256}`;
}

export function githubApi(token) {
  if (!token) throw new Error('JEMUR_RELEASE_TOKEN is required for automatic GitHub releases');
  return async (method, path, options = {}) => {
    const url = path.startsWith('https://') ? new URL(path) : new URL(path, 'https://api.github.com');
    if (!['api.github.com', 'uploads.github.com'].includes(url.hostname) || url.protocol !== 'https:') {
      throw new Error('Unexpected GitHub API destination');
    }
    const response = await fetch(url, {
      method, redirect: 'error', signal: AbortSignal.timeout(300000),
      headers: {
        Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2026-03-10', 'User-Agent': 'Jemur-Cloudflare-Releases',
        ...(options.json ? { 'Content-Type': 'application/json' } : {}),
        ...(options.body ? { 'Content-Type': options.contentType, 'Content-Length': String(options.size) } : {}),
      },
      ...(options.json ? { body: JSON.stringify(options.json) } : {}),
      ...(options.body ? { body: options.body, duplex: 'half' } : {}),
    });
    if (response.status === 404 && options.allowMissing) return null;
    if (!response.ok) throw new Error(`GitHub ${method} ${url.pathname} returned HTTP ${response.status}`);
    if (response.status === 204) return null;
    return response.json();
  };
}

async function listAll(api, path) {
  const values = [];
  for (let page = 1; ; page++) {
    const batch = await api('GET', `${path}?per_page=100&page=${page}`);
    values.push(...batch);
    if (batch.length < 100) return values;
  }
}

export async function publishRelease({ directory, commit, api, notes = '', log = () => {} }) {
  const manifest = JSON.parse(await readFile(join(directory, 'manifest.json'), 'utf8'));
  if (!/^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$/.test(manifest.version)) throw new Error('Invalid release version');
  const tagName = `v${manifest.version}`;
  const releases = await listAll(api, `${base}/releases`);
  let release = releases.find(value => value.tag_name === tagName);
  if (release && !release.draft) {
    log(`${tagName} is already published; keeping its tag and assets unchanged.`);
    return { ...release, skipped: true };
  }
  if (!/^[0-9a-f]{40}$/.test(commit) || manifest.commit !== commit) throw new Error('Manifest commit does not match build');
  if (JSON.stringify(manifest.files.map(file => file.variant).sort()) !== JSON.stringify(variants)) {
    throw new Error('Expected four portable/setup EXEs');
  }
  const assets = [];
  for (const file of manifest.files) {
    const expectedName = `Jemur-${manifest.version}-${file.variant}.exe`;
    if (file.name !== expectedName || file.runtimeIncluded !== true) throw new Error('Unexpected release filename or runtime');
    const path = join(directory, file.name);
    if ((await stat(path)).size !== file.size || await checksum(path) !== file.sha256) {
      throw new Error(`Local artifact verification failed: ${file.name}`);
    }
    assets.push({ ...file, path, contentType: 'application/octet-stream' });
  }
  const expectedSums = manifest.files.map(f => `${f.sha256}  ${f.name}\n`).join('');
  if (await readFile(join(directory, 'SHA256SUMS.txt'), 'utf8') !== expectedSums) throw new Error('Checksum list verification failed');
  for (const [name, contentType] of [['manifest.json', 'application/json'], ['SHA256SUMS.txt', 'text/plain']]) {
    const path = join(directory, name);
    assets.push({ name, path, size: (await stat(path)).size, sha256: await checksum(path), contentType });
  }
  const refPath = `${base}/git/ref/tags/${tagName}`;
  let ref = await api('GET', refPath, { allowMissing: true });
  if (ref && (ref.object.type !== 'commit' || ref.object.sha !== commit)) throw new Error(`${tagName} points to a different commit; refusing to move it`);
  if (release && release.target_commitish !== commit) throw new Error('Draft release targets a different commit');
  if (!ref) {
    ref = await api('POST', `${base}/git/refs`, { json: { ref: `refs/tags/${tagName}`, sha: commit } });
  }
  const body = [
    `## Jemur ${manifest.version}`, notes,
    '### Downloads',
    '| Package | Architecture |', '| --- | --- |',
    ...manifest.files.map(f => `| \`${f.name}\` | ${f.variant.includes('arm64') ? 'ARM64' : 'Intel / AMD x64'} |`),
    '', 'Choose a setup EXE to install Jemur, or run the portable EXE directly. Every package includes the .NET runtime.',
    'The installer adds Start menu shortcuts and an installed-apps entry for your current Windows user. Uninstalling retains your library and settings.',
    '', `Built and published automatically by Cloudflare Workers Builds from commit \`${commit}\`. Linux tests, Windows cross-compilation and public download SHA-256 checks passed before publication.`,
    'ARM64 runtime testing still requires an ARM64 Windows device. See manifest.json and SHA256SUMS.txt for package details and hashes.',
  ].join('\n');
  if (!release) {
    release = await api('POST', `${base}/releases`, { json: {
      tag_name: tagName, target_commitish: commit, name: `Jemur ${tagName}`, body,
      draft: true, prerelease: manifest.version.includes('-'),
    } });
  }
  let uploaded = await listAll(api, `${base}/releases/${release.id}/assets`);
  for (const asset of assets) {
    const existing = uploaded.find(remote => remote.name === asset.name);
    if (existing && verifiedAsset(existing, asset)) continue;
    if (existing) await api('DELETE', `${base}/releases/assets/${existing.id}`);
    const url = `https://uploads.github.com${base}/releases/${release.id}/assets?name=${encodeURIComponent(asset.name)}`;
    const remote = await api('POST', url, {
      body: createReadStream(asset.path), size: asset.size, contentType: asset.contentType,
    });
    if (!verifiedAsset(remote, asset)) throw new Error(`GitHub upload verification failed: ${asset.name}`);
    log(`Uploaded and verified: ${asset.name}`);
  }
  uploaded = await listAll(api, `${base}/releases/${release.id}/assets`);
  if (uploaded.length !== assets.length || assets.some(local => !uploaded.some(remote => remote.name === local.name && verifiedAsset(remote, local)))) {
    throw new Error('Release asset verification failed; leaving draft unpublished');
  }
  const published = await api('PATCH', `${base}/releases/${release.id}`, { json: {
    draft: false, body, make_latest: manifest.version.includes('-') ? 'false' : 'true',
  } });
  if (published.draft) throw new Error('GitHub release is still a draft');
  log(`Published ${tagName}: ${published.html_url}`);
  return published;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const commit = process.argv[2];
  const directory = resolve(process.env.JEMUR_ARTIFACT_DIRECTORY ?? 'artifacts');
  const changelog = await readFile('CHANGELOG.md', 'utf8');
  const version = JSON.parse(await readFile(join(directory, 'manifest.json'), 'utf8')).version;
  const entry = changelog.split(`## [${version}]`)[1]?.split(/\n## \[/)[0]?.trim();
  const notes = entry ? entry.replace(/^[^\n]*\n/, '').trim() : '';
  await publishRelease({ directory, commit, api: githubApi(process.env.JEMUR_RELEASE_TOKEN), notes, log: console.log });
}
