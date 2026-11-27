import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { billingRoutes } from '../billing.routes';
import { BillingPlansPageComponent } from './billing-plans-page.component';
import { BillingPlansService, type BillingPlansPage, type PublishedPlan } from './billing-plans.service';

describe('published billing plans', () => {
  it('has a dedicated read-only destination under billing', () => {
    expect(billingRoutes.find(route => route.path === 'plans')?.loadComponent).toBeDefined();
  });
});

function render(result: BillingPlansPage | Error, query: Record<string, string> = {},
  version = vi.fn()) {
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en },
      translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
      preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(query)) } },
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: BillingPlansService, useValue: { list: () => result instanceof Error ? throwError(() => result) : of(result), version } },
    ],
  });
  const fixture = TestBed.createComponent(BillingPlansPageComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const id = '2451f027-a7ea-43f6-8fd2-c9850c117c3e';
const offer: PublishedPlan = {
    planId: 'p', versionId: id, versionNumber: 1, kind: 'multi-shop', name: 'Local offer',
    monthlyPricePaise: 0, annualPricePaise: 1499000, includedShops: 3, membersPerShop: 5,
    extraShopsOffered: true, extraShopMonthlyPricePaise: 1000, extraShopAnnualPricePaise: 10000,
    customerTermsVersion: 'customer-local-1', refundPolicyVersion: 'refund-local-1', eligiblePaidGraceDays: 7,
    billingTimeZone: 'Asia/Kolkata', developmentFixture: true,
    trialPolicy: { versionNumber: 1, durationDays: 14, activeMemberCap: 2, maxConcurrentTrialShops: 2,
      trialTermsVersion: 'trial-local-1' },
};
const empty = { nextCursor: null, items: [] };

it('shows gross prices, the owner-inclusive limit and the local fixture label', () => {
  const page = render({ nextCursor: null, items: [offer] });
  expect(page.textContent).toContain('Development fixture');
  expect(page.textContent).toContain('₹0.00');
  expect(page.textContent).toContain('₹14,990.00');
  expect(page.textContent).toContain('including the owner');
  expect(page.textContent).toContain('proof of payment only');
  expect(page.querySelector('[data-testid="published-plan"] button')).toBeNull();
});

it('reports unavailability separately from an empty published catalogue', () => {
  const page = render(new Error('unavailable'));
  expect(page.querySelector('[role="alert"]')?.textContent).toContain('unavailable');
  expect(page.textContent).not.toContain('No plans are available for new purchase');
});


it('shows a retired version’s exact terms as unavailable for new purchase', () => {
  const version = vi.fn(() => of({ terms: offer, availableForNewPurchase: false }));
  const page = render(empty, { version: id }, version);
  expect(version).toHaveBeenCalledWith(id);
  const terms = page.querySelector('[data-testid="retained-plan"]');
  expect(terms?.textContent).toContain('No longer available for new purchase');
  for (const fact of ['Development fixture', '₹14,990.00', 'customer-local-1', 'refund-local-1', 'trial-local-1']) {
    expect(terms?.textContent).toContain(fact);
  }
});

it('reports whether the requested version is still offered', () => {
  const version = vi.fn(() => of({ terms: offer, availableForNewPurchase: true }));
  const page = render(empty, { version: id }, version);
  expect(page.querySelector('[data-testid="retained-plan"]')?.textContent).toContain('Currently available for new purchase');
});

it.each([[404, 'Plan version not found'], [503, 'Billing is temporarily unavailable']])(
  'identifies version-read status %s', (status, text) => {
    const version = vi.fn(() => throwError(() => new HttpErrorResponse({ status })));
    const page = render(empty, { version: id }, version);
    expect(page.querySelector('[role="alert"]')?.textContent).toContain(text);
  });

it('avoids a version read for a malformed ID', () => {
  const version = vi.fn();
  const page = render(empty, { version: 'malformed' }, version);
  expect(version).not.toHaveBeenCalled();
  expect(page.querySelector('[role="alert"]')?.textContent).toContain('Plan version not found');
});
