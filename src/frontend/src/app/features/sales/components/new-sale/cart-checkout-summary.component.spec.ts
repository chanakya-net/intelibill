import { TestBed } from '@angular/core/testing';
import { FormControl, Validators } from '@angular/forms';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { CartCheckoutSummaryComponent } from './cart-checkout-summary.component';
import { SalePreviewDto } from '../../../../features/sales/services/sale.models';

describe('CartCheckoutSummaryComponent', () => {
  it('renders computed totals from preview', () => {
    const preview: SalePreviewDto = {
      totalAmount: 120,
      totalTaxableAmount: 100,
      totalTaxAmount: 20,
      totalDiscountAmount: 5,
      saleLevelEligibleSubtotal: 100,
      configuredSaleRule: null,
      lines: [],
      infos: [],
      warnings: [],
    };

    TestBed.configureTestingModule({
      imports: [CartCheckoutSummaryComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentInstance.preview = preview;
    fixture.componentInstance.totalAmount = preview.totalAmount;
    fixture.componentInstance.totalTaxAmount = preview.totalTaxAmount;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('₹100.00');
    expect(text).toContain('₹20.00');
    expect(text).toContain('₹120.00');
  });

  it('emits sale discount events', () => {
    const typeSpy = vi.fn();
    const valueSpy = vi.fn();
    const toggleSpy = vi.fn();

    TestBed.configureTestingModule({
      imports: [CartCheckoutSummaryComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentInstance.saleDiscountTypeChanged.subscribe(typeSpy);
    fixture.componentInstance.saleDiscountValueChanged.subscribe(valueSpy);
    fixture.componentInstance.saleDiscountEditorToggled.subscribe(toggleSpy);

    fixture.componentInstance.onDiscountTypeChanged(1);
    fixture.componentInstance.onDiscountValueChanged(5);
    fixture.componentInstance.toggleEditor();

    expect(typeSpy).toHaveBeenCalledWith(1);
    expect(valueSpy).toHaveBeenCalledWith(5);
    expect(toggleSpy).toHaveBeenCalled();
  });

  it('places record sale after total due and emits submit requests', () => {
    const submitSpy = vi.fn();

    TestBed.configureTestingModule({
      imports: [CartCheckoutSummaryComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentInstance.cartLength = 1;
    fixture.componentInstance.submitRequested.subscribe(submitSpy);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text.indexOf('sales.newSale.totalDue')).toBeLessThan(text.indexOf('sales.newSale.recordSale'));

    const buttons = fixture.nativeElement.querySelectorAll('button');
    const button = buttons[buttons.length - 1] as HTMLButtonElement;
    button.click();

    expect(submitSpy).toHaveBeenCalledOnce();
  });

  it('disables record sale while checkout forms are invalid', () => {
    TestBed.configureTestingModule({
      imports: [
        CartCheckoutSummaryComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentInstance.cartLength = 1;
    fixture.componentInstance.customerForm = new FormControl('', Validators.required);
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('button');
    expect((buttons[buttons.length - 1] as HTMLButtonElement).disabled).toBe(true);
  });
});
