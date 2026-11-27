import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

import { expect, test } from '@playwright/test';

import {
  assertNoUnexpectedBrowserFailures,
  collectBrowserFailures,
  compareScreenshot,
  isBaselineUpdateEnabled,
  mockExternalRequests,
  waitForStablePage,
} from './support/audit-page';
import { AUDIT_VIEWPORTS, assertLoginLayout } from './support/layout-assertions';
import { writeRedactedArtifacts } from './support/artifact-redaction';

test.describe('login page UI audit', () => {
  test('renders without layout or browser failures', async ({ page }, testInfo) => {
    const viewport = getAuditViewport(testInfo.project.name);
    const collector = collectBrowserFailures(page);
    const artifactName = `login-${viewport.name}`;
    const result: Record<string, unknown> = {
      page: '/login',
      viewport,
      baselineMode: isBaselineUpdateEnabled(process.env),
      status: 'running',
      failures: collector.failures,
    };

    try {
      await mockExternalRequests(page);
      await page.goto('/login');
      await waitForStablePage(page);
      await assertLoginLayout(page, viewport);
      await expect(page.locator('.auth-card')).toBeVisible();
      await expect(page.locator('#identifier')).toBeVisible();
      await expect(page.locator('#password')).toBeVisible();
      await expect(page.locator('button[type="submit"]')).toBeVisible();

      const screenshot = await compareScreenshot({
        page,
        baselinePath: join(process.cwd(), '.ui-audit', 'baselines', `${artifactName}.png`),
        updateBaseline: isBaselineUpdateEnabled(process.env),
      });
      result.screenshot = { status: screenshot.status };
      await writeScreenshot(artifactName, screenshot.bytes);
      assertNoUnexpectedBrowserFailures(collector.failures);
      result.status = 'passed';
    } catch (error) {
      result.status = 'failed';
      result.error = error instanceof Error ? error.message : String(error);
      throw error;
    } finally {
      result.failures = collector.failures;
      await writeRedactedArtifacts({
        outputDir: join(process.cwd(), '.ui-audit', 'reports'),
        name: artifactName,
        html: await page.content().catch(() => ''),
        result,
      });
      collector.dispose();
    }
  });
});

function getAuditViewport(projectName: string) {
  const viewport = projectName.includes('mobile') ? AUDIT_VIEWPORTS[0] : AUDIT_VIEWPORTS[1];
  return viewport;
}

async function writeScreenshot(name: string, bytes: Buffer): Promise<void> {
  const path = join(process.cwd(), '.ui-audit', 'screenshots', `${name}.png`);
  await mkdir(join(process.cwd(), '.ui-audit', 'screenshots'), { recursive: true });
  await writeFile(path, bytes);
}
