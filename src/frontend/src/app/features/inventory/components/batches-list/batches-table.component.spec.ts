import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it } from 'vitest';

import { BatchesTableComponent } from './batches-table.component';
import type { InventoryBatchDto } from '../../services/inventory.models';

describe('BatchesTableComponent', () => {
  let fixture: ComponentFixture<BatchesTableComponent>;
  let component: BatchesTableComponent;

  const mockBatches: InventoryBatchDto[] = [
    {
      id: 'b1',
      shopId: 's1',
      itemId: 'i1',
      itemName: 'Milk',
      barcode: '111',
      batchNumber: 'B1',
      quantity: 12,
      originalQuantity: 12,
      costPrice: 60,
      mrp: 70,
      salesPrice: 75,
      taxRatePercent: 5,
      taxIncluded: false,
      expiryDate: null,
      manufacturingDate: null,
      supplierId: null,
      supplierName: null,
      isVoided: false,
      createdAt: new Date().toISOString(),
      updatedAt: null,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BatchesTableComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    }).compileComponents();

    fixture = TestBed.createComponent(BatchesTableComponent);
    component = fixture.componentInstance;
    component.batches = mockBatches;
  });

  it('should emit batch action payload', () => {
    const events: Array<{ action: 'edit' | 'adjust' | 'void' | 'printLabels'; batchId: string }> = [];
    component.batchAction.subscribe((value) => events.push(value));

    component.onRowAction('edit', 'b1');
    component.onRowAction('printLabels', 'b1');

    expect(events).toEqual([
      { action: 'edit', batchId: 'b1' },
      { action: 'printLabels', batchId: 'b1' },
    ]);
  });

  it('should emit selection updates for selectable rows', () => {
    const events: string[][] = [];
    component.selectionChange.subscribe((value) => events.push([...value]));

    component.onRowSelectionChange(mockBatches[0], true);

    expect(events).toEqual([['b1']]);
  });

  it('should emit batch clicked when non-voided row selected', () => {
    const events: string[] = [];
    component.batchClicked.subscribe((value) => events.push(value));

    component.onRowSelect(mockBatches[0]);

    expect(events).toEqual(['b1']);
  });

  it('should ignore selection for voided batch rows', () => {
    const events: string[] = [];
    component.batchClicked.subscribe((value) => events.push(value));

    component.onRowSelect({ ...mockBatches[0], id: 'b2', isVoided: true });

    expect(events).toHaveLength(0);
  });

  it('should build initials from product name', () => {
    expect(component.productInitials('Almond Milk')).toBe('AM');
  });

  it('should keep existing avatar color format', () => {
    const color = component.productAvatarColor('Milk');

    expect(color).toMatch(/^#[0-9a-f]{6}$/);
  });
});
