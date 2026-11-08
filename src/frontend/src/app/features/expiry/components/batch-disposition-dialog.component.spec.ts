import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { EMPTY, of } from 'rxjs';
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
function setup(reasonCode = 'Expiry.RequiredDateMissing') {
  const policy = {
    version: 2,
    mode: 'Block' as const,
    expiryRequired: true,
    sellThroughExpiryDate: true,
    warningDays: 7,
    overrideAllowed: false,
  };
  const batch = {
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
  const root = {
    id: 'root',
    rootChangeId: null,
    action: 'Quarantine',
    quantity: 3,
    reasonCode,
    expiryDate: null,
  } as BatchDispositionChange;
  const execute = vi.fn(() => of({}));
  TestBed.configureTestingModule({
    imports: [
      BatchDispositionDialogComponent,
      TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
    ],
    providers: [
      {
        provide: BatchDispositionDraftService,
        useValue: { changes$: EMPTY, history: () => of([root]), pending: async () => [], execute },
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
  return { fixture, component: fixture.componentInstance, execute, batch };
}
describe('quarantined lot Release flow', () => {
  it('releases a fully quarantined missing-date root after a separate date correction', async () => {
    const { fixture, component, execute } = setup();
    await vi.waitFor(() => expect(component.busy()).toBe(false));
    component.selectRoot('root');
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    expect(component.canRelease()).toBe(true);
    expect(fixture.nativeElement.querySelector('option[value="Release"]').disabled).toBe(false);
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
});
