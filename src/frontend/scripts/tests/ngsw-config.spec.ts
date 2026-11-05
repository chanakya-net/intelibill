import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { Generator, type Config, type Filesystem } from '@angular/service-worker/config';
import { describe, expect, it } from 'vitest';

/**
 * The worker configuration is compiled by Angular's own generator, the one `ng build` runs, so the
 * assertions read the manifest the production worker would load rather than the glob strings.
 */

const frontendRoot = join(import.meta.dir, '..', '..');
const config = JSON.parse(readFileSync(join(frontendRoot, 'ngsw-config.json'), 'utf8')) as Config;

const BUILT_FILES: Readonly<Record<string, string>> = {
  '/index.html': '<!doctype html><base href="/">',
  '/index.csr.html': '<!doctype html><base href="/">',
  '/main-ABC123.js': 'main',
  '/chunk-DEF456.js': 'chunk',
  '/styles-GHI789.css': 'body{}',
  '/app-worker.js': 'importScripts("./ngsw-worker.js")',
  '/favicon.ico': 'ico',
  '/manifest.webmanifest': '{}',
  '/assets/i18n/en-IN.json': '{}',
  '/fonts/inter.woff2': 'font',
  '/icons/icon-192.png': 'png',
};

class MemoryFilesystem implements Filesystem {
  async list(dir: string): Promise<string[]> {
    const prefix = dir.endsWith('/') ? dir : `${dir}/`;
    return Object.keys(BUILT_FILES).filter((file) => file.startsWith(prefix));
  }

  async read(file: string): Promise<string> {
    return BUILT_FILES[file] ?? '';
  }

  async hash(file: string): Promise<string> {
    return createHash('sha1').update(BUILT_FILES[file] ?? '').digest('hex');
  }

  async write(): Promise<void> {}
}

interface GeneratedManifest {
  readonly assetGroups: readonly { name: string; urls: string[]; patterns: string[] }[];
  readonly dataGroups?: readonly { name: string; patterns: string[] }[];
  readonly navigationUrls: readonly { positive: boolean; regex: string }[];
}

async function generate(): Promise<GeneratedManifest> {
  return (await new Generator(new MemoryFilesystem(), '/').process(config)) as GeneratedManifest;
}

const PROTECTED_URLS = [
  '/api/customers',
  '/api/sales',
  '/api/bank-accounts',
  '/api/sales/offline-snapshot/stream',
  '/api/ping',
  '/api/items/stream',
  '/api/reports/export.json',
  '/api/x/y?z=1',
  '/hubs/products/negotiate',
];

/** Data groups are tested against the absolute request URL; both forms are checked. */
function candidates(path: string): string[] {
  return [path, `https://shop.test${path}`];
}

/** Mirrors the worker's navigation test: path without query, every include, no exclude. */
function isNavigationUrl(manifest: GeneratedManifest, path: string): boolean {
  const bare = path.replace(/[?#].*$/, '');
  const include = manifest.navigationUrls.filter((rule) => rule.positive).map((rule) => new RegExp(rule.regex));
  const exclude = manifest.navigationUrls.filter((rule) => !rule.positive).map((rule) => new RegExp(rule.regex));
  return include.some((regex) => regex.test(bare)) && !exclude.some((regex) => regex.test(bare));
}

describe('ngsw-config.json', () => {
  it('protected_api_not_in_generated_data_groups', async () => {
    const manifest = await generate();

    for (const group of manifest.dataGroups ?? []) {
      for (const pattern of group.patterns) {
        const regex = new RegExp(pattern);
        for (const url of PROTECTED_URLS.flatMap(candidates)) {
          expect(regex.test(url), `${group.name} must not match ${url}`).toBe(false);
        }
      }
    }
  });

  it('never lists or matches a protected URL in an asset group', async () => {
    const manifest = await generate();

    for (const group of manifest.assetGroups) {
      expect(group.urls.filter((url) => url.startsWith('/api/') || url.startsWith('/hubs/'))).toEqual([]);
      for (const pattern of group.patterns) {
        const regex = new RegExp(pattern);
        for (const url of PROTECTED_URLS.flatMap(candidates)) {
          expect(regex.test(url), `${group.name} must not match ${url}`).toBe(false);
        }
      }
    }
  });

  it('keeps the app shell and its assets available to the worker', async () => {
    const manifest = await generate();
    const listed = manifest.assetGroups.flatMap((group) => group.urls);

    for (const file of [
      '/index.html',
      '/main-ABC123.js',
      '/chunk-DEF456.js',
      '/styles-GHI789.css',
      '/favicon.ico',
      '/manifest.webmanifest',
      '/assets/i18n/en-IN.json',
      '/fonts/inter.woff2',
      '/icons/icon-192.png',
    ]) {
      expect(listed, file).toContain(file);
    }
  });

  it('leaves the worker script itself to the browser', async () => {
    const manifest = await generate();

    expect(manifest.assetGroups.flatMap((group) => group.urls)).not.toContain('/app-worker.js');
  });

  it('never answers a navigation to a protected path with the app shell', async () => {
    const manifest = await generate();

    expect(isNavigationUrl(manifest, '/sales/new')).toBe(true);
    expect(isNavigationUrl(manifest, '/customers/42?tab=ledger')).toBe(true);
    expect(isNavigationUrl(manifest, '/api/customers')).toBe(false);
    expect(isNavigationUrl(manifest, '/api/sales/123/document')).toBe(false);
    expect(isNavigationUrl(manifest, '/hubs/products')).toBe(false);
    // Angular's own exclusions are kept: files and harness-style `__` paths.
    expect(isNavigationUrl(manifest, '/assets/i18n/en-IN.json')).toBe(false);
    expect(isNavigationUrl(manifest, '/__harness')).toBe(false);
  });
});
