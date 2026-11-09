import { TestBed } from '@angular/core/testing';
import { FormControl, Validators } from '@angular/forms';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { CartCheckoutSummaryComponent } from './cart-checkout-summary.component';
import { SalePreviewDto } from '../../../../features/sales/services/sale.models';

describe('CartCheckoutSummaryComponent', () => {
  it('associates sale discount labels and errors with native controls', async () => {
    TestBed.configureTestingModule({ imports: [CartCheckoutSummaryComponent, TranslocoTestingModule.forRoot({
      langs: { en: { sales: { newSale: { discounts: { editSale: 'Edit sale discount', percent: 'Percent' } } } } },
      translocoConfig: { availableLangs: ['en'], defaultLang: 'en' }, preloadLangs: true,
    })] });
    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentInstance.isDiscountEditorOpen = true;
    fixture.componentInstance.saleDiscountError = 'sales.newSale.discounts.fixErrors';
    fixture.componentInstance.instantDiscountTypeOptions = [{ value: 1, labelKey: 'sales.newSale.discounts.percent' }];
    fixture.componentInstance.saleDiscountType = 1;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const controls = [root.querySelector<HTMLElement>('[role="combobox"]'), root.querySelector<HTMLInputElement>('input.p-inputnumber-input')];
    for (const control of controls) {
      expect(control?.id).toBeTruthy();
      expect(root.querySelector(`label[for="${control?.id}"]`)).toBeTruthy();
    }
    const combobox = controls[0]!;
    const label = root.querySelector<HTMLLabelElement>(`label[for="${combobox.id}"]`)!;
    expect(combobox.getAttribute('aria-labelledby')).toBe(label.id);
    expect(label.textContent?.trim()).toBe('Edit sale discount');
    const input = controls[1]!;
    expect(root.querySelector(`#${input.getAttribute('aria-describedby')}`)?.textContent).toContain('fixErrors');
  });
  it('shows accepted net revenue cost and profit, and clears them with the preview', () => {
    TestBed.configureTestingModule({ imports: [CartCheckoutSummaryComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })] });
    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentInstance.preview = { totalAmount: 236, totalTaxableAmount: 200, totalTaxAmount: 36, totalDiscountAmount: 0,
      saleLevelEligibleSubtotal: 200, configuredSaleRule: null, infos: [], warnings: [],
      lines: [{ economics: { netRevenue: 200, economicCost: 100, grossProfit: 100, grossMarginPercent: 50, markupPercent: 100 } } as SalePreviewDto['lines'][number]] };
    fixture.detectChanges();
    const text = fixture.nativeElement.querySelector('[data-testid="checkout-economics"]').textContent;
    expect(text).toContain('200.00'); expect(text).toContain('100.00');
    expect(fixture.componentInstance.recordedCost).toBe(100);
    expect(fixture.componentInstance.grossProfit).toBe(100);
    fixture.componentRef.setInput('preview', null); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="checkout-economics"]')).toBeNull();
  });

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

  it('keeps explicit invalid submit reachable so the page can focus its validation summary', () => {
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
    expect((buttons[buttons.length - 1] as HTMLButtonElement).disabled).toBe(false);
  });

  it('announces settled totals without changing its live region during preview recalculation', () => {
    TestBed.configureTestingModule({ imports: [CartCheckoutSummaryComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })] });
    const fixture = TestBed.createComponent(CartCheckoutSummaryComponent);
    fixture.componentRef.setInput('cartLength', 1);
    fixture.componentRef.setInput('totalAmount', 120); fixture.detectChanges();
    const status = fixture.nativeElement.querySelector('[role="status"]') as HTMLElement;
    expect(status?.textContent).toContain('120.00');
    fixture.componentRef.setInput('loading', true);
    fixture.componentRef.setInput('totalAmount', 135); fixture.detectChanges();
    expect(status.textContent).toContain('120.00');
    fixture.componentRef.setInput('loading', false); fixture.detectChanges();
    expect(status.textContent).toContain('135.00');
  });
});
