import { expect, test } from '@playwright/test';
import { access, readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { startReleaseServer } from './release-server';

const merchant = resolve('dist/INVENTORY/browser');

test('merchant login retains its appearance and loads only the auth style kit', async ({
  page,
}) => {
  const server = await startReleaseServer(merchant);
  try {
    await page.route('**/api/auth/phone/availability', (route) =>
      route.fulfill({ json: { enabled: false } }),
    );
    await page.goto(server.origin + '/login');
    await expect(page.locator('.ib-auth-card')).toBeVisible();
    await expect(page.locator('app-auth-kit-styles')).toHaveCount(1);
    await expect(page.locator('app-ui-kit-styles')).toHaveCount(0);
    await expect(page.locator('.ib-auth-card')).toHaveCSS('max-width', '416px');
    await expect(page.locator('body')).toHaveCSS('font-family', /IBM Plex Sans/);
    await expect(page.locator('html')).toHaveCSS('--app-orange-500', '#f97316');

    const css = await page.locator('head style').allTextContents();
    expect(css.join('\n')).toContain('.ib-auth-card');
    expect(css.join('\n')).not.toMatch(/\.ib-overlay|\.ib-panel/);

    const scripts = [
      ...new Set(server.requests.map(({ path }) => path).filter((path) => path.endsWith('.js'))),
    ];
    for (const script of scripts.filter((path) => !path.startsWith('/ngsw-'))) {
      expect(await readFile(join(merchant, script), 'utf8')).not.toContain('.ib-overlay');
    }
  } finally {
    await server.close();
  }
});

test('merchant ships the protected wrapper with both offline packs and the shared fonts', async () => {
  const read = (path: string) => readFile(join(merchant, path), 'utf8');
  expect(await read('ngsw-worker.js')).toMatch(/^\/\* intelibill:protected-api-worker \*\//);
  await access(join(merchant, 'ngsw-angular-worker.js'));

  const full = JSON.parse(await read('ngsw.json'));
  const lite = JSON.parse(await read('ngsw-lite.json'));
  expect(full.appData.offlinePack).toBe('full');
  expect(lite.appData.offlinePack).toBe('lite');
  expect(lite.appData.buildId).toBe(full.appData.buildId);
  expect(lite.assetGroups).toEqual([]);
  expect(full.dataGroups).toEqual([]);

  const prefetched: string[] = full.assetGroups
    .filter(({ installMode }: { installMode: string }) => installMode === 'prefetch')
    .flatMap(({ urls }: { urls: string[] }) => urls);
  expect(prefetched.some((url) => /^\/(api|hubs)(\/|$)/.test(url))).toBe(false);
  const fonts = prefetched.filter((url) => url.startsWith('/fonts/') && url.endsWith('.woff2'));
  expect(fonts).toHaveLength(8);
  await Promise.all(fonts.map((font) => access(join(merchant, font))));
});
