/** Minimal static server for dist/. Module scripts need http, not file://. */
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'dist');
const types = {
  '.html': 'text/html',
  '.js': 'text/javascript',
  '.css': 'text/css',
  '.map': 'application/json',
};

export function serve(port = 0) {
  const server = createServer(async (req, res) => {
    const raw = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    const stripped = raw.replace(/^\/+/, '');
    const file = stripped === '' ? join(root, 'index.html') : join(root, normalize(stripped));
    if (!file.startsWith(root)) {
      res.writeHead(403).end();
      return;
    }
    try {
      const body = await readFile(file);
      res
        .writeHead(200, { 'content-type': types[extname(file)] ?? 'application/octet-stream' })
        .end(body);
    } catch {
      res.writeHead(404).end('not found');
    }
  });
  return new Promise((ok) =>
    server.listen(port, '127.0.0.1', () => ok({ server, port: server.address().port })),
  );
}
