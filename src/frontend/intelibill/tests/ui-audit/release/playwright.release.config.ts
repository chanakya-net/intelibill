import { defineConfig } from '@playwright/test';

/**
 * Runs against the production build with service workers allowed: the behaviour under test lives in
 * the built worker, which development mode never registers. Build first (`bun run build`). Each spec
 * starts its own server, because a test swaps the manifest the worker sees mid-run.
 */
export default defineConfig({
  testDir: '.',
  testMatch: ['*.spec.ts'],
  outputDir: '../../../.ui-audit/release-test-results',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  timeout: 90_000,
  reporter: [['list']],
  use: {
    browserName: 'chromium',
    locale: 'en-IN',
    timezoneId: 'Asia/Kolkata',
    serviceWorkers: 'allow',
    trace: 'retain-on-failure',
    screenshot: 'off',
    video: 'off',
  },
});
