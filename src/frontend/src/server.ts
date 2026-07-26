import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

/**
 * The browser talks only to this origin. `/api` and `/hubs` are relative in the
 * built application and are forwarded from here to the API, so there is no
 * cross-origin request to allow, no second hostname baked into the bundle, and
 * no CORS or cookie configuration that has to agree across two deployments.
 *
 * API_ORIGIN is the API's own origin (scheme, host, port). Without it the proxy
 * is not mounted at all: local `ng serve` talks to the API directly through the
 * absolute URL in the development environment file.
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
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) =>
      response ? writeResponseToNodeResponse(response, res) : next(),
    )
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  const server = app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });

  if (apiProxy) {
    // Upgrade requests never reach the Express middleware chain, so the proxy
    // has to be attached to the server's own 'upgrade' event.
    server.on('upgrade', apiProxy.upgrade);
  }
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
