import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { BatchDispositionDialogComponent } from './batch-disposition-dialog.component';
import {
  BatchDispositionDraftService,
  type BatchDispositionChange,
} from '../services/batch-disposition-draft.service';
import { ExpiryPolicyService } from '../services/expiry-policy.service';
import { InventoryService } from '../../inventory/services/inventory.service';
import type { InventoryBatchDto } from '../../inventory/services/inventory.models';
import { shopBusinessDate } from '../services/expiry-policy-evaluator';
import type { ShopScope } from '../../../core/context/shop-scope';
import {
  OPERATING_ACCESS_ERROR_KEYS,
  OperatingAccessHintService,
  operatingAccessErrorKey,
} from '../../billing/status/operating-access-hint.service';

const expiredKey = OPERATING_ACCESS_ERROR_KEYS['Licence.Expired'];
const securityKey = OPERATING_ACCESS_ERROR_KEYS['Licence.SecuritySuspended'];
const busyKey = OPERATING_ACCESS_ERROR_KEYS['Licence.Busy'];
const refusal = (code: string, status: number) =>
  new HttpErrorResponse({ status, error: { code, title: 'INTERNAL_DO_NOT_DISPLAY' } });

const policy = {
  version: 2,
  mode: 'Block' as const,
  expiryRequired: true,
  sellThroughExpiryDate: true,
  warningDays: 7,
  overrideAllowed: false,
};

function releasableBatch(): InventoryBatchDto {
  return {
    id: 'batch',
    itemId: 'item',
    itemName: 'Milk',
    batchNumber: 'CORRECTED',
    quantity: 3,
    quarantinedQuantity: 3,
    availableQuantity: 0,
    version: 8,
    quantityScale: 0,
    expiryDate: '2099-12-31',
    manufacturingDate: null,
    isVoided: false,
    sellability: {
      allowed: false,
      reasonCode: 'InventoryBatch.InsufficientStock',
      requiresApproval: false,
      warning: false,
      policyVersion: 2,
      catalogVersion: 3,
      businessDate: shopBusinessDate(Date.now(), 'Asia/Kolkata'),
      physicalQuantity: 3,
      quarantinedQuantity: 3,
      availableQuantity: 0,
    },
  } as InventoryBatchDto;
}

function mockAccessHint() {
  const scope = signal<ShopScope | null>({ shopId: 'shop', userId: 'owner', epoch: 1 });
  const blockingMessage = signal<string | null>(null);
  const refresh = vi.fn(async () => true);
  const reportRefusal = (error: unknown, owner: ShopScope | null) => {
    const key = operatingAccessErrorKey(error);
    if (owner === scope() && key && key !== busyKey) blockingMessage.set(key);
  };
  return { scope, blockingMessage, refresh, reportRefusal };
}

function setup(reasonCode = 'Expiry.RequiredDateMissing') {
  const batch = releasableBatch();
  const root = {
    id: 'root',
    rootChangeId: null,
    action: 'Quarantine',
    quantity: 3,
    reasonCode,
    expiryDate: null,
  } as BatchDispositionChange;
  const execute = vi.fn(() => of({}));
  const history = vi.fn(() => of([root]));
  const pending = vi.fn(async (): Promise<unknown[]> => []);
  const access = mockAccessHint();
  TestBed.configureTestingModule({
    imports: [
      BatchDispositionDialogComponent,
      TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
    ],
    providers: [
      {
        provide: BatchDispositionDraftService,
        useValue: { changes$: EMPTY, history, pending, execute },
      },
      {
        provide: OperatingAccessHintService,
        useValue: access,
      },
      {
        provide: ExpiryPolicyService,
        useValue: {
          role: () => 'owner',
          read: () => of({ effectivePolicy: policy, catalogVersion: 3 }),
          businessTimeZone: () => of('Asia/Kolkata'),
        },
      },
      { provide: InventoryService, useValue: { getInventoryBatches: () => of([batch]) } },
    ],
  });
  const fixture = TestBed.createComponent(BatchDispositionDialogComponent);
  fixture.componentRef.setInput('batch', batch);
  fixture.detectChanges();
  return {
    fixture,
    component: fixture.componentInstance,
    execute,
    history,
    pending,
    batch,
    scope: access.scope,
    blockingMessage: access.blockingMessage,
  };
}
describe('quarantined lot Release flow', () => {
  it('releases a fully quarantined missing-date root after a separate date correction', async () => {
    const { fixture, component, execute } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    expect(component.canRelease()).toBe(true);
    component.action = 'Release';
    component.reason = 'Audited corrected date';
    await component.submit();
    expect(execute).toHaveBeenCalledWith('batch', {
      expectedVersion: 8,
      quarantineChangeId: 'root',
      action: 'Release',
      quantity: 1,
      reason: 'Audited corrected date',
    });
  });
  it('refuses current policy blocks, inactive status and stale authority despite zero available stock', async () => {
    const { component, batch, execute } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    component.current = { ...batch, expiryDate: '2020-01-01' };
    expect(component.canRelease()).toBe(false);
    component.current = {
      ...batch,
      sellability: { ...batch.sellability!, reasonCode: 'Item.Inactive' },
    };
    expect(component.canRelease()).toBe(false);
    component.current = { ...batch, sellability: { ...batch.sellability!, catalogVersion: 2 } };
    expect(component.canRelease()).toBe(false);
    component.current = {
      ...batch,
      sellability: { ...batch.sellability!, businessDate: '2020-01-01' },
    };
    expect(component.canRelease()).toBe(false);
    expect(execute).not.toHaveBeenCalled();
  });
  it('refuses an originally expired root even after its date is extended', async () => {
    const { component, execute } = setup('Expiry.Expired');
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    component.action = 'Release';
    component.reason = 'Extended';
    expect(component.canRelease()).toBe(false);
    await component.submit();
    expect(execute).not.toHaveBeenCalled();
  });
  it('blocks a previously selected release while an owner can dispose the same held units', async () => {
    const { component, execute, blockingMessage } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    component.action = 'Release';
    component.reason = 'Audited date';
    expect(component.canRelease()).toBe(true);
    blockingMessage.set(expiredKey);
    await component.submit();
    expect(execute).not.toHaveBeenCalled();
    expect(component.canRelease()).toBe(false);
    component.action = 'Dispose';
    await component.submit();
    expect(execute).toHaveBeenCalledWith(
      'batch',
      expect.objectContaining({
        action: 'Dispose',
        quarantineChangeId: 'root',
        quantity: 1,
      }),
    );
  });

  it('keeps exact-request recovery available beside an authoritative release denial', async () => {
    const { component, execute, pending } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    component.action = 'Release';
    component.reason = 'Audited date';
    const payload = {
      action: 'Release' as const,
      reason: 'Audited date',
      quantity: 1,
      quarantineChangeId: 'root',
      expectedVersion: 8,
    };
    execute.mockImplementationOnce(() => {
      pending.mockResolvedValue([{ targetId: 'frozen', pending: { payload } }]);
      return throwError(() => refusal('Licence.Expired', 403));
    });
    await component.submit();
    expect(component.message()).toBe(expiredKey);
    expect(component.pending()).toHaveLength(1);
    await component.submit(payload);
    expect(execute).toHaveBeenLastCalledWith('batch', payload);
  });

  it('shows a retryable busy reason and hides shop details on a security refusal', async () => {
    const { fixture, component, execute, history } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    component.reason = 'Safety disposal';
    execute.mockReturnValueOnce(throwError(() => refusal('Licence.Busy', 409)));
    await component.submit();
    expect(component.message()).toBe(busyKey);
    history.mockReturnValueOnce(throwError(() => refusal('Licence.SecuritySuspended', 403)));
    await component.load();
    fixture.detectChanges();
    expect(component.message()).toBe(securityKey);
    expect(component.roots()).toHaveLength(0);
    expect(fixture.nativeElement.textContent).not.toContain('Milk');
    expect(fixture.nativeElement.textContent).not.toContain('INTERNAL_DO_NOT_DISPLAY');
  });

  it('ignores a late refusal from the previous shop epoch', async () => {
    const { component, execute, scope, blockingMessage } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    component.reason = 'Safety disposal';
    execute.mockImplementationOnce(() => {
      scope.set({ ...scope()!, epoch: 2 });
      return throwError(() => refusal('Licence.Expired', 403));
    });
    await component.submit();
    expect(blockingMessage()).toBeNull();
    expect(component.message()).toBe('');
    expect(component.roots()).toHaveLength(0);
  });
});
