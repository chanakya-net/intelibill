import { defineConfig } from '@playwright/test';

const evidence = process.env['P0608_EVIDENCE_DIR'];
if (!evidence) throw new Error('Set P0608_EVIDENCE_DIR.');

export default defineConfig({
  testMatch: 'p06-08-refund-request.spec.ts',
  outputDir: evidence + '/playwright',
  forbidOnly: true,
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
});
