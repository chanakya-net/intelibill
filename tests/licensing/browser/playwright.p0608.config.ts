import { join } from 'node:path';
import { defineConfig } from '@playwright/test';

const scratch = process.env['P0608_TEMPORARY'];
if (!scratch) throw new Error('Set P0608_TEMPORARY.');

export default defineConfig({
  testMatch: 'p06-08-refund-request.spec.ts',
  outputDir: join(scratch, 'playwright'),
  forbidOnly: true,
  workers: 1,
  retries: 0,
  timeout: 120_000,
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
