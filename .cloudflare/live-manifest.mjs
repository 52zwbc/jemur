export async function waitForDeployment(origin, commit, options = {}) {
  const fetchResponse = options.fetch ?? fetch;
  const attempts = options.attempts ?? 20;
  const wait = options.wait ?? (() => new Promise(resolve => setTimeout(resolve, 3000)));
  let lastError;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      const query = `commit=${commit}&attempt=${attempt}`;
      const healthResponse = await fetchResponse(`${origin}/health?${query}`);
      if (!healthResponse.ok) throw new Error(`Health returned HTTP ${healthResponse.status}`);
      const health = await healthResponse.json();
      if (health.commit !== commit) throw new Error(`Health still reports ${health.commit}`);
      const manifestResponse = await fetchResponse(`${origin}/manifest.json?${query}`);
      if (!manifestResponse.ok) throw new Error(`Manifest returned HTTP ${manifestResponse.status}`);
      const manifest = await manifestResponse.json();
      if (manifest.commit !== commit) throw new Error(`Manifest still reports ${manifest.commit}`);
      return manifest;
    } catch (error) {
      lastError = error;
      if (attempt < attempts) await wait();
    }
  }
  throw new Error(`Deployment did not become ready for ${commit}`, { cause: lastError });
}
