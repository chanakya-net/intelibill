import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-09-sign-in.spec.ts',
  outputDir: process.env['P0109_ARTIFACTS'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off' },
});
