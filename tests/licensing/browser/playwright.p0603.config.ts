import { defineConfig } from '@playwright/test';

const evidence = process.env['P0603_EVIDENCE_DIR'];
if (!evidence) throw new Error('Set P0603_EVIDENCE_DIR.');

export default defineConfig({
  testMatch: 'p06-03-capacity-reduction.spec.ts',
  outputDir: evidence + '/playwright',
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 15_000, navigationTimeout: 30_000,
  },
});
