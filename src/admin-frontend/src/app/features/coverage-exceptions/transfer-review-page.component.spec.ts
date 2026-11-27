import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AdminSessionService } from '../auth/admin-session.service';
import { TransferDrafts, TransferReviewApi, type TransferCommand } from './transfer-review-api';
import { TransferReviewPageComponent } from './transfer-review-page.component';

const source = '11111111-1111-4111-8111-111111111111';
const transfer = '22222222-2222-4222-8222-222222222222';

function mount(capabilities = ['licensing-manager']) {
  let frozen: TransferCommand | undefined;
  const scope = { admin: 'reviewer', account: source, order: transfer };
  const drafts = {
    read: vi.fn(async () => ({ revision: 0, pending: frozen })),
    claim: vi.fn(async (_scope, revision, cmd) => ({ revision, pending: frozen ??= cmd })),
    settle: vi.fn(async () => { frozen = undefined; }),
  };
  const review = {
    transferId: transfer, sourceAccountId: source, shopName: 'North', sourceAccountName: 'Original',
    destinationAccountName: 'New payer', sourceTerms: [], destinationTerms: [], consents: [],
    treatment: 'ReviewedAllocation', cutoverAt: '2028-02-16T10:00:00Z', sourcePaidThroughAt: '2028-02-29T04:30:00Z',
    reviewDigest: 'a'.repeat(64), memberAllowance: 5, observedPermitCount: 1,
  };
  const api = {
    list: vi.fn(async () => []), preview: vi.fn(async () => review),
    decide: vi.fn(async (_account, _id, _session, cmd, lookup) => {
      if (!lookup) throw new Error('Lost approval response');
      return { transferId: transfer, operationKey: cmd.operationKey, state: 'Applied' };
    }),
  };
  const paramMap = convertToParamMap({ accountId: source }), queryParamMap = convertToParamMap({ transfer });
  TestBed.configureTestingModule({ imports: [TransferReviewPageComponent], providers: [
    provideRouter([]), { provide: AdminSessionService, useValue: {
      adminId: signal('reviewer'), sessionId: signal('session'), capabilities: signal(capabilities),
    } }, { provide: TransferDrafts, useValue: drafts }, { provide: TransferReviewApi, useValue: api },
    { provide: ActivatedRoute, useValue: { paramMap: of(paramMap), queryParamMap: of(queryParamMap),
        snapshot: { paramMap, queryParamMap } } },
  ] });
  return { scope, drafts, api, create: () => TestBed.createComponent(TransferReviewPageComponent), pending: () => frozen };
}

async function render(fixture: ComponentFixture<TransferReviewPageComponent>) {
  fixture.detectChanges();
  await fixture.whenStable();
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('coverage transfer review', () => {
  it('requires Licensing Manager access before reading account data', async () => {
    const ui = mount(['support']);
    const el = await render(ui.create());
    expect(el.textContent).toContain('Licensing Manager access is required');
    expect(ui.api.list).not.toHaveBeenCalled();
  });

  it.each(['lost response', 'changed terms'])('recovers the saved decision after %s', async failure => {
    const ui = mount();
    if (failure === 'changed terms') ui.api.decide.mockRejectedValueOnce(new HttpErrorResponse({
      status: 409, error: { code: 'Coverage.TransferChanged' },
    }));
    const fixture = ui.create(), el = await render(fixture);
    await vi.waitFor(() => { fixture.detectChanges(); expect(el.textContent).toContain('Carry one funded slot for North'); });
    const reason = el.querySelector('textarea')!, checked = el.querySelector<HTMLInputElement>('input')!;
    reason.value = 'Owners confirmed the exact interval'; reason.dispatchEvent(new Event('input'));
    checked.checked = true; checked.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    el.querySelector('button')!.click();
    await vi.waitFor(() => { fixture.detectChanges(); expect(el.textContent).toContain(failure === 'lost response' ? 'Outcome unconfirmed' : 'Check the saved decision'); });
    const frozen = ui.pending()!;
    expect(frozen.acknowledgedHistoricalBillsStay).toBe(true);
    expect(ui.drafts.claim).toHaveBeenCalledWith(ui.scope, 0, frozen);
    fixture.destroy();
    if (failure === 'changed terms') {
      ui.api.list.mockRejectedValueOnce(new Error('List unavailable'));
      ui.api.preview.mockRejectedValueOnce(new Error('Preview unavailable'));
    }
    const reopened = ui.create(), page = await render(reopened);
    await vi.waitFor(() => { reopened.detectChanges(); expect(page.textContent).toContain(frozen.operationKey); expect(page.querySelector('button:disabled')).toBeNull(); });
    page.querySelector('button')!.click();
    await vi.waitFor(() => { reopened.detectChanges(); expect(page.textContent).toContain('Applied'); });
    expect(ui.api.decide).toHaveBeenLastCalledWith(source, transfer, 'session', frozen, true);
    expect(page.textContent).toContain('Applied');
    expect(ui.drafts.settle).toHaveBeenCalledWith(ui.scope, frozen.operationKey);
  });
});
