import { defineConfig } from '@playwright/test';

const evidence = process.env['P0613_EVIDENCE_DIR'];
if (!evidence) throw new Error('P0613_EVIDENCE_DIR is required.');

export default defineConfig({
  testMatch: 'p06-13-billing-document-supersession.spec.ts',
  outputDir: evidence + '/playwright',
  workers: 1,
  retries: 0,
  forbidOnly: true,
  timeout: 240_000,
  reporter: 'list',
  use: {
    browserName: 'chromium',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
    actionTimeout: 15_000,
  },
});
