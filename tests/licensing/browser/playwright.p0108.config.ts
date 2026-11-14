import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p01-08-mfa-enrollment.spec.ts',
  outputDir: process.env['P0108_ARTIFACTS'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 90_000,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
});
