import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import {
  ANGULAR_WORKER_FILE,
  FULL_MANIFEST_FILE,
  LITE_MANIFEST_FILE,
  WORKER_FILE,
  WRAPPER_MARKER,
  finalizeServiceWorker,
  writeOfflinePackManifests,
} from '../finalize-service-worker.mjs';

const frontendRoot = join(import.meta.dir, '..', '..');
const WRAPPER_PATH = join(frontendRoot, 'service-worker', 'protected-api-worker.js');
const ANGULAR_WORKER = '(() => { var Driver = class {}; const prefix = "ngsw:"; })();\n';

describe('finalizeServiceWorker', () => {
  let browserDir: string;

  beforeEach(() => {
    browserDir = mkdtempSync(join(tmpdir(), 'finalize-sw-'));
  });

  afterEach(() => {
    rmSync(browserDir, { recursive: true, force: true });
  });

  it('publishes the wrapper at the registered URL and Angular beside it', () => {
    writeFileSync(join(browserDir, WORKER_FILE), ANGULAR_WORKER);

    expect(finalizeServiceWorker(browserDir, WRAPPER_PATH)).toEqual({ moved: true });

    expect(readFileSync(join(browserDir, WORKER_FILE), 'utf8')).toBe(readFileSync(WRAPPER_PATH, 'utf8'));
    expect(readFileSync(join(browserDir, ANGULAR_WORKER_FILE), 'utf8')).toBe(ANGULAR_WORKER);
  });

  it('can run twice without wrapping the wrapper', () => {
    writeFileSync(join(browserDir, WORKER_FILE), ANGULAR_WORKER);
    finalizeServiceWorker(browserDir, WRAPPER_PATH);

    expect(finalizeServiceWorker(browserDir, WRAPPER_PATH)).toEqual({ moved: false });

    expect(readFileSync(join(browserDir, WORKER_FILE), 'utf8').startsWith(WRAPPER_MARKER)).toBe(true);
    expect(readFileSync(join(browserDir, ANGULAR_WORKER_FILE), 'utf8')).toBe(ANGULAR_WORKER);
  });

  it('refuses a build without a worker', () => {
    expect(() => finalizeServiceWorker(browserDir, WRAPPER_PATH)).toThrow(/production configuration/);
  });

  it('refuses to replace a worker it does not recognise', () => {
    writeFileSync(join(browserDir, WORKER_FILE), 'self.addEventListener("fetch", () => {});');

    expect(() => finalizeServiceWorker(browserDir, WRAPPER_PATH)).toThrow(/refusing to replace/);
  });

  it('refuses a wrapper whose Angular worker went missing', () => {
    writeFileSync(join(browserDir, WORKER_FILE), readFileSync(WRAPPER_PATH, 'utf8'));

    expect(() => finalizeServiceWorker(browserDir, WRAPPER_PATH)).toThrow(/missing or not Angular's worker/);
  });
});

describe('writeOfflinePackManifests', () => {
  let browserDir: string;

  const GENERATED = {
    configVersion: 1,
    timestamp: 1_700_000_000_000,
    index: '/index.html',
    assetGroups: [
      { name: 'app', installMode: 'prefetch', updateMode: 'prefetch', cacheQueryOptions: { ignoreVary: true }, urls: ['/index.html', '/main-ABC.js'], patterns: [] },
      { name: 'offline', installMode: 'prefetch', updateMode: 'prefetch', cacheQueryOptions: { ignoreVary: true }, urls: ['/assets/i18n/en-IN.json'], patterns: [] },
    ],
    dataGroups: [],
    hashTable: { '/index.html': 'aaa', '/main-ABC.js': 'bbb', '/assets/i18n/en-IN.json': 'ccc' },
    navigationUrls: [{ positive: true, regex: '^\\/.*$' }],
    navigationRequestStrategy: 'performance',
  };

  const read = (name: string) => JSON.parse(readFileSync(join(browserDir, name), 'utf8'));

  beforeEach(() => {
    browserDir = mkdtempSync(join(tmpdir(), 'offline-pack-'));
  });

  afterEach(() => {
    rmSync(browserDir, { recursive: true, force: true });
  });

  it('keeps the generated manifest as the full pack and derives a lite one with nothing to download', () => {
    writeFileSync(join(browserDir, FULL_MANIFEST_FILE), JSON.stringify(GENERATED));

    const { buildId } = writeOfflinePackManifests(browserDir);

    const full = read(FULL_MANIFEST_FILE);
    const lite = read(LITE_MANIFEST_FILE);
    expect(buildId).toMatch(/^[0-9a-f]{16}$/);
    expect(full).toEqual({ ...GENERATED, appData: { buildId, offlinePack: 'full' } });
    expect(lite).toEqual({
      ...GENERATED,
      appData: { buildId, offlinePack: 'lite' },
      assetGroups: [],
      hashTable: {},
      navigationRequestStrategy: 'freshness',
    });
    // Navigation and protected-path rules are the full manifest's, unchanged.
    expect(lite.navigationUrls).toEqual(full.navigationUrls);
    expect(lite.dataGroups).toEqual([]);
  });

  it('names the build by its files, not by when it was built', () => {
    writeFileSync(join(browserDir, FULL_MANIFEST_FILE), JSON.stringify(GENERATED));
    const first = writeOfflinePackManifests(browserDir).buildId;

    writeFileSync(join(browserDir, FULL_MANIFEST_FILE), JSON.stringify({ ...GENERATED, timestamp: 1 }));
    expect(writeOfflinePackManifests(browserDir).buildId).toBe(first);

    writeFileSync(
      join(browserDir, FULL_MANIFEST_FILE),
      JSON.stringify({ ...GENERATED, hashTable: { ...GENERATED.hashTable, '/main-ABC.js': 'changed' } }),
    );
    expect(writeOfflinePackManifests(browserDir).buildId).not.toBe(first);
  });

  it('can run twice on the same output', () => {
    writeFileSync(join(browserDir, FULL_MANIFEST_FILE), JSON.stringify({ ...GENERATED, appData: { release: 'r1' } }));
    writeOfflinePackManifests(browserDir);
    const once = { full: read(FULL_MANIFEST_FILE), lite: read(LITE_MANIFEST_FILE) };

    writeOfflinePackManifests(browserDir);

    expect({ full: read(FULL_MANIFEST_FILE), lite: read(LITE_MANIFEST_FILE) }).toEqual(once);
    expect(once.full.appData.release).toBe('r1');
  });

  it('refuses a build without a manifest or with something else in its place', () => {
    expect(() => writeOfflinePackManifests(browserDir)).toThrow(/production configuration/);

    writeFileSync(join(browserDir, FULL_MANIFEST_FILE), '{"hello":"world"}');
    expect(() => writeOfflinePackManifests(browserDir)).toThrow(/not an Angular service worker manifest/);
  });
});
