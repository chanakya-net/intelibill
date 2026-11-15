import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-14-audit.spec.ts',
  outputDir: process.env['P0114_ARTIFACTS'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 90_000,
  reporter: 'list',
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off' },
});
