import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-10-refresh.spec.ts',
  outputDir: process.env['P0110_ARTIFACTS'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 180_000,
  reporter: 'list',
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off' },
});
