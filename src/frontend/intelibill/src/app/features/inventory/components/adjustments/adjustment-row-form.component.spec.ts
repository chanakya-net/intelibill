import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { beforeEach, describe, expect, it } from 'vitest';

import { InventoryBatchOption } from '../../services/inventory.models';
import { AdjustmentRowFormComponent } from './adjustment-row-form.component';

describe('AdjustmentRowFormComponent', () => {
  const batchOptions: InventoryBatchOption[] = [
    {
      id: 'batch-1',
      label: 'BATCH-001 · Rice',
      itemName: 'Rice',
      batchNumber: 'BATCH-001',
      barcode: '111',
      quantity: 10,
      uom: 'PCS',
      quantityScale: 0,
    },
    {
      id: 'batch-kg',
      label: 'BATCH-KG · Flour',
      itemName: 'Flour',
      batchNumber: 'BATCH-KG',
      barcode: '333',
      quantity: 5,
      uom: 'kg',
      quantityScale: 3,
    },
    {
      id: 'batch-empty',
      label: 'BATCH-EMPTY · Sugar',
      itemName: 'Sugar',
      batchNumber: 'BATCH-EMPTY',
      barcode: '222',
      quantity: 0,
    },
  ];

  function setup(options: InventoryBatchOption[] = batchOptions) {
    TestBed.configureTestingModule({
      imports: [
        AdjustmentRowFormComponent,
        NoopAnimationsModule,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(AdjustmentRowFormComponent);
    fixture.componentInstance.batchOptions = options;
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('initializes with Decrease direction and Damaged reason by default', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    expect(component.adjustmentForm.controls.direction.value).toBe('Decrease');
    expect(component.adjustmentForm.controls.reason.value).toBe('Damaged');
  });

  it('filters batch suggestions by search query', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.onBatchSearch({ query: 'sug' });

    expect(component.batchSuggestions().map((b) => b.id)).toEqual(['batch-empty']);
  });

  it('filters batch suggestions by barcode', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.onBatchSearch({ query: '111' });

    expect(component.batchSuggestions().map((b) => b.id)).toEqual(['batch-1']);
  });

  it('keeps the selected batch visible in the autocomplete after first selection', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.onBatchModelChange(batchOptions[0]);
    fixture.detectChanges();

    expect(component.selectedBatch()).toEqual(batchOptions[0]);
  });

  it('clears selected batch when autocomplete value is cleared', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.onSelectBatch(batchOptions[0]);
    component.onBatchModelChange(null);

    expect(component.selectedBatch()).toBeNull();
  });

  it('blocks decrease for empty-quantity batch', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.onSelectBatch(batchOptions[2]);

    expect(component.selectedBatchDecreaseBlocked()).toBe(true);
    expect(component.adjustmentForm.invalid).toBe(true);
  });

  it('allows increase even for empty-quantity batch', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.onSelectBatch(batchOptions[2]);
    component.adjustmentForm.controls.direction.setValue('Increase');

    expect(component.selectedBatchDecreaseBlocked()).toBe(false);
    expect(component.adjustmentForm.valid).toBe(true);
  });

  it('emits rowChange with correct DTO on valid form submission', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const emitted: unknown[] = [];
    component.rowChange.subscribe((row) => emitted.push(row));

    component.onSelectBatch(batchOptions[0]);
    component.adjustmentForm.patchValue({
      direction: 'Decrease',
      reason: 'Damaged',
      quantity: 3,
      notes: 'Broken packaging',
    });

    component.onSave();

    expect(emitted).toHaveLength(1);
    const dto = emitted[0] as ReturnType<
      typeof component.rowChange.emit extends (arg: infer T) => unknown ? (arg: T) => T : never
    >;
    expect((dto as { batchId: string }).batchId).toBe('batch-1');
  });

  it('does not emit when no batch is selected', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const emitted: unknown[] = [];
    component.rowChange.subscribe((row) => emitted.push(row));

    component.adjustmentForm.patchValue({ direction: 'Decrease', reason: 'Damaged', quantity: 1 });
    component.onSave();

    expect(emitted).toHaveLength(0);
  });

  it('emits cancelled when cancel button is clicked', () => {
    const fixture = setup();
    const component = fixture.componentInstance;
    const cancelled: boolean[] = [];
    component.cancelled.subscribe(() => cancelled.push(true));

    component.onCancel();

    expect(cancelled).toHaveLength(1);
  });

  it('updates reason options when direction changes to Increase', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.adjustmentForm.controls.direction.setValue('Increase');

    const reasonValues = component.adjustmentReasonOptions().map((opt) => opt.value);
    expect(reasonValues).toContain('FoundStock');
    expect(reasonValues).not.toContain('Damaged');
  });

  it('rejects whitespace-only notes when an other reason requires details', () => {
    const fixture = setup();
    const component = fixture.componentInstance;

    component.adjustmentForm.controls.reason.setValue('OtherLoss');
    component.adjustmentForm.controls.notes.setValue('   ');

    expect(component.adjustmentForm.controls.notes.hasError('required')).toBe(true);
  });

  it('takes three decimals for a weighed item', () => {
    const component = setup().componentInstance;
    component.onSelectBatch(batchOptions[1]);

    component.adjustmentForm.controls.quantity.setValue(0.125);

    expect(component.adjustmentForm.controls.quantity.valid).toBe(true);
    expect(component.adjustmentForm.controls.quantity.errors).toBeNull();
  });

  it('refuses a fraction of a whole-unit item without rounding it', () => {
    const component = setup().componentInstance;
    component.onSelectBatch(batchOptions[0]);

    component.adjustmentForm.controls.quantity.setValue(0.5);

    expect(component.adjustmentForm.controls.quantity.errors).toHaveProperty('maxFractionDigits');
    expect(component.adjustmentForm.controls.quantity.value).toBe(0.5);
    expect(component.quantityPrecisionError()).toEqual({
      key: 'common.quantity.errors.tooPreciseWhole',
      params: { unit: 'PCS', decimals: 0, step: '1' },
    });
  });

  it('refuses a fourth decimal and names the unit and allowed decimals', () => {
    const component = setup().componentInstance;
    component.onSelectBatch(batchOptions[1]);

    component.adjustmentForm.controls.quantity.setValue(0.1234);

    expect(component.adjustmentForm.controls.quantity.invalid).toBe(true);
    expect(component.quantityPrecisionError()).toEqual({
      key: 'common.quantity.errors.tooPrecise',
      params: { unit: 'kg', decimals: 3, step: '0.001' },
    });
  });

  it('checks the precision again when the direction changes validators', () => {
    const component = setup().componentInstance;
    component.onSelectBatch(batchOptions[0]);
    component.adjustmentForm.controls.quantity.setValue(2.5);
    component.adjustmentForm.controls.direction.setValue('Increase');

    expect(component.adjustmentForm.controls.quantity.errors).toHaveProperty('maxFractionDigits');
  });

  it('sends the typed quantity unrounded', () => {
    const component = setup().componentInstance;
    component.onSelectBatch(batchOptions[1]);
    component.adjustmentForm.controls.quantity.setValue(0.125);
    let emitted: { quantity: number } | undefined;
    component.rowChange.subscribe((dto) => (emitted = dto));

    component.onSave();

    expect(emitted?.quantity).toBe(0.125);
  });
});
