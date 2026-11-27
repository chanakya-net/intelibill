import { join } from 'node:path';
import { defineConfig } from '@playwright/test';

const scratch = process.env['P0606_TEMPORARY'];
if (!scratch) throw new Error('Set P0606_TEMPORARY.');

export default defineConfig({
  testMatch: 'p06-06-coverage-exception.spec.ts',
  outputDir: join(scratch, 'playwright'),
  forbidOnly: true,
  workers: 1,
  retries: 0,
  timeout: 180_000,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
});
