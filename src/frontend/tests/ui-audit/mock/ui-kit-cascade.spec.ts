import { expect, test, type Page } from '@playwright/test';

import { mockExternalRequests, waitForStablePage } from '../support/audit-page';
import { createShellScenario, installShellFixture } from '../fixtures/shell.fixture';

/**
 * The shared UI kit (`UiKitStylesComponent`) is delivered with the shell, never with the login
 * page, and sits in `@layer ib-kit` after PrimeNG's `primeng` layer, Tailwind's layers and the
 * global element resets (`ib-base`, from styles.css):
 * PrimeNG < Tailwind utilities < element resets < kit < unlayered component styles. See docs/superpowers/plans/2026-11-10-shared-styles-and-offline-pack.md § 3.1.
 */

/** Cascade layer names in the order the document establishes them (first appearance wins). */
async function layerOrder(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const order: string[] = [];
    const add = (name: string) => {
      if (name && !order.includes(name)) order.push(name);
    };
    const walk = (rules: CSSRuleList) => {
      for (const rule of Array.from(rules)) {
        if (rule instanceof CSSLayerStatementRule) rule.nameList.forEach(add);
        else if (rule instanceof CSSLayerBlockRule) add(rule.name);
        else if (rule instanceof CSSImportRule && rule.styleSheet) {
          if (rule.layerName) add(rule.layerName);
          walk(rule.styleSheet.cssRules);
        }
      }
    };
    for (const sheet of Array.from(document.styleSheets)) {
      try {
        walk(sheet.cssRules);
      } catch {
        // Cross-origin sheets cannot be read; the app has none.
      }
    }
    return order;
  });
}

test.describe('ui kit cascade', () => {
  test('is absent from the login page', async ({ page }) => {
    await mockExternalRequests(page);
    await page.goto('/login');
    await waitForStablePage(page);

    const order = await layerOrder(page);
    expect(order).not.toContain('ib-kit');
    // The element resets are layered on every page, still after Tailwind's utilities.
    expect(order.indexOf('ib-base')).toBeGreaterThan(order.indexOf('utilities'));
    await expect(page.locator('app-ui-kit-styles')).toHaveCount(0);
  });

  test('loads with the shell, after PrimeNG and below unlayered component styles', async ({ page }, testInfo) => {
    await installShellFixture(page, createShellScenario({ role: 'Owner' }));
    await page.goto('/sales');
    await waitForStablePage(page);
    await expect(page.locator('.app-shell')).toBeVisible();

    const order = await layerOrder(page);
    expect(order).toContain('primeng');
    expect(order.indexOf('ib-kit')).toBeGreaterThan(order.indexOf('primeng'));
    // Kit rules replace unlayered component rules, which beat Tailwind utilities today.
    expect(order.indexOf('ib-kit')).toBeGreaterThan(order.indexOf('utilities'));
    // Global heading and paragraph resets keep beating utilities, and lose to kit rules.
    expect(order.indexOf('ib-base')).toBeGreaterThan(order.indexOf('utilities'));
    expect(order.indexOf('ib-kit')).toBeGreaterThan(order.indexOf('ib-base'));

    const probe = async () =>
      page.evaluate(() => {
        document.getElementById('ib-cascade-root')?.remove();
        const root = document.createElement('div');
        root.id = 'ib-cascade-root';
        root.innerHTML =
          '<button class="p-button ib-cascade-probe">kit</button>' +
          '<span class="ib-cascade-probe ib-cascade-own">component</span>' +
          '<h2 class="ib-cascade-probe ib-cascade-heading">heading</h2>';
        document.body.append(root);
        // Appended the way Angular adds component styles. The PrimeNG rule is the most specific,
        // the component rule the least: only the layer order can decide these.
        for (const css of [
          '@layer primeng { html body #ib-cascade-root .ib-cascade-probe.p-button, html body #ib-cascade-root .ib-cascade-probe { color: rgb(10, 10, 10); background-color: rgb(11, 11, 11); } }',
          '@layer ib-kit { #ib-cascade-root .ib-cascade-probe { color: rgb(1, 2, 3); background-color: rgb(4, 5, 6); margin-top: 7px; } }',
          '.ib-cascade-own { color: rgb(7, 8, 9); }',
        ]) {
          const style = document.createElement('style');
          style.dataset['cascadeProbe'] = '';
          style.textContent = css;
          document.head.append(style);
        }
        const read = (selector: string) => {
          const computed = getComputedStyle(root.querySelector(selector)!);
          return { color: computed.color, background: computed.backgroundColor };
        };
        const result = {
          kit: read('.p-button'),
          own: read('.ib-cascade-own'),
          // styles.css sets `margin: 0` on h2 in `ib-base`; the kit's margin must win.
          headingMarginTop: getComputedStyle(root.querySelector('.ib-cascade-heading')!).marginTop,
        };
        root.remove();
        document.querySelectorAll('style[data-cascade-probe]').forEach((style) => style.remove());
        return result;
      });

    expect(await probe()).toEqual({
      kit: { color: 'rgb(1, 2, 3)', background: 'rgb(4, 5, 6)' },
      own: { color: 'rgb(7, 8, 9)', background: 'rgb(4, 5, 6)' },
      headingMarginTop: '7px',
    });

    // The kit stays while routes change inside the shell.
    if (testInfo.project.name.includes('desktop')) {
      await page.locator('.shell-sidebar a[href="/expenses"]').first().click();
      await expect(page).toHaveURL(/\/expenses$/);
      await waitForStablePage(page);
      expect(await layerOrder(page)).toContain('ib-kit');
      expect((await probe()).kit.color).toBe('rgb(1, 2, 3)');
    }
  });
});
