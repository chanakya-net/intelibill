import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';
import type { Menu } from 'primeng/menu';

import { BatchesTableComponent, type BatchTableAction } from './batches-table.component';
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
      version: 1,
      receiptValueId: 'r1',
      receiptStatus: 'Resolved',
      receiptOriginalTotalCost: 720,
      receiptTotalCost: 720,
      receiptVersion: 3,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        BatchesTableComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BatchesTableComponent);
    component = fixture.componentInstance;
    component.batches = mockBatches;
  });

  it('should emit batch action payload', () => {
    const events: BatchTableAction[] = [];
    component.batchAction.subscribe((value) => events.push(value));

    component.onRowAction('edit', 'b1');
    component.onRowAction('printLabels', 'b1');

    expect(events).toEqual([
      { action: 'edit', batchId: 'b1' },
      { action: 'printLabels', batchId: 'b1' },
    ]);
  });

  it('keeps every row action available through one menu', () => {
    const menuItems = component.rowActionItems(mockBatches[0]);

    expect(menuItems.map((item) => item.id)).toEqual([
      'printLabels',
      'adjust',
      'count',
      'edit',
      'void',
    ]);
    expect(menuItems.every((item) => item.disabled === false)).toBe(true);
  });

  it('opens one stable menu model and refreshes its batch target on the next opening', () => {
    const events: BatchTableAction[] = [];
    component.batchAction.subscribe((value) => events.push(value));
    const toggle = vi.fn();
    const menu = { toggle } as unknown as Menu;
    const event = new MouseEvent('click');
    const stopPropagation = vi.spyOn(event, 'stopPropagation');

    component.openRowActions(event, menu, mockBatches[0]);
    const firstItems = component.rowMenuItems;
    firstItems[1].command?.({});
    expect(toggle).toHaveBeenCalledWith(event);
    expect(stopPropagation).toHaveBeenCalledOnce();
    expect(component.rowMenuItems).toBe(firstItems);

    component.canCorrectReceiptCost = true;
    component.openRowActions(event, menu, { ...mockBatches[0], id: 'b2' });
    for (const item of component.rowMenuItems) item.command?.({});
    expect(events).toEqual([
      { action: 'adjust', batchId: 'b1' },
      ...['printLabels', 'adjust', 'count', 'edit', 'correctCost', 'void'].map((action) => ({
        action,
        batchId: 'b2',
      })),
    ]);
  });

  it('offers receipt cost correction to an owner on an active batch only', () => {
    component.canCorrectReceiptCost = true;

    expect(component.rowActionItems(mockBatches[0]).map((item) => item.id)).toEqual([
      'printLabels',
      'adjust',
      'count',
      'edit',
      'correctCost',
      'void',
    ]);
    expect(
      component
        .rowActionItems({ ...mockBatches[0], isVoided: true })
        .some((item) => item.id === 'correctCost'),
    ).toBe(false);
  });

  it('starts hiding an open menu before retargeting it to another row', () => {
    const event = new MouseEvent('click');
    const hide = vi.fn();
    const toggle = vi.fn();
    const show = vi.fn();
    const menu = {
      visible: true,
      target: document.createElement('button'),
      hide,
      toggle,
      show,
    } as unknown as Menu;

    const nextTrigger = document.createElement('button');
    nextTrigger.addEventListener('click', (current) =>
      component.openRowActions(current as MouseEvent, menu, { ...mockBatches[0], id: 'b2' }),
    );
    nextTrigger.dispatchEvent(event);

    expect(hide).toHaveBeenCalledOnce();
    expect(toggle).not.toHaveBeenCalled();
    expect(show).not.toHaveBeenCalled();
    component.onRowMenuHidden();
    expect(event.currentTarget).toBeNull();
    expect(show).toHaveBeenCalledWith({ currentTarget: nextTrigger });
    component.onRowMenuHidden();
    expect(show).toHaveBeenCalledOnce();
  });

  it('hides receipt cost correction from a manager', () => {
    component.canCorrectReceiptCost = false;

    expect(component.rowActionItems(mockBatches[0]).some((item) => item.id === 'correctCost')).toBe(
      false,
    );
  });

  it('should emit selection updates for selectable rows', () => {
    const events: string[][] = [];
    component.selectionChange.subscribe((value) => events.push([...value]));

    component.onRowSelectionChange(mockBatches[0], true);

    expect(events).toEqual([['b1']]);
  });

  it('should reflect selected batch ids in selection helpers', () => {
    component.selectedBatchIds = ['b1'];

    expect(component.isBatchSelected('b1')).toBe(true);
    expect(component.isBatchSelected('missing')).toBe(false);
    expect(component.isAllSelectableSelected()).toBe(true);
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
