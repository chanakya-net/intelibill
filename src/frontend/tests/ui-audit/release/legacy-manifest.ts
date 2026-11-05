import { createHash } from 'node:crypto';
import { readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';

import { Generator, type Config, type Filesystem } from '@angular/service-worker/config';

/**
 * `src/frontend/ngsw-config.json` as deployed up to `fbff57b09` (unchanged since `7ff1de03f`),
 * verbatim: the configuration whose `api-fresh` group replayed one shop's answers to another.
 */
export const LEGACY_NGSW_CONFIG: Config = {
  index: '/index.html',
  assetGroups: [
    {
      name: 'app',
      installMode: 'prefetch',
      resources: {
        files: ['/favicon.ico', '/index.csr.html', '/index.html', '/manifest.webmanifest', '/*.css', '/*.js'],
      },
    },
    {
      name: 'assets',
      installMode: 'lazy',
      updateMode: 'prefetch',
      resources: {
        files: ['/**/*.(svg|cur|jpg|jpeg|png|apng|webp|avif|gif|otf|ttf|woff|woff2|json)'],
      },
    },
  ],
  dataGroups: [
    {
      name: 'ping-no-cache',
      urls: ['/api/ping'],
      cacheConfig: { strategy: 'freshness', maxSize: 1, maxAge: '0s', timeout: '5s' },
    },
    {
      name: 'offline-sales-api-no-cache',
      urls: ['/api/sales/offline-snapshot/stream', '/api/sales/invoice-leases/reserve', '/api/sales/offline-sync'],
      cacheConfig: { strategy: 'freshness', maxSize: 1, maxAge: '0s', timeout: '5s' },
    },
    {
      name: 'api-fresh',
      urls: ['/api/**'],
      cacheConfig: { strategy: 'freshness', maxSize: 100, maxAge: '1h', timeout: '8s' },
    },
  ],
};

/** Files the CLI writes after generating the manifest, so a deployed manifest never lists them. */
const WORKER_FILES = new Set(['/ngsw.json', '/ngsw-worker.js', '/ngsw-angular-worker.js', '/safety-worker.js', '/worker-basic.min.js']);

class BuildFilesystem implements Filesystem {
  constructor(private readonly root: string) {}

  async list(dir: string): Promise<string[]> {
    const entries = await readdir(join(this.root, dir), { recursive: true, withFileTypes: true });
    return entries
      .filter((entry) => entry.isFile())
      .map((entry) => `/${relative(this.root, join(entry.parentPath, entry.name)).split(sep).join('/')}`)
      .filter((file) => !WORKER_FILES.has(file));
  }

  async read(file: string): Promise<string> {
    return readFile(join(this.root, file), 'utf8');
  }

  async hash(file: string): Promise<string> {
    return createHash('sha1').update(await readFile(join(this.root, file))).digest('hex');
  }

  async write(): Promise<void> {}
}

/** The manifest Angular's generator produces for the current build files under the old config. */
export async function buildLegacyManifest(root: string): Promise<object> {
  return new Generator(new BuildFilesystem(root), '/').process(LEGACY_NGSW_CONFIG);
}
