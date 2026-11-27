import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-06-bootstrap.spec.ts',
  outputDir: process.env['P0105_ARTIFACTS'] + '/p0106-browser',
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
