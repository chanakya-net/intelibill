import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p02-09-access-status.spec.ts',
  outputDir: process.env['P0209_EVIDENCE'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 300_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 15_000, navigationTimeout: 30_000,
  },
});
