import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

/**
 * Publishes Intelibill's worker at the URL every build has registered.
 *
 * `ng build` writes Angular's worker as `ngsw-worker.js`. This moves it to `ngsw-angular-worker.js`
 * and writes `service-worker/protected-api-worker.js` (which imports it) as `ngsw-worker.js`. Keeping
 * the registered URL means the browser's update check installs the wrapper over any earlier worker,
 * and an older page registering `ngsw-worker.js` cannot reinstate Angular's bare one. Safe to run
 * twice on the same output. It then writes the full and lite offline-pack manifests
 * (`writeOfflinePackManifests`).
 */

export const WORKER_FILE = 'ngsw-worker.js';
export const ANGULAR_WORKER_FILE = 'ngsw-angular-worker.js';
export const WRAPPER_MARKER = '/* intelibill:protected-api-worker */';
export const FULL_MANIFEST_FILE = 'ngsw.json';
export const LITE_MANIFEST_FILE = 'ngsw-lite.json';

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const DEFAULT_BROWSER_DIR = join(frontendRoot, 'dist', 'INVENTORY', 'browser');
const DEFAULT_WRAPPER = join(frontendRoot, 'service-worker', 'protected-api-worker.js');

function isAngularWorker(source) {
  return source.includes('var Driver = class') && source.includes('ngsw:');
}

export function finalizeServiceWorker(browserDir = DEFAULT_BROWSER_DIR, wrapperPath = DEFAULT_WRAPPER) {
  const workerPath = join(browserDir, WORKER_FILE);
  const angularWorkerPath = join(browserDir, ANGULAR_WORKER_FILE);
  const wrapper = readFileSync(wrapperPath, 'utf8');
  if (!wrapper.startsWith(WRAPPER_MARKER)) {
    throw new Error(`${wrapperPath} does not start with ${WRAPPER_MARKER}.`);
  }

  if (!existsSync(workerPath)) {
    throw new Error(`No ${WORKER_FILE} in ${browserDir}: build with the production configuration first.`);
  }

  const built = readFileSync(workerPath, 'utf8');
  if (built.startsWith(WRAPPER_MARKER)) {
    if (!existsSync(angularWorkerPath) || !isAngularWorker(readFileSync(angularWorkerPath, 'utf8'))) {
      throw new Error(`${workerPath} is already the wrapper but ${ANGULAR_WORKER_FILE} is missing or not Angular's worker.`);
    }
    writeFileSync(workerPath, wrapper);
    return { moved: false };
  }

  if (!isAngularWorker(built)) {
    throw new Error(`${workerPath} is neither Angular's worker nor the wrapper; refusing to replace it.`);
  }

  writeFileSync(angularWorkerPath, built);
  writeFileSync(workerPath, wrapper);
  return { moved: true };
}

/**
 * Writes the two manifests the wrapper worker chooses between (docs/frontend-architecture.md
 * § Offline Pack).
 *
 * - `ngsw.json`, Angular's own manifest, is the full offline pack: every prefetch group is
 *   downloaded and hash-checked before the worker switches to it. Its `appData` gains
 *   `offlinePack: 'full'` and a `buildId` taken from the file hashes alone.
 * - `ngsw-lite.json` is the same manifest with no asset groups and no hashes, and navigations
 *   always go to the network: a device without offline billing downloads nothing in the background.
 *
 * The two differ as manifests (Angular treats each as its own version), so the page tells "only the
 * pack choice changed" from "a new build" by `buildId`, which `timestamp` does not disturb. Safe to
 * run twice on the same output.
 */
export function writeOfflinePackManifests(browserDir = DEFAULT_BROWSER_DIR) {
  const fullPath = join(browserDir, FULL_MANIFEST_FILE);
  if (!existsSync(fullPath)) {
    throw new Error(`No ${FULL_MANIFEST_FILE} in ${browserDir}: build with the production configuration first.`);
  }

  const generated = JSON.parse(readFileSync(fullPath, 'utf8'));
  if (!Array.isArray(generated.assetGroups) || typeof generated.hashTable !== 'object' || generated.hashTable === null) {
    throw new Error(`${fullPath} is not an Angular service worker manifest.`);
  }

  const buildId = createHash('sha1').update(JSON.stringify(generated.hashTable)).digest('hex').slice(0, 16);
  const appData = { ...(generated.appData ?? {}), buildId };
  const full = { ...generated, appData: { ...appData, offlinePack: 'full' } };
  const lite = {
    ...generated,
    appData: { ...appData, offlinePack: 'lite' },
    assetGroups: [],
    hashTable: {},
    navigationRequestStrategy: 'freshness',
  };

  writeFileSync(fullPath, `${JSON.stringify(full, null, 2)}\n`);
  writeFileSync(join(browserDir, LITE_MANIFEST_FILE), `${JSON.stringify(lite, null, 2)}\n`);
  return { buildId };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { moved } = finalizeServiceWorker();
  console.log(
    moved
      ? `Service worker: ${WORKER_FILE} now wraps ${ANGULAR_WORKER_FILE}.`
      : `Service worker: ${WORKER_FILE} already wraps ${ANGULAR_WORKER_FILE}; wrapper refreshed.`,
  );
  const { buildId } = writeOfflinePackManifests();
  console.log(`Offline pack: ${FULL_MANIFEST_FILE} (full) and ${LITE_MANIFEST_FILE} (lite) written for build ${buildId}.`);
}
