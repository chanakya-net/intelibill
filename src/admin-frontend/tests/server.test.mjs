// Runtime behaviour of the admin web image's server (src/admin-frontend/server.mjs).
// Runs with Node's own test runner against real sockets:
//   node --test src/admin-frontend/tests/server.test.mjs
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import http from 'node:http';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';

import { createAdminServer, parseApiOrigin } from '../server.mjs';

const listen = (server) =>
  new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve(`http://127.0.0.1:${server.address().port}`)));
const close = (server) =>
  new Promise((resolve) => {
    server.closeAllConnections?.();
    server.close(() => resolve());
  });

function rawGet(origin, path) {
  // fetch() normalises "..", so traversal attempts go out on a raw request.
  const { hostname, port } = new URL(origin);
  return new Promise((resolve, reject) => {
    const request = http.request({ hostname, port, path, method: 'GET' }, (response) => {
      let body = '';
      response.on('data', (chunk) => (body += chunk));
      response.on('end', () => resolve({ status: response.statusCode, body }));
    });
    request.on('error', reject);
    request.end();
  });
}

describe('admin web runtime server', () => {
  let root;
  let upstream;
  let upstreamOrigin;
  let server;
  let origin;
  const upstreamRequests = [];

  before(async () => {
    const scratch = await mkdtemp(join(tmpdir(), 'admin-server-'));
    root = join(scratch, 'browser');
    await mkdir(join(root, 'media'), { recursive: true });
    await writeFile(join(root, 'index.html'), '<!doctype html><adm-root></adm-root>');
    await writeFile(join(root, 'main-ABC123.js'), 'console.log("admin")');
    await writeFile(join(root, 'styles-ABC123.css'), 'body{}');
    await writeFile(join(root, 'media', 'font.woff2'), 'font');
    await writeFile(join(scratch, 'secret.txt'), 'outside the web root');

    upstream = http.createServer((request, response) => {
      let body = '';
      request.on('data', (chunk) => (body += chunk));
      request.on('end', () => {
        upstreamRequests.push({ method: request.method, url: request.url, body, headers: request.headers });
        response.writeHead(418, { 'Content-Type': 'application/json' });
        response.end(JSON.stringify({ echoed: request.url }));
      });
    });
    upstreamOrigin = await listen(upstream);
    server = createAdminServer({ root, apiOrigin: upstreamOrigin });
    origin = await listen(server);
  });

  after(async () => {
    await close(server);
    await close(upstream);
    await rm(join(root, '..'), { recursive: true, force: true });
  });

  test('serves the shell at the root and on client-side routes', async () => {
    for (const path of ['/', '/provider/accounts/42']) {
      const response = await fetch(`${origin}${path}`);
      assert.equal(response.status, 200, path);
      assert.match(response.headers.get('content-type'), /^text\/html/);
      assert.equal(response.headers.get('cache-control'), 'no-cache');
      assert.match(await response.text(), /<adm-root>/);
    }
  });

  test('serves built assets with their content types', async () => {
    const cases = [
      ['/main-ABC123.js', /^text\/javascript/],
      ['/styles-ABC123.css', /^text\/css/],
      ['/media/font.woff2', /^font\/woff2/],
    ];
    for (const [path, type] of cases) {
      const response = await fetch(`${origin}${path}`);
      assert.equal(response.status, 200, path);
      assert.match(response.headers.get('content-type'), type, path);
      assert.equal(response.headers.get('x-content-type-options'), 'nosniff');
    }
  });

  test('answers a missing asset with 404 instead of the shell', async () => {
    const response = await fetch(`${origin}/main-MISSING.js`);
    assert.equal(response.status, 404);
    assert.doesNotMatch(await response.text(), /<adm-root>/);
  });

  test('never serves a file outside the web root', async () => {
    for (const path of ['/../secret.txt', '/%2e%2e/secret.txt', '/media/..%2f..%2fsecret.txt']) {
      const response = await rawGet(origin, path);
      assert.ok([400, 404].includes(response.status), `${path} answered ${response.status}`);
      assert.doesNotMatch(response.body, /outside the web root/, path);
    }
  });

  test('has no service worker', async () => {
    for (const path of ['/ngsw-worker.js', '/ngsw.json']) {
      const response = await fetch(`${origin}${path}`);
      assert.equal(response.status, 404, path);
    }
  });

  test('forwards only the reserved /api/admin segment to the licensing API', async () => {
    upstreamRequests.length = 0;
    const response = await fetch(`${origin}/api/admin/session/check?mode=probe`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ probe: true }),
    });
    assert.equal(response.status, 418);
    assert.equal(response.headers.get('cache-control'), 'no-store');
    assert.deepEqual(await response.json(), { echoed: '/api/admin/session/check?mode=probe' });
    assert.equal(upstreamRequests.length, 1);
    assert.equal(upstreamRequests[0].method, 'POST');
    assert.equal(upstreamRequests[0].body, '{"probe":true}');
    assert.equal(upstreamRequests[0].headers['x-forwarded-proto'], 'http');
  });

  test('refuses merchant and other API routes without forwarding them', async () => {
    upstreamRequests.length = 0;
    for (const path of ['/api/shops/me', '/api/administrator', '/api', '/hubs/inventory', '/api/admin/../shops/me']) {
      const response = await rawGet(origin, path);
      assert.equal(response.status, 404, path);
      assert.doesNotMatch(response.body, /<adm-root>/, path);
    }
    const generic = await fetch(`${origin}/api/shops/me`);
    assert.equal(generic.headers.get('cache-control'), 'no-store');
    assert.equal(upstreamRequests.length, 0);
  });

  test('refuses writes to static paths', async () => {
    const response = await fetch(`${origin}/`, { method: 'POST', body: 'x' });
    assert.equal(response.status, 405);
  });

  test('answers 502 without detail when the licensing API is unreachable', async () => {
    const offline = createAdminServer({ root, apiOrigin: 'http://127.0.0.1:1' });
    const offlineOrigin = await listen(offline);
    try {
      const response = await fetch(`${offlineOrigin}/api/admin/session`);
      assert.equal(response.status, 502);
      assert.equal(response.headers.get('cache-control'), 'no-store');
      assert.doesNotMatch(await response.text(), /ECONNREFUSED|127\.0\.0\.1/);
    } finally {
      await close(offline);
    }
  });
});

describe('API origin configuration', () => {
  test('accepts a bare http(s) origin', () => {
    assert.equal(parseApiOrigin('http://licensing-api:8080').origin, 'http://licensing-api:8080');
    assert.equal(parseApiOrigin('https://licensing.example.test').origin, 'https://licensing.example.test');
  });

  test('rejects missing, relative, credentialed, path-bearing and non-http origins', () => {
    for (const value of [
      undefined,
      '',
      'licensing-api:8080',
      'ftp://licensing-api',
      'http://user:pass@licensing-api:8080',
      'http://licensing-api:8080/api',
      'http://licensing-api:8080?x=1',
    ]) {
      assert.throws(() => parseApiOrigin(value), /API_ORIGIN/, String(value));
    }
  });
});
