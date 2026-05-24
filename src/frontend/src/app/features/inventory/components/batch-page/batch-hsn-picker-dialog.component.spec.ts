import { TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { By } from '@angular/platform-browser';
import { describe, it, expect, beforeEach } from 'vitest';

import { BatchHsnPickerDialogComponent } from './batch-hsn-picker-dialog.component';

describe('BatchHsnPickerDialogComponent', () => {
  const hsnResult = {
    hsnCodes: ['0401', '0402'],
    taxScenarios: [
      { condition: 'General dairy', taxPercentage: '5%' },
      { condition: 'Special', taxPercentage: '12%' },
    ],
  };

  async function setup(props: { hsnResult?: typeof hsnResult | null; visible?: boolean } = {}) {
    TestBed.configureTestingModule({
      imports: [
        BatchHsnPickerDialogComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(BatchHsnPickerDialogComponent);
    fixture.componentRef.setInput('hsnResult', props.hsnResult === undefined ? hsnResult : props.hsnResult);
    fixture.componentRef.setInput('visible', props.visible ?? true);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('renders picker card when visible=true and hsnResult is set', async () => {
    const fixture = await setup({ visible: true });
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).not.toBeNull();
  });

  it('does not render picker card when visible=false', async () => {
    const fixture = await setup({ visible: false });
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).toBeNull();
  });

  it('does not render picker card when hsnResult is null', async () => {
    const fixture = await setup({ hsnResult: null });
    expect(fixture.debugElement.query(By.css('.hsn-picker-card'))).toBeNull();
  });

  it('initializes pickerHsnCode with first HSN code from hsnResult', async () => {
    const fixture = await setup();
    expect(fixture.componentInstance.pickerHsnCode).toBe('0401');
  });

  it('initializes pickerTaxRate with first tax scenario from hsnResult', async () => {
    const fixture = await setup();
    expect(fixture.componentInstance.pickerTaxRate).toBe('5%');
  });

  it('emits hsnSelected when apply is called with valid selection', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const selected: { hsnCode: string; taxRate: string }[] = [];
    component.hsnSelected.subscribe((v) => selected.push(v));

    component.pickerHsnCode = '0402';
    component.pickerTaxRate = '12%';
    component.apply();

    expect(selected).toHaveLength(1);
    expect(selected[0]).toEqual({ hsnCode: '0402', taxRate: '12%' });
  });

  it('does not emit hsnSelected when pickerHsnCode is null', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    const selected: unknown[] = [];
    component.hsnSelected.subscribe((v) => selected.push(v));

    component.pickerHsnCode = null;
    component.pickerTaxRate = '12%';
    component.apply();

    expect(selected).toHaveLength(0);
  });

  it('emits cancel when onCancel is called', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;
    let cancelled = false;
    component.cancel.subscribe(() => (cancelled = true));

    component.onCancel();

    expect(cancelled).toBe(true);
  });

  it('filterHsn filters hsnOptions by query', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.filterHsn({ query: '0402', originalEvent: new Event('input') });

    expect(component.filteredHsnOptions()).toEqual(['0402']);
  });

  it('filterTax filters tax options by query', async () => {
    const fixture = await setup();
    const component = fixture.componentInstance;

    component.filterTax({ query: '12', originalEvent: new Event('input') });

    expect(component.filteredTaxOptions()).toEqual(['12%']);
  });
});
