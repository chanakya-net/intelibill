import { defineConfig } from '@playwright/test';

const evidence = process.env['P0213_EVIDENCE'];
if (!evidence) throw new Error('Set P0213_EVIDENCE to the explicit evidence directory.');

export default defineConfig({
  testMatch: 'p02-14-synchronization-status.spec.ts',
  outputDir: evidence + '/browser',
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 300_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 15_000, navigationTimeout: 30_000,
  },
});
