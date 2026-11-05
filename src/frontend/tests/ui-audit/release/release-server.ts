import { readFile, stat } from 'node:fs/promises';
import http from 'node:http';
import type { AddressInfo } from 'node:net';
import { extname, join, normalize } from 'node:path';

/**
 * Serves a production build the way `src/server.ts` does, with the API replaced by a probe whose
 * answer names the identity that asked. Each test starts its own server, so each gets its own origin
 * and with it a browser storage partition and service worker registration of its own.
 */

export interface RecordedRequest {
  readonly method: string;
  readonly path: string;
  readonly authorization: string | null;
  readonly workerBypass: string | null;
}

export interface ReleaseServer {
  readonly origin: string;
  readonly requests: readonly RecordedRequest[];
  /**
   * Serves what builds up to `fbff57b09` deployed: Angular's own worker at `/ngsw-worker.js` and a
   * manifest compiled from their configuration.
   */
  useLegacyDeployment(manifest: object): void;
  /**
   * Serves the legacy worker script to the next request for it only, then this build again: one
   * update check that reaches an old instance mid-rollout.
   */
  serveLegacyWorkerOnce(manifest: object): void;
  /** Serves the build as it is: the wrapper at `/ngsw-worker.js` and the built manifest. */
  useCurrentDeployment(): void;
  /** Holds every `/api/review-tenant` answer this long, to push a worker into its timeout fallback. */
  setApiDelay(ms: number): void;
  close(): Promise<void>;
}

export const TENANT_PROBE = '/api/review-tenant';

const HARNESS_PAGE = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>Release harness</title></head>
<body><output id="out"></output></body></html>`;

const CONTENT_TYPES: Readonly<Record<string, string>> = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.webmanifest': 'application/manifest+json',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.txt': 'text/plain; charset=utf-8',
};

export async function startReleaseServer(root: string): Promise<ReleaseServer> {
  const requests: RecordedRequest[] = [];
  const timers = new Set<NodeJS.Timeout>();
  let legacyManifest: object | null = null;
  let legacyWorkerOnce = false;
  let apiDelayMs = 0;

  const server = http.createServer(async (req, res) => {
    const url = new URL(req.url ?? '/', 'http://release.test');
    requests.push({
      method: req.method ?? 'GET',
      path: url.pathname,
      authorization: header(req, 'authorization'),
      workerBypass: header(req, 'ngsw-bypass'),
    });

    if (url.pathname === '/__harness') {
      return send(res, 200, HARNESS_PAGE, 'text/html; charset=utf-8', 'no-cache');
    }

    if (url.pathname === '/ngsw.json' && legacyManifest) {
      return send(res, 200, JSON.stringify(legacyManifest), CONTENT_TYPES['.json'], 'no-cache');
    }

    if (url.pathname === '/ngsw-worker.js' && legacyManifest) {
      if (legacyWorkerOnce) {
        legacyWorkerOnce = false;
        legacyManifest = null;
      }
      return send(res, 200, await readFile(join(root, 'ngsw-angular-worker.js')), CONTENT_TYPES['.js'], 'no-cache');
    }

    if (url.pathname === TENANT_PROBE) {
      const answer = (): void =>
        send(res, 200, JSON.stringify({ tenant: header(req, 'authorization') }), CONTENT_TYPES['.json'], 'no-store');
      if (apiDelayMs <= 0) {
        return answer();
      }
      const timer = setTimeout(() => {
        timers.delete(timer);
        answer();
      }, apiDelayMs);
      timers.add(timer);
      return;
    }

    if (url.pathname === '/api/ping') {
      return send(res, 200, JSON.stringify({ serverTime: new Date().toISOString() }), CONTENT_TYPES['.json'], 'no-store');
    }

    if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) {
      return send(res, 401, JSON.stringify({ code: 'Auth.Unauthorized' }), 'application/problem+json', 'no-store');
    }

    const file = await resolveFile(root, url.pathname);
    if (file) {
      const name = file.slice(root.length + 1);
      const revalidate = !name.includes('/') && !/-[A-Z0-9]{8}\.(js|css)$/.test(name);
      return send(res, 200, await readFile(file), CONTENT_TYPES[extname(file)] ?? 'application/octet-stream', revalidate ? 'no-cache' : 'max-age=31536000');
    }

    // A client-side route, answered with the shell as the production server does.
    return send(res, 200, await readFile(join(root, 'index.html')), CONTENT_TYPES['.html'], 'no-cache');
  });

  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  const { port } = server.address() as AddressInfo;

  return {
    origin: `http://127.0.0.1:${port}`,
    requests,
    useLegacyDeployment(manifest) {
      legacyManifest = manifest;
      legacyWorkerOnce = false;
    },
    serveLegacyWorkerOnce(manifest) {
      legacyManifest = manifest;
      legacyWorkerOnce = true;
    },
    useCurrentDeployment() {
      legacyManifest = null;
      legacyWorkerOnce = false;
    },
    setApiDelay(ms) {
      apiDelayMs = ms;
    },
    async close() {
      for (const timer of timers) {
        clearTimeout(timer);
      }
      server.closeAllConnections();
      await new Promise<void>((resolve) => server.close(() => resolve()));
    },
  };
}

function header(req: http.IncomingMessage, name: string): string | null {
  const value = req.headers[name];
  return Array.isArray(value) ? value.join(', ') : (value ?? null);
}

function send(res: http.ServerResponse, status: number, body: string | Buffer, contentType: string, cacheControl: string): void {
  res.writeHead(status, { 'Content-Type': contentType, 'Cache-Control': cacheControl });
  res.end(body);
}

async function resolveFile(root: string, pathname: string): Promise<string | null> {
  const relative = normalize(decodeURIComponent(pathname === '/' ? '/index.html' : pathname)).replace(/^([/\\])+/, '');
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
