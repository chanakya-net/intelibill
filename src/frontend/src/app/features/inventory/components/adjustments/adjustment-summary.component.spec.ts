import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it } from 'vitest';

import { InventoryAdjustmentHistoryItem } from '../../services/inventory.models';
import { AdjustmentSummaryComponent } from './adjustment-summary.component';

describe('AdjustmentSummaryComponent', () => {
  const baseAdjustment: InventoryAdjustmentHistoryItem = {
    adjustmentId: 'adj-1',
    adjustmentNumber: 'ADJ-0001',
    itemId: 'item-1',
    itemName: 'Rice',
    barcode: '111',
    batchId: 'batch-1',
    batchNumber: 'BATCH-001',
    direction: 'Decrease',
    reason: 'Damaged',
    quantity: 2,
    unitCost: 100,
    costImpact: -200,
    batchQuantityBefore: 10,
    batchQuantityAfter: 8,
    inventoryQuantityBefore: 20,
    inventoryQuantityAfter: 18,
    performedAt: '2026-05-05T08:30:00.000Z',
    performedByUserId: 'user-1',
    performedByDisplayName: 'Test User',
    notes: null,
    isVoided: false,
    voidedAt: null,
    voidedByUserId: null,
    voidedByDisplayName: null,
    voidReason: null,
    reversalStockTransactionId: null,
  };

  function setup(rows: InventoryAdjustmentHistoryItem[], canVoid = false) {
    TestBed.configureTestingModule({
      imports: [
        AdjustmentSummaryComponent,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(AdjustmentSummaryComponent);
    fixture.componentInstance.rows = rows;
    fixture.componentInstance.canVoidAdjustments = canVoid;
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders adjustment data in the desktop table', () => {
    const fixture = setup([baseAdjustment]);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.desktop-table')?.textContent).toContain('ADJ-0001');
    expect(host.querySelector('.desktop-table')?.textContent).toContain('Rice');
  });

  it('renders adjustment data in mobile cards', () => {
    const fixture = setup([baseAdjustment]);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.mobile-grid-container')?.textContent).toContain('Rice');
    expect(host.querySelector('.mobile-grid-container')?.textContent).toContain('ADJ-0001');
  });

  it('shows void button when canVoidAdjustments is true and adjustment is active', () => {
    const fixture = setup([baseAdjustment], true);

    const voidButtons = fixture.debugElement.queryAll(By.css('[data-testid="void-adjustment-action"]'));
    expect(voidButtons.length).toBeGreaterThan(0);
  });

  it('hides void button when canVoidAdjustments is false', () => {
    const fixture = setup([baseAdjustment], false);

    const voidButtons = fixture.debugElement.queryAll(By.css('[data-testid="void-adjustment-action"]'));
    expect(voidButtons).toHaveLength(0);
  });

  it('hides void button for voided adjustments', () => {
    const voided = { ...baseAdjustment, isVoided: true, voidedAt: '2026-05-05T09:00:00.000Z', voidReason: 'Test' };
    const fixture = setup([voided], true);

    const voidButtons = fixture.debugElement.queryAll(By.css('[data-testid="void-adjustment-action"]'));
    expect(voidButtons).toHaveLength(0);
  });

  it('emits voidRequested when void button is clicked', () => {
    const fixture = setup([baseAdjustment], true);
    const component = fixture.componentInstance;
    const emitted: InventoryAdjustmentHistoryItem[] = [];
    component.voidRequested.subscribe((adj) => emitted.push(adj));

    const voidBtn = fixture.debugElement.query(By.css('[data-testid="void-adjustment-action"]'));
    voidBtn.triggerEventHandler('click');

    expect(emitted).toHaveLength(1);
    expect(emitted[0].adjustmentId).toBe('adj-1');
  });

  it('shows empty state message when rows is empty', () => {
    const fixture = setup([]);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('.empty-state')).not.toBeNull();
  });

  it('displays void metadata for voided adjustments', () => {
    const voided = {
      ...baseAdjustment,
      isVoided: true,
      voidedAt: '2026-05-05T09:00:00.000Z',
      voidedByDisplayName: 'Owner User',
      voidReason: 'Duplicate',
      reversalStockTransactionId: 'tx-reversal-1',
    };
    const fixture = setup([voided]);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.textContent).toContain('Owner User');
    expect(host.textContent).toContain('Duplicate');
    expect(host.textContent).toContain('tx-reversal-1');
  });
});
