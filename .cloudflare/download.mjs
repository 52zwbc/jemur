export function createWorker(manifest, createStream = size => new FixedLengthStream(size)) {
  return {
    async fetch(request, env, context) {
      if (!['GET', 'HEAD'].includes(request.method)) {
        return new Response('Method not allowed', { status: 405, headers: { Allow: 'GET, HEAD' } });
      }
      const url = new URL(request.url);
      const head = request.method === 'HEAD';
      const json = (value) => new Response(head ? null : JSON.stringify(value), {
        headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-cache' },
      });
      if (url.pathname === '/health') return json({ version: manifest.version, commit: manifest.commit });
      if (url.pathname === '/' || url.pathname === '/manifest.json') {
        return json({ version: manifest.version, commit: manifest.commit,
          files: manifest.files.map(({ chunks, ...file }) => ({ ...file, url: `${url.origin}/downloads/${file.name}` })) });
      }
      if (url.pathname === '/SHA256SUMS.txt') {
        return new Response(head ? null : manifest.files.map(f => `${f.sha256}  ${f.name}\n`).join(''), {
          headers: { 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'no-cache' },
        });
      }
      if (url.pathname.startsWith('/latest/')) {
        const variant = url.pathname.slice('/latest/'.length).replace(/-standalone$/, '');
        const file = manifest.files.find(f => f.variant === variant);
        if (file) return new Response(null, { status: 302, headers: {
          Location: `/downloads/${file.name}`, 'Cache-Control': 'no-cache',
        } });
      }
      const file = manifest.files.find(f => `/downloads/${f.name}` === url.pathname);
      if (!file) return new Response('Not found', { status: 404 });
      const headers = new Headers({
        'Content-Type': 'application/octet-stream',
        'Content-Disposition': `attachment; filename="${file.name}"`,
        'Content-Length': String(file.size),
        'Cache-Control': 'public, max-age=300',
        'X-Content-Type-Options': 'nosniff',
        'ETag': `"${file.sha256}"`,
      });
      if (request.headers.get('If-None-Match') === headers.get('ETag')) {
        headers.delete('Content-Length');
        return new Response(null, { status: 304, headers });
      }
      if (head) return new Response(null, { headers });
      async function openChunk(index) {
        const chunk = file.chunks[index];
        const response = await env.ASSETS.fetch(new URL(chunk.path, url.origin));
        if (!response.ok || !response.body ||
          (response.headers.has('Content-Length') && Number(response.headers.get('Content-Length')) !== chunk.size)) {
          await response.body?.cancel();
          throw new Error('Unavailable download chunk');
        }
        return response.body;
      }
      let firstChunk;
      try { firstChunk = await openChunk(0); } catch {
        return new Response('Download temporarily unavailable', { status: 503 });
      }
      const { readable, writable } = createStream(file.size);
      const transfer = (async () => {
        try {
          for (let index = 0; index < file.chunks.length; index++) {
            const body = index === 0 ? firstChunk : await openChunk(index);
            await body.pipeTo(writable, { preventClose: true });
          }
          const writer = writable.getWriter();
          try { await writer.close(); } finally { writer.releaseLock(); }
        } catch (error) {
          await writable.abort(error).catch(() => {});
        }
      })();
      context?.waitUntil(transfer);
      return new Response(readable, { headers });
    },
  };
}
