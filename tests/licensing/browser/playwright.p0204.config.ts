import { isAbsolute } from 'node:path';
import { defineConfig } from '@playwright/test';

const evidence = process.env['P0204_EVIDENCE'];
if (!evidence || !isAbsolute(evidence)) throw new Error('P0204_EVIDENCE must name an absolute private directory.');

export default defineConfig({
  testMatch: 'p02-04-billing-ownership-transfer.spec.ts',
  outputDir: evidence + '/browser',
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: 'list',
  expect: { timeout: 15_000 },
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off', actionTimeout: 15_000, navigationTimeout: 30_000 },
});
