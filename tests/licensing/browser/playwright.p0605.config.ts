import { defineConfig } from '@playwright/test';

export default defineConfig({
  testMatch: 'p06-05-coverage-replacement.spec.ts', outputDir: process.env.P0605_EVIDENCE_DIR + '/playwright',
  workers: 1, retries: 0, timeout: 100_000, reporter: 'list',
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off', actionTimeout: 12_000 },
});
