import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach } from 'vitest';

import { BatchSaveResultsComponent } from './batch-save-results.component';
import { AddInventoryBatchResponse } from '../../services/inventory.service';

describe('BatchSaveResultsComponent', () => {
  function makeSucceededRow(clientRowId: string) {
    return {
      clientRowId,
      result: {
        itemId: 'item-1',
        itemName: 'Test Item',
        barcode: 'BC-001',
        batchId: 'batch-1',
        batchNumber: 'BN-001',
        batchQuantity: 5,
        totalQuantity: 15,
        supplierId: null,
        stockTransactionId: 'st-1',
        performedAt: new Date().toISOString(),
      },
    };
  }

  function makeFailedRow(clientRowId: string) {
    return {
      clientRowId,
      itemName: 'Failed Item',
      barcode: 'BC-999',
      errors: [{ code: 'NOT_FOUND', description: 'Item not found' }],
    };
  }

  async function setup(summary: AddInventoryBatchResponse | null) {
    TestBed.configureTestingModule({
      imports: [
        BatchSaveResultsComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });
    const fixture = TestBed.createComponent(BatchSaveResultsComponent);
    fixture.componentInstance.summary = summary;
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders nothing when summary is null', async () => {
    const fixture = await setup(null);
    expect(fixture.debugElement.query(By.css('.save-results-section'))).toBeNull();
  });

  it('renders success section when successCount > 0', async () => {
    const summary: AddInventoryBatchResponse = {
      requestedCount: 1,
      successCount: 1,
      failedCount: 0,
      succeeded: [makeSucceededRow('r1')],
      failed: [],
    };
    const fixture = await setup(summary);
    expect(fixture.debugElement.query(By.css('.save-results-heading--success'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.save-results-heading--error'))).toBeNull();
  });

  it('renders error section when failedCount > 0', async () => {
    const summary: AddInventoryBatchResponse = {
      requestedCount: 1,
      successCount: 0,
      failedCount: 1,
      succeeded: [],
      failed: [makeFailedRow('r1')],
    };
    const fixture = await setup(summary);
    expect(fixture.debugElement.query(By.css('.save-results-heading--error'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.save-results-heading--success'))).toBeNull();
  });

  it('renders both sections when summary has succeeded and failed rows', async () => {
    const summary: AddInventoryBatchResponse = {
      requestedCount: 2,
      successCount: 1,
      failedCount: 1,
      succeeded: [makeSucceededRow('r1')],
      failed: [makeFailedRow('r2')],
    };
    const fixture = await setup(summary);
    expect(fixture.debugElement.query(By.css('.save-results-heading--success'))).not.toBeNull();
    expect(fixture.debugElement.query(By.css('.save-results-heading--error'))).not.toBeNull();
  });
});
