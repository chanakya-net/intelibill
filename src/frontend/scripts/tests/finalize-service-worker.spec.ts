import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import {
  ANGULAR_WORKER_FILE,
  WORKER_FILE,
  WRAPPER_MARKER,
  finalizeServiceWorker,
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
