import { defineConfig } from '@playwright/test';

/**
 * Runs the admin production build in Chromium. `bun run audit:release` builds the app first, so the
 * suite never depends on the merchant app or on output left by an earlier build.
 */
export default defineConfig({
  testDir: '.',
  testMatch: ['*.spec.ts'],
  outputDir: '../../test-results/release',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  timeout: 60_000,
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
