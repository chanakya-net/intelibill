import { readFile, stat } from 'node:fs/promises';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { extname, join, normalize } from 'node:path';

/**
 * Serves the admin production build on its own origin and records every requested path. Client-side
 * routes answer with the shell; nothing else is answered, so any API or worker request shows up as
 * a recorded path the scenario can refuse.
 */
export interface StaticServer {
  readonly origin: string;
  readonly paths: readonly string[];
  close(): Promise<void>;
}

const CONTENT_TYPES: Readonly<Record<string, string>> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
};

export async function startStaticServer(root: string): Promise<StaticServer> {
  const paths: string[] = [];
  const server = http.createServer(async (req, res) => {
    const { pathname } = new URL(req.url ?? '/', 'http://admin.test');
    paths.push(pathname);
    if (/^\/(api|hubs)(\/|$)/.test(pathname)) {
      res.writeHead(404, { 'Cache-Control': 'no-store' }).end();
      return;
    }
    const file = (await resolveFile(root, pathname)) ?? join(root, 'index.html');
    const type = CONTENT_TYPES[extname(file)] ?? 'application/octet-stream';
    res.writeHead(200, { 'Content-Type': type, 'Cache-Control': 'no-cache' });
    res.end(await readFile(file));
  });

  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;
  return {
    origin: `http://127.0.0.1:${port}`,
    paths,
    async close() {
      server.closeAllConnections();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}

async function resolveFile(root: string, pathname: string): Promise<string | null> {
  const relative = normalize(decodeURIComponent(pathname === '/' ? '/index.html' : pathname)).replace(
    /^([/\\])+/,
    '',
  );
  if (relative.startsWith('..')) {
    return null;
  }
  const file = join(root, relative);
  try {
    return (await stat(file)).isFile() ? file : null;
  } catch {
    return null;
  }
}
