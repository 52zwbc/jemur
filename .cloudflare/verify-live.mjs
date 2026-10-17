import { createHash } from 'node:crypto';

const origin = 'https://jemur-downloads.syeana-co.workers.dev';
const expectedCommit = process.argv[2];
const response = await fetch(`${origin}/manifest.json?commit=${expectedCommit}`);
if (!response.ok) throw new Error(`Manifest returned HTTP ${response.status}`);
const manifest = await response.json();
if (manifest.commit !== expectedCommit) throw new Error('Download manifest does not match build');
for (const file of manifest.files) {
  const download = await fetch(`${file.url}?commit=${expectedCommit}`);
  if (!download.ok) throw new Error(`${file.name} returned HTTP ${download.status}`);
  const hash = createHash('sha256');
  let size = 0;
  for await (const chunk of download.body) {
    size += chunk.byteLength;
    hash.update(chunk);
  }
  if (size !== file.size || hash.digest('hex') !== file.sha256) {
    throw new Error(`${file.name} failed size or SHA-256 verification`);
  }
  console.log(`Verified public download: ${file.name} (${size} bytes)`);
}
