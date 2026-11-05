/**
 * The worker this application registers: Angular's worker behind a listener that keeps protected
 * traffic away from it (`public/app-worker.js`). Relative, so it resolves against `<base href>`.
 */
export const APP_WORKER_SCRIPT = 'app-worker.js';

/**
 * `none` makes update checks fetch the imported `ngsw-worker.js` past the HTTP cache as well, so a
 * stale copy can never keep an older worker running. Every registration passes the same options:
 * a different set would start another update.
 */
export const APP_WORKER_REGISTRATION = { updateViaCache: 'none' } as const satisfies RegistrationOptions;

export function isAppWorkerScript(scriptUrl: string): boolean {
  try {
    return new URL(scriptUrl).pathname.endsWith(`/${APP_WORKER_SCRIPT}`);
  } catch {
    return false;
  }
}
