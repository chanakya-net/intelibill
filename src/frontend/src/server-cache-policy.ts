/**
 * Files at the build root whose name never changes between deployments. A browser must revalidate
 * them on every load: `index.html` names the hashed bundles, and the worker files decide which
 * version runs. A year-long copy of `ngsw-worker.js` would also be what `app-worker.js` imports.
 */
const REVALIDATED_ROOT_FILES: ReadonlySet<string> = new Set([
  'index.html',
  'index.csr.html',
  'ngsw.json',
  'ngsw-worker.js',
  'app-worker.js',
  'safety-worker.js',
  'worker-basic.min.js',
  'manifest.webmanifest',
]);

/** The Cache-Control for a file under the browser build folder, or null for the static default. */
export function cacheControlFor(relativePath: string): string | null {
  const normalized = relativePath.replaceAll('\\', '/');
  return REVALIDATED_ROOT_FILES.has(normalized) ? 'no-cache' : null;
}
