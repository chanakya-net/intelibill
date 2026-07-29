import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/ui-audit',
  testMatch: ['ui-audit/login-page.spec.ts', 'ui-audit/mock/*.spec.ts', 'ui-audit/print/*.spec.ts'],
  outputDir: '.ui-audit/test-results',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:4300',
    browserName: 'chromium',
    colorScheme: 'light',
    locale: 'en-IN',
    timezoneId: 'Asia/Kolkata',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [
    {
      name: 'chromium-mobile',
      use: { viewport: { width: 360, height: 800 }, deviceScaleFactor: 1, isMobile: false },
    },
    {
      name: 'chromium-desktop',
      use: { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1, isMobile: false },
    },
  ],
  webServer: {
    command: 'bun run start -- --host 127.0.0.1 --port 4300',
    url: 'http://127.0.0.1:4300',
    timeout: 120_000,
    reuseExistingServer: false,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
