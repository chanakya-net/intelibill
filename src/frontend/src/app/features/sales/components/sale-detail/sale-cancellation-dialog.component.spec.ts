import { disabledTillProviders } from '../../../../../tests/tills/disabled-till.providers';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { IDBFactory } from 'fake-indexeddb';
import { of, Subject, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ShopContextService } from '../../../../core/context/shop-context.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { SaleCancellationService } from '../../services/sale-cancellation.service';
import { SaleCancellationDraftService } from '../../services/sale-cancellation-draft.service';
import type {
  SaleCancellationPreviewDto,
  SaleCancellationDto,
} from '../../services/sale-cancellation.models';
import { SaleCancellationDialogComponent } from './sale-cancellation-dialog.component';
const owner = { userId: 'u', shopId: 's', epoch: 0 };
const preview: SaleCancellationPreviewDto = {
  saleId: 'sale',
  invoiceNumber: 'A/2627/000001',
  document: null,
  status: 'Posted',
  expectedVersion: 7,
  eligible: true,
  postingEnabled: true,
  businessDate: '2026-11-08',
  reasons: [],
  lines: [
    {
      saleItemId: 'line',
      itemId: 'item',
      inventoryBatchId: 'batch',
      quantity: 2,
      description: 'Frozen soap',
      uqc: 'NOS',
    },
  ],
  totalAmount: 200,
  dueReductionAmount: 120,
  cashRefundAmount: 80,
  externalRefundAmount: 0,
  creditRestorationAmount: 0,
  requiresGoodsConfirmation: true,
  requiresCashRefundConfirmation: true,
};
describe('SaleCancellationDialogComponent', () => {
  let api: {
    preview: ReturnType<typeof vi.fn>;
    cancel: ReturnType<typeof vi.fn>;
    getOperation: ReturnType<typeof vi.fn>;
  };
  let scope: ReturnType<typeof signal<typeof owner | null>>;
  let role: ReturnType<typeof signal<string>>;
  beforeEach(() => {
    vi.stubGlobal('indexedDB', new IDBFactory());
    scope = signal<typeof owner | null>(owner);
    role = signal('Owner');
    api = {
      preview: vi.fn(() => of(preview)),
      cancel: vi.fn(() => of({ saleId: 'sale', reference: 'SC-1' } as SaleCancellationDto)),
      getOperation: vi.fn(() => throwError(() => new HttpErrorResponse({ status: 404 }))),
    };
    TestBed.configureTestingModule({
      imports: [
        SaleCancellationDialogComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': {} },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
        }),
      ],
      providers: [
        disabledTillProviders()[0],
        { provide: SaleCancellationService, useValue: api },
        {
          provide: ShopContextService,
          useValue: {
            scope,
            epoch: () => scope()?.epoch ?? 1,
            isCurrent: (o: typeof owner) =>
              scope()?.userId === o.userId && scope()?.shopId === o.shopId,
            beginMutation: () => ({ release: () => {} }),
          },
        },
        {
          provide: AuthService,
          useValue: {
            session: () => ({ activeShopId: 's', shops: [{ shopId: 's', role: role() }] }),
          },
        },
      ],
    });
  });
  afterEach(() => vi.unstubAllGlobals());
  async function setup() {
    const f = TestBed.createComponent(SaleCancellationDialogComponent);
    f.componentRef.setInput('saleId', 'sale');
    f.componentRef.setInput('visible', true);
    f.detectChanges();
    await vi.waitFor(() =>
      expect(f.componentInstance.ready() || !!f.componentInstance.error()).toBe(true),
    );
    f.detectChanges();
    return f;
  }
  it('requires independent goods and cash confirmations for the exact mixed settlement', async () => {
    const f = await setup();
    const c = f.componentInstance;
    c.reason = 'Duplicate sale';
    expect(c.valid()).toBe(false);
    c.goodsConfirmed = true;
    expect(c.valid()).toBe(false);
    c.cashConfirmed = true;
    expect(c.valid()).toBe(true);
    await c.submit();
    const r = await TestBed.inject(SaleCancellationDraftService).load(owner, 'sale');
    expect(r).toBeNull();
    expect(api.cancel.mock.calls[0][1]).toMatchObject({
      reason: 'Duplicate sale',
      expectedVersion: 7,
      goodsRecoveredConfirmed: true,
      cashRefundConfirmed: true,
    });
  });
  it('storage failure blocks the HTTP write', async () => {
    const f = await setup();
    const c = f.componentInstance;
    c.reason = 'Duplicate sale';
    c.goodsConfirmed = c.cashConfirmed = true;
    vi.stubGlobal('indexedDB', undefined);
    await c.submit();
    expect(api.cancel).not.toHaveBeenCalled();
    expect(c.error()).toBe('sales.cancellation.storageError');
  });
  it('loads pending first and keeps its payload after lookup404 then refused retry', async () => {
    const drafts = TestBed.inject(SaleCancellationDraftService);
    const frozen = await drafts.prepare(
      owner,
      'sale',
      {
        payload: {
          expectedVersion: 7,
          reason: 'Original reason',
          goodsRecoveredConfirmed: true,
          cashRefundConfirmed: true,
        },
      },
      0,
    );
    api.cancel.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 409 })));
    const f = await setup();
    expect(api.preview).not.toHaveBeenCalled();
    expect(f.componentInstance.reason).toBe('Original reason');
    await f.componentInstance.checkStatus();
    await f.componentInstance.retry();
    const r = await drafts.load(owner, 'sale');
    expect(r?.pending?.payload).toEqual(frozen.pending!.payload);
    expect(r?.pending?.status).toBe('unknown');
  });
  it('does not render success after scope changes during HTTP', async () => {
    const response = new Subject<SaleCancellationDto>();
    api.cancel.mockReturnValue(response);
    const f = await setup();
    const c = f.componentInstance;
    const emitted = vi.fn();
    c.completed.subscribe(emitted);
    c.reason = 'Duplicate sale';
    c.goodsConfirmed = c.cashConfirmed = true;
    const sending = c.submit();
    await new Promise((r) => setTimeout(r, 10));
    scope.set(null);
    response.next({ saleId: 'sale' } as SaleCancellationDto);
    response.complete();
    await sending;
    expect(emitted).not.toHaveBeenCalled();
  });
  it('staff and disabled rollout cannot submit, while all refusal reasons remain visible', async () => {
    api.preview.mockReturnValue(
      of({
        ...preview,
        postingEnabled: false,
        eligible: false,
        reasons: [
          {
            code: 'ExternalRefundWorkflowRequired',
            message: 'External refund is not recorded by this cancellation workflow.',
          },
          { code: 'CreditRestorationRequired', message: 'No credit balance is restored.' },
        ],
      }),
    );
    const f = await setup();
    const c = f.componentInstance;
    c.reason = 'Duplicate sale';
    c.goodsConfirmed = c.cashConfirmed = true;
    expect(c.valid()).toBe(false);
    expect(f.nativeElement.textContent).toContain('ExternalRefundWorkflowRequired');
    expect(f.nativeElement.textContent).toContain('CreditRestorationRequired');
    role.set('Staff');
    expect(c.canManage()).toBe(false);
  });
  it('never sends a frozen intent under a foreign scope', async () => {
    const f = await setup();
    const c = f.componentInstance;
    c.reason = 'Duplicate sale';
    c.goodsConfirmed = c.cashConfirmed = true;
    scope.set({ ...owner, shopId: 'other', epoch: 1 });
    await c.submit();
    expect(api.cancel).not.toHaveBeenCalled();
  });
});
