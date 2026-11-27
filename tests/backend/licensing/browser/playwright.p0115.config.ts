import { defineConfig } from '@playwright/test';
import { resolve } from 'node:path';

const scratch = process.env['P0115_SCRATCH'];
if (!scratch) throw new Error('P0115_SCRATCH is required');

export default defineConfig({
  testMatch: 'p01-15-foundation.spec.ts',
  outputDir: resolve(scratch, 'browser'),
  workers: 1,
  retries: 0,
  timeout: 180_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 10_000, navigationTimeout: 15_000,
  },
});
