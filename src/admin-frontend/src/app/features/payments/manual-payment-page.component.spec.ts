import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { ManualPaymentApi, type ManualPaymentCommand, type ManualPaymentView } from './manual-payment-api.service';
import { ManualPaymentDrafts } from './manual-payment-drafts';
import { ManualPaymentPageComponent } from './manual-payment-page.component';

const account = '11111111-1111-4111-8111-111111111111';
const order = '22222222-2222-4222-8222-222222222222';
const scope = { admin: 'admin', account, order };
const command: ManualPaymentCommand = {
  operationKey: 'original', expectedRevision: 1, snapshotHash: 'a'.repeat(64), receivingAccountId: 'fixture',
  configurationVersion: 'fixture/1', reference: 'BANK-FIXTURE', evidence: 'STATEMENT-FIXTURE',
  receivedAt: '2027-04-01T00:00:00Z', amountPaise: 27000, currency: 'INR', reason: 'Verified', verified: true,
};

afterEach(() => TestBed.resetTestingModule());

describe('manual payment restoration', () => {
  it('retries the original frozen request and retains it until the current order is fulfilled', async () => {
    let held = true;
    const read = vi.fn(async (): Promise<ManualPaymentView> => ({
      order: { revision: 1, snapshotHash: command.snapshotHash, planName: 'Multi-shop', period: 'Month',
        shopCount: 1, amountPaise: command.amountPaise, currency: 'INR', shops: [], nextShop: null },
      receivingAccount: null,
      status: { payment: 'Verified', attemptId: null, fulfilment: held ? 'NeedsReview' : 'Fulfilled',
        access: held ? 'NotApplicable' : 'Applying', restorationHeld: held },
      onlineFundsNeedReview: false, receipts: [],
    }));
    const confirm = vi.fn(async () => ({
      operationKey: command.operationKey, adminId: scope.admin, reviewReason: 'RestorationPending',
      source: 'Manual', payment: 'Verified', fulfilment: 'NeedsReview',
    }));
    const settle = vi.fn(async () => {});
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({ account, order })) } },
      { provide: AdminSessionService, useValue: {
        adminId: signal(scope.admin), sessionId: signal('session'), capabilities: signal(['billing']),
      } },
      { provide: ManualPaymentApi, useValue: { read, confirm } },
      { provide: ManualPaymentDrafts, useValue: { read: async () => ({ revision: 0, pending: command }), settle } },
    ] });
    const fixture = TestBed.createComponent(ManualPaymentPageComponent);
    const page = fixture.nativeElement as HTMLElement;
    const update = () => fixture.detectChanges();
    const retry = () => Array.from(page.querySelectorAll('button')).find(button =>
      button.textContent?.includes('Check restoration again'));
    await vi.waitFor(() => {
      update();
      expect(page.querySelector('[data-testid="manual-restoration-hold"]'), page.textContent ?? '').not.toBeNull();
    });

    retry()!.click();
    await vi.waitFor(() => {
      update();
      expect(retry()?.disabled).toBe(false);
    });
    expect(confirm).toHaveBeenCalledWith(scope, 'session', command);
    expect(settle).not.toHaveBeenCalled();

    held = false;
    retry()!.click();
    await vi.waitFor(() => {
      update();
      expect(page.querySelector('[data-testid="order-access"]')?.textContent).toContain('Applying');
    });
    expect(confirm).toHaveBeenLastCalledWith(scope, 'session', command);
    expect(settle).toHaveBeenCalledOnce();
    expect(settle).toHaveBeenCalledWith(scope, command.operationKey);
    expect(retry()).toBeUndefined();
    expect(page.querySelector('[data-testid="order-access"]')?.textContent).toContain('Applying');
  });
});

describe('original payment routing', () => {
  it('distinguishes original identity A from current selection B and shows the recovery blocker', async () => {
    TestBed.configureTestingModule({ providers: [
      provideRouter([]),
      { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({ account, order })) } },
      { provide: AdminSessionService, useValue: {
        adminId: signal(scope.admin), sessionId: signal('session'), capabilities: signal(['billing']),
      } },
      { provide: ManualPaymentApi, useValue: {
        read: async () => ({ order: { planName: 'Plan', period: 'Month', shopCount: 1, amountPaise: 12000, shops: [] },
          status: { payment: 'PendingVerification', attemptId: 'original-attempt', access: 'NotApplicable' }, receipts: [] }),
        routing: async () => ({ schemaVersion: 1, accountId: account, orderId: order, attemptId: 'original-attempt',
          original: { providerId: 'development-simulator', providerAccountId: 'simulator-a', environment: 'Development', configurationVersion: 'a/1' },
          currentSelection: { revision: 2, profileId: 'b', pin: {
            providerId: 'development-simulator', providerAccountId: 'simulator-b', environment: 'Development', configurationVersion: 'b/1' } },
          recoveryAvailable: false, blocker: 'MissingProfile' }),
      } },
      { provide: ManualPaymentDrafts, useValue: { read: async () => ({ revision: 0 }) } },
    ] });
    const fixture = TestBed.createComponent(ManualPaymentPageComponent);
    await vi.waitFor(() => {
      fixture.detectChanges();
      const routing = fixture.nativeElement.querySelector('[data-testid="payment-routing"]') as HTMLElement | null;
      expect(routing?.textContent).toContain('simulator-a');
      expect(routing?.textContent).toContain('simulator-b');
      expect(routing?.textContent).toContain('a/1');
      expect(routing?.textContent).toContain('MissingProfile');
    });
  });
});
