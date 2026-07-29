import { defineConfig } from '@playwright/test';

import { loadLiveEnv, requireLiveConfig } from '../support/live-api.fixture';

const liveConfig = requireLiveConfig(loadLiveEnv());
process.env['PLAYWRIGHT_NO_COPY_PROMPT'] = '1';
const appUrl = new URL(liveConfig.appBaseUrl);
const appPort = appUrl.port || (appUrl.protocol === 'https:' ? '443' : '80');

export default defineConfig({
  testDir: '.',
  testMatch: ['local-api-smoke.spec.ts'],
  outputDir: '../../../.ui-audit/live-test-results',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: liveConfig.appBaseUrl,
    browserName: 'chromium',
    colorScheme: 'light',
    locale: 'en-IN',
    timezoneId: 'Asia/Kolkata',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
  projects: [
    {
      name: 'chromium-live-local-api',
      use: {
        viewport: { width: 1440, height: 900 },
        deviceScaleFactor: 1,
        isMobile: false,
      },
    },
  ],
  webServer: {
    command: `bun run start -- --host ${appUrl.hostname} --port ${appPort}`,
    url: liveConfig.appBaseUrl,
    timeout: 120_000,
    reuseExistingServer: true,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
