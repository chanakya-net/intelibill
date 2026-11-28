import { defineConfig } from '@playwright/test';
import { resolve } from 'node:path';

const scratch = process.env['P0115_SCRATCH'];
if (!scratch) throw new Error('P0702 private fixture directory required');

export default defineConfig({
  testMatch: 'p07-02-email-delivery.spec.ts',
  outputDir: resolve(scratch, 'browser'),
  workers: 1,
  retries: 0,
  timeout: 240_000,
  reporter: 'list',
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off' },
});
