import { defineConfig } from '@playwright/test';

const evidence = process.env['P0211_EVIDENCE_DIR'];
if (!evidence) throw new Error('Set P0211_EVIDENCE_DIR to the explicit evidence directory.');

export default defineConfig({
  testMatch: 'p02-11-trial-extension.spec.ts',
  outputDir: evidence + '/playwright',
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
