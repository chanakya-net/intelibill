import { defineConfig } from '@playwright/test';

const evidence = process.env['P0302_EVIDENCE_DIR'];
if (!evidence) throw new Error('Set P0302_EVIDENCE_DIR.');

export default defineConfig({
  testMatch: 'p03-02-simulator-purchase.spec.ts',
  outputDir: evidence + '/playwright',
  workers: 1,
  retries: 0,
  timeout: 100_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 12_000, navigationTimeout: 30_000,
  },
});
