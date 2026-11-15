import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-13-capabilities.spec.ts',
  outputDir: process.env['P0113_ARTIFACTS'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 420_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 10_000, navigationTimeout: 15_000,
  },
});
