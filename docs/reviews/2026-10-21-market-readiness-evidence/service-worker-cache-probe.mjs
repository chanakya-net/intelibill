import http from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from '/Users/chanakya/projects/intelibill/src/frontend/node_modules/playwright/index.mjs';

const root = '/Users/chanakya/projects/intelibill/src/frontend/dist/INVENTORY/browser';
let delayApi = false;
const mime = { '.html': 'text/html', '.js': 'application/javascript', '.json': 'application/json', '.css': 'text/css', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };
const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1:4379');
  if (url.pathname === '/api/review-tenant') {
    if (delayApi) return;
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ tenant: req.headers.authorization }));
    return;
  }
  if (url.pathname === '/__review-page') {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end('<!doctype html><title>Production service worker isolation audit</title>');
    return;
  }
  try {
    const file = path.join(root, url.pathname === '/' ? '/index.html' : url.pathname);
    const bytes = await readFile(file);
    res.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' });
    res.end(bytes);
  } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(4379, '127.0.0.1', resolve));
const browser = await chromium.launch();
try {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('http://127.0.0.1:4379/__review-page');
  await page.evaluate(async () => {
    await navigator.serviceWorker.register('/ngsw-worker.js');
    await navigator.serviceWorker.ready;
  });
  await page.waitForFunction(() => navigator.serviceWorker.controller !== null);
  const first = await page.evaluate(async () => (await fetch('/api/review-tenant', {headers: {Authorization: 'Shop-A'}})).json());
  await page.waitForFunction(async () => {
    for (const key of await caches.keys()) {
      if (!key.includes('api-fresh')) continue;
      if (await (await caches.open(key)).match('/api/review-tenant')) return true;
    }
    return false;
  });
  delayApi = true;
  const started = Date.now();
  const second = await page.evaluate(async () => (await fetch('/api/review-tenant', {headers: {Authorization: 'Shop-B'}})).json());
  console.log(JSON.stringify({productionConfig:true, firstRequest:'Shop-A', firstResponse:first, secondRequest:'Shop-B', secondResponse:second, delayedNetworkFallbackMs:Date.now()-started, crossTenantCacheReuse:second.tenant==='Shop-A'}, null, 2));
} finally {
  await browser.close();
  server.closeAllConnections();
  await new Promise(resolve=>server.close(resolve));
}
