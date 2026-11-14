// Runtime server for the admin web image. Node built-ins only, so the image
// ships no node_modules.
//
// It serves the ADMIN production build (client-side routes answer with the
// shell) and forwards exactly one reserved path segment, /api/admin and its
// descendants, to the licensing API named by API_ORIGIN. Nothing else under
// /api or /hubs is forwarded: the admin origin must never become a route to the
// merchant API, and a merchant session in the same browser confers nothing here.
//
//   PORT        listening port (default 4000)
//   API_ORIGIN  licensing API origin, e.g. http://licensing-api:8080
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import http from 'node:http';
import https from 'node:https';
import { extname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.woff': 'font/woff',
  '.woff2': 'font/woff2',
  '.ttf': 'font/ttf',
  '.txt': 'text/plain; charset=utf-8',
};
const SECURITY_HEADERS = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'no-referrer',
};
const HOP_BY_HOP = new Set([
  'connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization',
  'te', 'trailer', 'transfer-encoding', 'upgrade', 'host',
]);
const PROXY_TIMEOUT_MS = 15_000;
const ADMIN_API = /^\/api\/admin(\/|$)/;
const RESERVED = /^\/(api|hubs)(\/|$)/;

export function parseApiOrigin(value) {
  let url;
  try {
    url = new URL(value ?? '');
  } catch {
    throw new Error('API_ORIGIN must be an absolute http(s) origin.');
  }
  const bare = url.pathname === '/' && !url.search && !url.hash && !url.username && !url.password;
  if (!['http:', 'https:'].includes(url.protocol) || !bare || String(value).replace(/\/$/, '') !== url.origin) {
    throw new Error('API_ORIGIN must be a bare http(s) origin without credentials, path or query.');
  }
  return url;
}

function plain(response, status, cacheControl = 'no-store') {
  response.writeHead(status, { ...SECURITY_HEADERS, 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': cacheControl });
  response.end(http.STATUS_CODES[status]);
}

// Decodes and confines a request path to the web root; null when it escapes,
// is malformed, or names a dot-segment.
function confine(root, pathname) {
  let decoded;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    return null;
  }
  if (decoded.includes('\0') || decoded.split(/[/\\]/).some((part) => part === '..')) {
    return null;
  }
  const file = resolve(root, `.${decoded}`);
  return file === root || file.startsWith(root + sep) ? file : null;
}

async function isFile(path) {
  try {
    return (await stat(path)).isFile();
  } catch {
    return false;
  }
}

async function serveStatic(root, request, response, pathname) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.setHeader('Allow', 'GET, HEAD');
    plain(response, 405);
    return;
  }
  const file = confine(root, pathname === '/' ? '/index.html' : pathname);
  if (file === null) {
    plain(response, 404);
    return;
  }
  let target = file;
  if (!(await isFile(file))) {
    // Client-side routes have no extension; a missing asset is a real 404.
    if (extname(pathname)) {
      plain(response, 404);
      return;
    }
    target = join(root, 'index.html');
  }
  const html = target.endsWith('.html');
  response.writeHead(200, {
    ...SECURITY_HEADERS,
    'Content-Type': CONTENT_TYPES[extname(target)] ?? 'application/octet-stream',
    'Cache-Control': html ? 'no-cache' : 'public, max-age=3600',
  });
  if (request.method === 'HEAD') {
    response.end();
    return;
  }
  createReadStream(target).pipe(response);
}

function forward(apiOrigin, request, response) {
  const headers = Object.fromEntries(
    Object.entries(request.headers).filter(([name]) => !HOP_BY_HOP.has(name.toLowerCase())),
  );
  headers['x-forwarded-proto'] = 'http';
  headers['x-forwarded-for'] = request.socket.remoteAddress ?? '';
  const client = apiOrigin.protocol === 'https:' ? https : http;
  const upstream = client.request(
    new URL(request.url, apiOrigin),
    { method: request.method, headers, timeout: PROXY_TIMEOUT_MS },
    (answer) => {
      const forwarded = Object.fromEntries(
        Object.entries(answer.headers).filter(([name]) => !HOP_BY_HOP.has(name.toLowerCase())),
      );
      response.writeHead(answer.statusCode ?? 502, { ...forwarded, ...SECURITY_HEADERS, 'Cache-Control': 'no-store' });
      answer.pipe(response);
    },
  );
  const failed = () => {
    if (!response.headersSent) {
      plain(response, 502);
    } else {
      response.destroy();
    }
  };
  upstream.on('timeout', () => upstream.destroy(new Error('timeout')));
  upstream.on('error', failed);
  request.pipe(upstream);
}

export function createAdminServer({ root, apiOrigin }) {
  const webRoot = resolve(root);
  const upstream = parseApiOrigin(typeof apiOrigin === 'string' ? apiOrigin : apiOrigin?.origin);
  return http.createServer((request, response) => {
    const rawPath = (request.url ?? '/').split('?')[0];
    if (!rawPath.startsWith('/')) {
      plain(response, 400);
      return;
    }
    if (RESERVED.test(rawPath)) {
      const traversal = rawPath.split('/').some((part) => part === '..' || part === '.' || /%2e|%2f|%5c/i.test(part));
      if (ADMIN_API.test(rawPath) && !traversal) {
        forward(upstream, request, response);
      } else {
        plain(response, 404);
      }
      return;
    }
    serveStatic(webRoot, request, response, rawPath).catch(() => {
      if (!response.headersSent) plain(response, 500);
    });
  });
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number.parseInt(process.env.PORT ?? '4000', 10);
  const root = process.env.ADMIN_WEB_ROOT ?? join(fileURLToPath(new URL('.', import.meta.url)), 'browser');
  const server = createAdminServer({ root, apiOrigin: process.env.API_ORIGIN });
  server.listen(port, () => console.log(`Admin web listening on ${port}`));
  const stop = () => server.close(() => process.exit(0));
  process.on('SIGTERM', stop);
  process.on('SIGINT', stop);
}
