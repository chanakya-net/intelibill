import { expect, test } from '@playwright/test';
import { access } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { startStaticServer } from './static-server';

const admin = resolve('dist/ADMIN/browser');

test('admin landing denies provider access and confirms a local display change', async ({
  page,
}) => {
  await access(join(admin, 'index.html')).catch(() => {
    throw new Error(`No admin build at ${admin}. Run \`bun run audit:release\` in src/admin-frontend.`);
  });
  const server = await startStaticServer(admin);
  try {
    await page.goto(server.origin + '/inventory');
    await expect(page.getByRole('heading', { name: 'Provider Console' })).toBeVisible();
    await expect(page).toHaveURL(server.origin + '/');
    await expect(page.getByRole('alert')).toHaveText('No provider session');
    await expect(page.getByText('No provider permissions')).toBeVisible();
    await expect(page.locator('.ib-form-section')).toHaveCSS('display', 'grid');
    await expect(page.getByRole('alert')).toHaveCSS('color', 'rgb(185, 28, 28)');

    const hideHelp = page.getByRole('button', { name: 'Hide access help' });
    const prompt = page.getByRole('alertdialog', { name: 'Hide access help?' });
    await hideHelp.click();
    await prompt.getByRole('button', { name: 'Cancel' }).click();
    await expect(hideHelp).toBeVisible();
    await hideHelp.click();
    await prompt.getByRole('button', { name: 'Hide help' }).click();
    await expect(page.getByRole('alert')).toHaveText('No provider session');
    await expect(page.getByText('No provider permissions')).toBeVisible();
    await expect(page.getByText('Provider access requires a provider session.')).toBeHidden();
    await page.getByRole('button', { name: 'Show access help' }).click();
    await expect(page.getByText('Provider access requires a provider session.')).toBeVisible();
    expect(server.paths.filter((path) => /^\/(api|hubs|ngsw)([\/.-]|$)/.test(path))).toEqual([]);
    expect(
      await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).length),
    ).toBe(0);
  } finally {
    await server.close();
  }
});
