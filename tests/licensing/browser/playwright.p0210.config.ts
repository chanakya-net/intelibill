import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p02-10-shop-trial.spec.ts',
  outputDir: process.env['P0210_EVIDENCE_DIR'] + '/playwright',
  workers: 1,
  retries: 0,
  timeout: 150_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 15_000, navigationTimeout: 30_000,
  },
});
