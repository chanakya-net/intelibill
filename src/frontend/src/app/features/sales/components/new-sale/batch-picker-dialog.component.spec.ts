import { LocalizationService } from '../../../../core/i18n/localization.service';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { vi, describe, expect, it } from 'vitest';

import { BatchPickerDialogComponent } from './batch-picker-dialog.component';
import type { SellableGoodsDto } from '../../../sales/services/sale.models';

beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [{ provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } }],
  });
});

describe('BatchPickerDialogComponent', () => {
  it('restores the search opener even when suggestions blurred it before the picker opened', async () => {
    TestBed.configureTestingModule({
      providers: [{ provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } }],
      imports: [
        BatchPickerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });
    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    fixture.detectChanges();
    const opener = document.createElement('input');
    document.body.append(opener);
    opener.focus();
    opener.blur();
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.activeElement).toBe(opener);
    opener.remove();
  });
  const batch: SellableGoodsDto = {
    kind: 'Goods',
    itemName: 'Milk',
    barcode: 'MILK123',
    batchNumber: 'B-01',
    inventoryBatchId: 'inv-batch-1',
    quantity: 10,
    mrp: 50,
    salesPrice: 55,
    taxRatePercent: 18,
    taxIncluded: true,
    expiryDate: null,
  };

  it('emits selected batch when add is confirmed', () => {
    TestBed.configureTestingModule({
      imports: [
        BatchPickerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();

    component.visible = true;
    component.batches = [batch];
    component.batchSelected.subscribe(spy);

    component.onSelectBatch(batch);
    component.onAddToCart();

    expect(spy).toHaveBeenCalledWith(batch);
  });

  it('never adds a blocked diagnostic lot even through a direct selection call', () => {
    TestBed.configureTestingModule({
      imports: [BatchPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
    });
    const component = TestBed.createComponent(BatchPickerDialogComponent).componentInstance;
    const emitted = vi.fn();
    component.batchSelected.subscribe(emitted);
    const blocked = {
      ...batch,
      quantity: 10,
      availableQuantity: 0,
      sellability: {
        allowed: false,
        reasonCode: 'Expiry.Expired',
        warning: false,
        requiresApproval: false,
        policyVersion: 2,
        catalogVersion: 3,
        businessDate: '2026-11-09',
        physicalQuantity: 10,
        quarantinedQuantity: 0,
        availableQuantity: 0,
      },
    };
    component.onSelectBatch(blocked);
    component.onAddToCart();
    expect(emitted).not.toHaveBeenCalled();
  });

  it('orders dated eligible lots FEFO, with stable batch identity ties and undated lots last', () => {
    TestBed.configureTestingModule({
      imports: [BatchPickerDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} } })],
    });
    const component = TestBed.createComponent(BatchPickerDialogComponent).componentInstance;
    component.batches = [
      { ...batch, batchNumber: 'undated', inventoryBatchId: 'u', expiryDate: null },
      { ...batch, batchNumber: 'late', inventoryBatchId: 'l', expiryDate: '2027-11-09' },
      { ...batch, batchNumber: 'same', inventoryBatchId: 'b', expiryDate: '2027-11-08' },
      { ...batch, batchNumber: 'same', inventoryBatchId: 'a', expiryDate: '2027-11-08' },
    ];
    expect(
      component.orderedBatches.map((x) => (x.kind === 'Goods' ? x.inventoryBatchId : '')),
    ).toEqual(['a', 'b', 'l', 'u']);
  });

  it('emits quantity changes', () => {
    TestBed.configureTestingModule({
      imports: [
        BatchPickerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();
    component.quantityChanged.subscribe(spy);

    component.onQuantityChanged(5);

    expect(spy).toHaveBeenCalledWith(5);
  });

  function renderWithSelection(selected: SellableGoodsDto) {
    TestBed.configureTestingModule({
      imports: [
        BatchPickerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const selectedSpy = vi.fn();
    const quantitySpy = vi.fn();
    component.batchSelected.subscribe(selectedSpy);
    component.quantityChanged.subscribe(quantitySpy);
    component.visible = true;
    component.batches = [selected];
    component.onSelectBatch(selected);
    fixture.detectChanges();

    const input = document.querySelector('app-quantity-field input') as HTMLInputElement;
    const type = (text: string): void => {
      input.value = text;
      input.dispatchEvent(new Event('input'));
    };
    return { fixture, component, input, type, selectedSpy, quantitySpy };
  }

  it('takes an exact fraction of a weighed batch', () => {
    const kg: SellableGoodsDto = { ...batch, uom: 'kg', quantityScale: 3, quantity: 2.5 };
    const { component, input, type, selectedSpy, quantitySpy } = renderWithSelection(kg);

    expect(input.getAttribute('inputmode')).toBe('decimal');
    type('0.25');
    input.dispatchEvent(new Event('blur'));
    component.onAddToCart();

    expect(quantitySpy).toHaveBeenCalledWith(0.25);
    expect(selectedSpy).toHaveBeenCalledWith(kg);
  });

  it('adds nothing while the quantity typed is one the unit cannot hold', () => {
    const pcs: SellableGoodsDto = { ...batch, uom: 'PCS', quantityScale: 0 };
    const { fixture, component, input, type, selectedSpy, quantitySpy } = renderWithSelection(pcs);

    type('0.5');
    component.onAddToCart();
    fixture.detectChanges();

    expect(quantitySpy).not.toHaveBeenCalled();
    expect(selectedSpy).not.toHaveBeenCalled();
    expect(input.getAttribute('aria-invalid')).toBe('true');
  });

  it('adds nothing beyond the stock of the selected batch', () => {
    const { component, type, selectedSpy } = renderWithSelection({ ...batch, quantity: 3 });

    type('4');
    component.onAddToCart();

    expect(selectedSpy).not.toHaveBeenCalled();
  });

  it('emits closed on hidden event', () => {
    TestBed.configureTestingModule({
      imports: [
        BatchPickerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(BatchPickerDialogComponent);
    const component = fixture.componentInstance;
    const spy = vi.fn();
    component.closed.subscribe(spy);

    component.onClose();

    expect(spy).toHaveBeenCalled();
  });
});
