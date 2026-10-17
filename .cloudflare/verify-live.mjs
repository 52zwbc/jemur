import { createHash } from 'node:crypto';
import { waitForDeployment } from './live-manifest.mjs';

const origin = 'https://jemur-downloads.syeana-co.workers.dev';
const expectedCommit = process.argv[2];
const manifest = await waitForDeployment(origin, expectedCommit);
console.log('Verified deployed commit: ' + manifest.commit);
const variants = ['win-arm64', 'win-arm64-setup', 'win-x64', 'win-x64-setup'];
if (JSON.stringify(manifest.files.map(file => file.variant).sort()) !== JSON.stringify(variants)) {
  throw new Error('Expected portable and installer EXEs for both architectures');
}
for (const file of manifest.files) {
  if (!file.name.endsWith('.exe') || file.runtimeIncluded !== true) throw new Error('Expected self-contained EXE');
  const download = await fetch(`${file.url}?commit=${expectedCommit}`);
  if (!download.ok) throw new Error(`${file.name} returned HTTP ${download.status}`);
  const hash = createHash('sha256');
  let size = 0;
  let magic = Buffer.alloc(0);
  for await (const chunk of download.body) {
    if (magic.length < 2) magic = Buffer.concat([magic, chunk]).subarray(0, 2);
    size += chunk.byteLength;
    hash.update(chunk);
  }
  if (size !== file.size || hash.digest('hex') !== file.sha256 || magic.toString('ascii') !== 'MZ') {
    throw new Error(`${file.name} failed size or SHA-256 verification`);
  }
  console.log(`Verified public download: ${file.name} (${size} bytes)`);
}
