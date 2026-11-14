import { defineConfig } from '@playwright/test';

/**
 * Chromium against the real images of an isolated full Compose stack, started by
 * scripts/dev/test-licensing-stack.sh --case browser. Nothing is mocked or intercepted:
 * the merchant web proxies to the merchant API and PostgreSQL, identity mail lands in the
 * stack's own Mailpit, and the admin web is served by its shipping runtime server.
 */
const artifacts = process.env['P0105_ARTIFACTS'] ?? '/tmp/intelibill-p01-05-browser';

export default defineConfig({
  testDir: '.',
  testMatch: ['*.spec.ts'],
  outputDir: `${artifacts}/playwright`,
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 120_000,
  reporter: [['list']],
  use: {
    browserName: 'chromium',
    locale: 'en-IN',
    timezoneId: 'Asia/Kolkata',
    serviceWorkers: 'allow',
    trace: 'on',
    screenshot: 'off',
    video: 'off',
  },
});
