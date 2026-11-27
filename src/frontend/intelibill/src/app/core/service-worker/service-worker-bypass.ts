/**
 * Every Angular worker, including ones deployed before this build, returns before matching any
 * cache rule when a request carries this header or query parameter (`ngsw-worker.js`,
 * `Driver.onFetch`). Protected requests carry it so that no worker version, old or new, can answer
 * them from storage.
 */
export const SERVICE_WORKER_BYPASS_HEADER = 'ngsw-bypass';

const SERVICE_WORKER_BYPASS_QUERY = 'ngsw-bypass=true';

/**
 * For raw `fetch` calls. A query parameter rather than the header: the header is a non-safelisted
 * request header, so on a cross-origin API (development) it would cost a CORS preflight.
 */
export function withServiceWorkerBypassQuery(url: string): string {
  const separator = url.includes('?') ? '&' : '?';
  return `${url}${separator}${SERVICE_WORKER_BYPASS_QUERY}`;
}

export interface ApiRequestRouting {
  /** Addressed to this application's API: the only requests that may carry its credentials. */
  readonly isApiRequest: boolean;
  /** An API request on the page's own origin, the only kind a worker can intercept and cache. */
  readonly bypassServiceWorker: boolean;
}

const NOT_API: ApiRequestRouting = { isApiRequest: false, bypassServiceWorker: false };

/**
 * Decides how a request is sent. `apiBaseUrl` may be relative (production, proxied on the page's
 * origin) or absolute (development); both, and the request URL, are resolved against `pageUrl`.
 */
export function classifyApiRequest(url: string, apiBaseUrl: string, pageUrl: string): ApiRequestRouting {
  let target: URL;
  let base: URL;
  try {
    target = new URL(url, pageUrl);
    base = new URL(apiBaseUrl, pageUrl);
  } catch {
    return NOT_API;
  }

  const basePath = base.pathname.replace(/\/+$/, '');
  const underBase = target.pathname === basePath || target.pathname.startsWith(`${basePath}/`);
  if (target.origin !== base.origin || !underBase) {
    return NOT_API;
  }

  return { isApiRequest: true, bypassServiceWorker: target.origin === new URL(pageUrl).origin };
}
