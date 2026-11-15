import { expect, test } from '@playwright/test';
import { access } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { startStaticServer } from './static-server';

const admin = resolve('dist/ADMIN/browser');

test('admin release requests only its own auth API and sends unauthenticated routes to sign-in', async ({ page }) => {
  await access(join(admin, 'index.html'));
  const server = await startStaticServer(admin);
  try {
    await page.goto(server.origin + '/inventory');
    await expect(page).toHaveURL(server.origin + '/auth/sign-in');
    await expect(page.getByRole('heading', { name: 'Sign in to the Provider Console' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Granted capabilities' })).toHaveCount(0);
    await expect(page.getByRole('alert')).toHaveText('Provider sign-in is unavailable. Try again.');
    expect(server.paths.filter((path) => /^\/(api|hubs|ngsw)([\/.-]|$)/.test(path))).toEqual([
      '/api/admin/auth/session',
      '/api/admin/auth/sign-in/context',
    ]);
    expect(await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).length)).toBe(0);
  } finally {
    await server.close();
  }
});
