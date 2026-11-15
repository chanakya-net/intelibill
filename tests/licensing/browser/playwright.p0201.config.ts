import { isAbsolute } from 'node:path';
import { defineConfig } from '@playwright/test';

const evidence = process.env['P0201_EVIDENCE'];
if (!evidence || !isAbsolute(evidence)) throw new Error('P0201_EVIDENCE must name an absolute private directory.');

export default defineConfig({
  testMatch: 'p02-01-delegation.spec.ts',
  outputDir: evidence + '/browser',
  workers: 1,
  retries: 0,
  timeout: 60_000,
  reporter: 'list',
  use: { browserName: 'chromium', trace: 'off', screenshot: 'off', video: 'off' },
});
