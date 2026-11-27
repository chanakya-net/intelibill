import { defineConfig } from '@playwright/test';

const evidence = process.env['P0418_BROWSER_EVIDENCE'];
if (!evidence) throw new Error('Set P0418_BROWSER_EVIDENCE.');

export default defineConfig({
  testMatch: 'p04-18-enforcement.spec.ts',
  outputDir: evidence + '/playwright',
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  use: {
    browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off',
    actionTimeout: 12_000, navigationTimeout: 30_000,
  },
});
