/**
 * The worker URL every build has registered. The build publishes Intelibill's worker here, wrapping
 * Angular's (`scripts/finalize-service-worker.mjs`, `service-worker/protected-api-worker.js`), so
 * the browser's update check replaces an earlier worker in place and no older page can register a
 * different one. Relative, so it resolves against `<base href>`.
 */
export const APP_WORKER_SCRIPT = 'ngsw-worker.js';

/**
 * Earlier workers share the URL, so a page asks: only Intelibill's worker answers this message, on
 * the port it is sent with, with `{ protectedApiIsolation: PROTECTED_WORKER_PROTOCOL }`. The worker
 * script carries the same two values.
 */
export const PROTECTED_WORKER_QUERY = 'intelibill:protected-api-isolation';
export const PROTECTED_WORKER_PROTOCOL = 1;
