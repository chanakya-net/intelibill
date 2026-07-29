import { expect, test } from '@playwright/test';

import { assertNoUnexpectedBrowserFailures, collectBrowserFailures } from '../support/audit-page';
import {
  DENSE_SERVICES,
  FIRST_PAGE_LONG_MARKER,
  HINDI_SERVICE_LABELS,
  SECOND_PAGE_LONG_MARKER,
  SERVICES,
  assertLocalizedEditOverlay,
  assertLocalizedServiceList,
  assertLocalizedStatusFilterOptions,
  assertNoHorizontalOverflow,
  assertServiceValueFitsContainer,
  fillServiceForm,
  openServices,
  submitInvalidServiceForm,
  visibleServices,
} from './services.support';

test.describe('services', () => {
  test('renders service data as a table on desktop and cards on mobile', async ({
    page,
  }, testInfo) => {
    const collector = collectBrowserFailures(page);
    try {
      await openServices(page);

      if (testInfo.project.name === 'chromium-mobile') {
        await expect(page.locator('.service-card')).toHaveCount(SERVICES.length);
        await expect(page.locator('.service-card').first()).toContainText('Installation');
      } else {
        await expect(page.locator('p-table')).toBeVisible();
        await expect(page.locator('tbody tr')).toHaveCount(SERVICES.length);
        await expect(page.locator('tbody')).toContainText('Annual support');
      }

      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('validates and submits add and edit service overlays', async ({ page }) => {
    const collector = collectBrowserFailures(page);
    try {
      await openServices(page);
      await page.getByRole('button', { name: 'Add Service' }).click();
      const addDialog = page.locator('.service-editor-dialog');
      await addDialog.getByRole('button', { name: 'Add Service' }).click();
      await expect(addDialog.locator('.field-error')).toHaveCount(1);
      await expect(addDialog.locator('input[formcontrolname="name"]')).toHaveAttribute(
        'aria-invalid',
        'true',
      );

      await fillServiceForm(addDialog, { name: 'Created audit service', price: '640' });
      await addDialog.getByRole('button', { name: 'Add Service' }).click();
      await expect(addDialog).toBeHidden();
      await expect(
        visibleServices(page).filter({ hasText: 'Created audit service' }),
      ).toBeVisible();

      await visibleServices(page)
        .filter({ hasText: 'Installation' })
        .locator('button')
        .first()
        .click();
      const editDialog = page.locator('.service-editor-dialog');
      await expect(editDialog.locator('input[formcontrolname="name"]')).toHaveValue('Installation');
      await fillServiceForm(editDialog, { name: 'Updated installation', price: '1450' });
      await editDialog.getByRole('button', { name: 'Save Changes' }).click();
      await expect(editDialog).toBeHidden();
      await expect(visibleServices(page).filter({ hasText: 'Updated installation' })).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('explains every blocking add and edit validation error', async ({ page }) => {
    await openServices(page);
    await page.getByRole('button', { name: 'Add Service' }).click();
    const addDialog = page.locator('.service-editor-dialog');
    await submitInvalidServiceForm(addDialog);
    await expect(addDialog.locator('#add-service-name-error')).toHaveText(
      'Service name must be 180 characters or fewer.',
    );
    await expect(addDialog.locator('#add-service-description-error')).toHaveText(
      'Description must be 320 characters or fewer.',
    );
    await expect(addDialog.locator('#add-service-price-error')).toHaveText(
      'This field is required',
    );
    await expect(addDialog.locator('#add-service-tax-error')).toHaveText('This field is required');
    await expect(addDialog.locator('#add-service-hsn-error')).toHaveText(
      'Enter a valid 4 to 8 digit HSN/SAC code.',
    );
    await expect(addDialog.locator('input[formcontrolname="name"]')).toHaveAttribute(
      'aria-describedby',
      'add-service-name-error',
    );
    await expect(addDialog.locator('input#add-service-price')).toHaveAttribute(
      'aria-describedby',
      'add-service-price-error',
    );
    await expect(addDialog.locator('input#add-service-price')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    await expect(addDialog.locator('input#add-service-tax')).toHaveAttribute(
      'aria-describedby',
      'add-service-tax-error',
    );
    await expect(addDialog.locator('input#add-service-tax')).toHaveAttribute(
      'aria-invalid',
      'true',
    );

    await addDialog.locator('input#add-service-price').fill('640');
    await expect(addDialog.locator('input#add-service-price')).not.toHaveAttribute(
      'aria-invalid',
      'true',
    );

    await addDialog.getByRole('button', { name: 'Cancel' }).click();
    await visibleServices(page)
      .filter({ hasText: 'Installation' })
      .locator('button')
      .first()
      .click();
    const editDialog = page.locator('.service-editor-dialog');
    await submitInvalidServiceForm(editDialog);
    await expect(editDialog.locator('#edit-service-name-error')).toHaveText(
      'Service name must be 180 characters or fewer.',
    );
    await expect(editDialog.locator('input#edit-service-tax')).toHaveAttribute(
      'aria-describedby',
      'edit-service-tax-error',
    );
    await expect(editDialog.locator('input#edit-service-tax')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
    await expect(editDialog.locator('input#edit-service-price')).toHaveAttribute(
      'aria-invalid',
      'true',
    );
  });

  test('filters services and reports list and save errors', async ({ page }) => {
    const collector = collectBrowserFailures(page, {
      ignoreConsole: (message) =>
        message.includes('503') && message.includes('Failed to load resource'),
      ignoreResponse: (response) =>
        response.url().includes('/api/services') && response.status() === 503,
    });
    try {
      await openServices(page);
      const search = page.getByPlaceholder('Search services...');
      await search.fill('support');
      await expect(visibleServices(page)).toHaveCount(1);
      await expect(visibleServices(page)).toContainText('Annual support');
      await search.fill('');
      await page.locator('p-select[inputid="services-status-filter"]').click();
      await page.getByRole('option', { name: 'Inactive' }).click();
      await expect(visibleServices(page)).toHaveCount(1);
      await expect(visibleServices(page)).toContainText('Annual support');

      await openServices(page, { failFirstMutation: true });
      await page.getByRole('button', { name: 'Add Service' }).click();
      const dialog = page.locator('.service-editor-dialog');
      await fillServiceForm(dialog, { name: 'Unavailable service', price: '640' });
      await dialog.getByRole('button', { name: 'Add Service' }).click();
      await expect(dialog.locator('.error-banner')).toHaveText('Unable to save service.');

      await openServices(page, { listState: 'error' });
      await expect(page.locator('.error')).toHaveText('Unable to load services.');
      await expect(page.locator('.empty-state')).toHaveCount(0);

      await openServices(page, { services: [] });
      await expect(page.locator('.empty-state:visible')).toBeVisible();
      assertNoUnexpectedBrowserFailures(collector.failures);
    } finally {
      collector.dispose();
    }
  });

  test('keeps the route unavailable to staff users', async ({ page }) => {
    await openServices(page, { role: 'Staff' });
    await expect(page).toHaveURL(/\/(dashboard|sales)$/);
  });

  test('paginates dense long service values within every viewport', async ({ page }) => {
    await openServices(page, { services: DENSE_SERVICES });
    await expect(visibleServices(page)).toHaveCount(20);
    await expect(visibleServices(page).filter({ hasText: 'Audit service 20' })).toBeVisible();
    await assertServiceValueFitsContainer(page, FIRST_PAGE_LONG_MARKER);
    await assertNoHorizontalOverflow(page);

    await page.locator('.p-paginator-next').click();
    await expect(visibleServices(page)).toHaveCount(3);
    await expect(visibleServices(page).filter({ hasText: 'Audit service 21' })).toBeVisible();
    await assertServiceValueFitsContainer(page, SECOND_PAGE_LONG_MARKER);
    await assertNoHorizontalOverflow(page);
  });

  test('localizes services controls, list, and overlays', async ({ page }, testInfo) => {
    await openServices(page, { locale: 'hi-IN' });
    await expect(page.locator('.services-page h1')).toHaveText('सेवाएँ');
    await expect(page.locator('input[placeholder="सेवाएँ खोजें..."]')).toBeVisible();
    await expect(page.locator('.table-caption')).toHaveText('1 - 2 में से 2');
    await expect(visibleServices(page).filter({ hasText: 'Installation' })).toBeVisible();

    await assertLocalizedStatusFilterOptions(page);
    await assertLocalizedServiceList(page, testInfo.project.name);

    await page.getByRole('button', { name: 'सेवा जोड़ें' }).click();
    const addDialog = page.locator('.service-editor-dialog');
    await expect(addDialog.locator('h2')).toHaveText('सेवा जोड़ें');
    await expect(addDialog.locator('label[for="add-service-name"]')).toHaveText(
      HINDI_SERVICE_LABELS.name,
    );
    await addDialog.getByRole('button', { name: HINDI_SERVICE_LABELS.cancel, exact: true }).click();
    await expect(addDialog).toBeHidden();

    await assertLocalizedEditOverlay(page);
  });
});
