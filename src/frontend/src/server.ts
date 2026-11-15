import express from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { join, relative } from 'node:path';

import { cacheControlFor } from './server-cache-policy';

/**
 * Serves the built single-page application and forwards its API calls.
 *
 * There is no server-side rendering here. Every route in this application is
 * behind an authentication guard whose session lives in browser storage, so a
 * server render could only ever produce the login screen, and the two routes
 * that mattered most had already opted out of it.
 *
 * The process remains because the browser must reach `/api` and `/hubs` on the
 * origin that served the page: that is what removes cross-origin requests, and
 * with them the CORS configuration and the second hostname in the bundle.
 */
const browserDistFolder = join(import.meta.dirname, '../browser');
const indexHtml = join(browserDistFolder, 'index.html');

const app = express();

/**
 * API_ORIGIN is the API's own origin (scheme, host, port). Without it the proxy
 * is not mounted: `ng serve` talks to the API directly through the absolute URL
 * in the development environment file.
 */
const apiOrigin = process.env['API_ORIGIN'];

const apiProxy = apiOrigin
  ? createProxyMiddleware({
      target: apiOrigin,
      changeOrigin: true,
      // SignalR upgrades /hubs to a WebSocket; without this the negotiate call
      // succeeds and the connection that follows it does not.
      ws: true,
      // Pass the client address and scheme on, so the API's forwarded-headers
      // configuration can see past this hop. It is the second proxy in the
      // chain, after the platform ingress.
      xfwd: true,
      pathFilter: ['/api/**', '/hubs/**'],
    })
  : null;

if (apiProxy) {
  app.use(apiProxy);
}

/**
 * Hashed build output is immutable and cached for a year. The files whose name
 * never changes are not: index.html names those hashed files, and the service
 * worker files decide which version runs, so a cached copy would keep the
 * previous deployment alive (see server-cache-policy.ts).
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
    setHeaders: (res, path) => {
      const cacheControl = cacheControlFor(relative(browserDistFolder, path));
      if (cacheControl) {
        res.setHeader('Cache-Control', cacheControl);
      }
    },
  }),
);

/**
 * Anything left is a client-side route. The application resolves it after the
 * bundle loads, so a deep link such as /sales/123/print has to be answered with
 * the shell rather than a 404.
 */
app.use((_req, res) => {
  res.setHeader('Cache-Control', 'no-cache');
  res.sendFile(indexHtml);
});

const port = Number(process.env['PORT'] ?? 4000);

const server = app.listen({ port, host: process.env['WEB_BIND_HOST'] }, (error?: Error) => {
  if (error) {
    throw error;
  }

  const address = server.address();
  console.log(`Web server listening on http://localhost:${typeof address === 'object' && address ? address.port : port}`);
});

if (apiProxy) {
  // Upgrade requests never reach the Express middleware chain, so the proxy has
  // to be attached to the server's own 'upgrade' event.
  server.on('upgrade', apiProxy.upgrade);
}
