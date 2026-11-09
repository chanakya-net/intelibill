import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { setupNewSalePageTestBed } from '../../../../tests/sales/new-sale-page/test-helpers';
import { NewSalePageComponent } from './new-sale-page.component';

describe('checkout keyboard feedback', () => {
  afterEach(() => { vi.useRealTimers(); TestBed.resetTestingModule(); });

  it('invalid_payment_has_one_summary and explicit submission focuses its associated control', async () => {
    vi.useFakeTimers();
    const { deps } = setupNewSalePageTestBed();
    const fixture = TestBed.createComponent(NewSalePageComponent); fixture.detectChanges();
    const component = fixture.componentInstance; const vm = component.vm;
    vm.searchInput.set('oreo'); await vm.onBarcodeSearch();
    await vi.advanceTimersByTimeAsync(300); fixture.detectChanges();
    // A validator refusal is independent of the payment arithmetic subscriptions.
    vm.paymentForm.controls.paidAmount.setErrors({ min: true });
    vm.paymentSplitError.set('sales.newSale.invalidPaymentSplit'); fixture.detectChanges();
    component.onPaymentSubmitRequested(); fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0); fixture.detectChanges();
    const summary = fixture.nativeElement.querySelector('[data-checkout-error-summary]') as HTMLElement;
    expect(summary).toBeTruthy();
    expect(fixture.nativeElement.querySelectorAll('[data-checkout-error-summary]')).toHaveLength(1);
    expect(summary.getAttribute('role')).toBe('alert');
    // Independent till-capability feedback can also be present. This failed payment
    // has exactly one live summary; its field description stays non-live.
    expect(Array.from(fixture.nativeElement.querySelectorAll('[role="alert"]') as NodeListOf<HTMLElement>).filter(node => node.textContent?.includes('sales.newSale.invalidPaymentSplit'))).toHaveLength(1);
    expect(document.activeElement).toBe(summary);
    const link = summary.querySelector<HTMLAnchorElement>('a')!;
    expect(document.getElementById(link.hash.slice(1))?.getAttribute('aria-invalid')).toBe('true');
    expect(deps.salesFacade.recordSale).not.toHaveBeenCalled();
  });

  it('does not steal focus when background errors or retry feedback changes', () => {
    setupNewSalePageTestBed();
    const fixture = TestBed.createComponent(NewSalePageComponent); fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.focus();
    fixture.componentInstance.vm.paymentSplitError.set('sales.newSale.previewRequired');
    fixture.detectChanges();
    expect(document.activeElement).toBe(input);
  });
});
