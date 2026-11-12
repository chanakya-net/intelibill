import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  testMatch: ['*.spec.ts'],
  outputDir: '../../../.ui-audit/onboarding-test-results',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  timeout: 120_000,
  reporter: [['list']],
  use: {
    browserName: 'chromium',
    actionTimeout: 12_000,
    navigationTimeout: 25_000,
    locale: 'en-IN',
    timezoneId: 'Asia/Kolkata',
    serviceWorkers: 'allow',
    trace: 'retain-on-failure',
  },
});
