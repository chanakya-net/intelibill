import { defineConfig } from '@playwright/test';

const evidence = process.env['P0304_EVIDENCE_DIR'];
if (!evidence) throw new Error('Set P0304_EVIDENCE_DIR.');
const output = process.env['P0304_PLAYWRIGHT_OUTPUT'];
if (!output) throw new Error('Set P0304_PLAYWRIGHT_OUTPUT.');

export default defineConfig({
  testMatch: /p03-04-razorpay-(purchase|callbacks|window-hold|hosted-failure)\.spec\.ts$/,
  outputDir: output,
  workers: 1,
  retries: 0,
  timeout: 100_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 12_000, navigationTimeout: 30_000,
  },
});
