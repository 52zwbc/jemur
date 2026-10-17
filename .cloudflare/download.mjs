export function createWorker(manifest) {
  return {
    async fetch(request, env) {
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
        const file = manifest.files.find(f => `/latest/${f.variant}` === url.pathname);
        if (file) return new Response(null, { status: 302, headers: {
          Location: `/downloads/${file.name}`, 'Cache-Control': 'no-cache',
        } });
      }
      const file = manifest.files.find(f => `/downloads/${f.name}` === url.pathname);
      if (!file) return new Response('Not found', { status: 404 });
      const headers = new Headers({
        'Content-Type': 'application/zip',
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
      const responses = await Promise.all(file.chunks.map(c => env.ASSETS.fetch(new URL(c.path, url.origin))));
      if (responses.some((r, i) => !r.ok || !r.body || Number(r.headers.get('Content-Length')) !== file.chunks[i].size)) {
        await Promise.all(responses.map(r => r.body?.cancel()));
        return new Response('Download temporarily unavailable', { status: 503 });
      }
      const readers = responses.map(r => r.body.getReader());
      let index = 0;
      const stream = new ReadableStream({
        async pull(controller) {
          while (index < readers.length) {
            const { done, value } = await readers[index].read();
            if (done) { index++; continue; }
            controller.enqueue(value);
            return;
          }
          controller.close();
        },
        async cancel(reason) { await Promise.all(readers.map(r => r.cancel(reason))); },
      });
      return new Response(stream, { headers });
    },
  };
}
