import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import en from '../../../../../public/assets/i18n/en-IN.json';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { billingRoutes } from '../billing.routes';
import { BillingPlansPageComponent } from './billing-plans-page.component';
import { BillingPlansService, type BillingPlansPage } from './billing-plans.service';

describe('published billing plans', () => {
  it('has a dedicated read-only destination under billing', () => {
    expect(billingRoutes.find(route => route.path === 'plans')?.loadComponent).toBeDefined();
  });
});

function render(result: BillingPlansPage | Error) {
  TestBed.configureTestingModule({
    imports: [TranslocoTestingModule.forRoot({
      langs: { 'en-IN': en },
      translocoConfig: { availableLangs: ['en-IN'], defaultLang: 'en-IN' },
      preloadLangs: true,
    })],
    providers: [
      provideRouter([]),
      { provide: LocalizationService, useValue: { currentLanguage: signal('en-IN') } },
      { provide: BillingPlansService, useValue: { list: () => result instanceof Error ? throwError(() => result) : of(result) } },
    ],
  });
  const fixture = TestBed.createComponent(BillingPlansPageComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

it('shows gross prices, the owner-inclusive limit and the local fixture label', () => {
  const page = render({ nextCursor: null, items: [{
    planId: 'p', versionId: 'p1', versionNumber: 1, kind: 'multi-shop', name: 'Local offer',
    monthlyPricePaise: 0, annualPricePaise: 1499000, includedShops: 3, membersPerShop: 5,
    extraShopsOffered: true, extraShopMonthlyPricePaise: 1000, extraShopAnnualPricePaise: 10000,
    customerTermsVersion: 'customer-local-1', refundPolicyVersion: 'refund-local-1', eligiblePaidGraceDays: 7,
    billingTimeZone: 'Asia/Kolkata', developmentFixture: true,
    trialPolicy: { versionNumber: 1, durationDays: 14, activeMemberCap: 2, maxConcurrentTrialShops: 2,
      trialTermsVersion: 'trial-local-1' },
  }] });
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
  expect(page.textContent).not.toContain('No plans have been published');
});
