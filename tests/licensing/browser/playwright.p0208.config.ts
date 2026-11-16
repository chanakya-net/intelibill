import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p02-08-plan-retirement.spec.ts',
  outputDir: process.env['P0207_EVIDENCE'] + '/browser',
  workers: 1,
  retries: 0,
  timeout: 240_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 10_000, navigationTimeout: 20_000,
  },
});
