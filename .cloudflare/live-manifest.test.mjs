import { test } from 'node:test';
import assert from 'node:assert/strict';
import { waitForDeployment } from './live-manifest.mjs';

test('waits for both health and manifest to propagate to the expected commit', async () => {
  const replies = [
    { commit: 'old' },
    { commit: 'new' }, { commit: 'old' },
    { commit: 'new' }, { commit: 'new', files: [] },
  ];
  let waits = 0;
  const manifest = await waitForDeployment('https://example.test', 'new', {
    fetch: async () => Response.json(replies.shift()),
    wait: async () => { waits++; }, attempts: 3,
  });
  assert.equal(manifest.commit, 'new');
  assert.equal(waits, 2);
  assert.equal(replies.length, 0);
});

test('never verifies a deployment that remains stale or unavailable', async () => {
  for (const fetch of [async () => Response.json({ commit: 'old' }), async () => new Response('busy', { status: 503 })]) {
    await assert.rejects(waitForDeployment('https://example.test', 'new', {
      fetch, wait: async () => {}, attempts: 2,
    }), /did not become ready/);
  }
});
