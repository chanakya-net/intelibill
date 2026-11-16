import { defineConfig } from '@playwright/test';

const evidence = process.env['P0306_EVIDENCE_DIR'];
if (!evidence) throw new Error('Set P0306_EVIDENCE_DIR to an explicit evidence directory.');

export default defineConfig({
  testMatch: 'p03-06-manual-payment.spec.ts',
  outputDir: evidence + '/playwright',
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 120_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 15_000, navigationTimeout: 30_000,
  },
});
