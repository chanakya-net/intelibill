import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-11-sessions.spec.ts',
  outputDir: process.env['P0111_ARTIFACTS'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 180_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 10_000, navigationTimeout: 15_000,
  },
});
