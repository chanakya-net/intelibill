import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { describe, expect, it, vi } from 'vitest';

import { CreateSaleResponse, SaleConfirmationDialogComponent } from './sale-confirmation-dialog.component';

describe('SaleConfirmationDialogComponent', () => {
  const sale: CreateSaleResponse = {
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    totalAmount: 120,
  };

  it('renders sale details when visible', () => {
    TestBed.configureTestingModule({
      imports: [SaleConfirmationDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    fixture.componentInstance.visible = true;
    fixture.componentInstance.saleResult = sale;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('INV-001');
    expect(text).toContain('120.00');
  });

  it('emits closed and print events', () => {
    TestBed.configureTestingModule({
      imports: [SaleConfirmationDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    const component = fixture.componentInstance;

    const closeSpy = vi.fn();
    const printSpy = vi.fn();

    component.closed.subscribe(closeSpy);
    component.printRequested.subscribe(printSpy);

    component.onClose();
    component.onPrint();

    expect(closeSpy).toHaveBeenCalled();
    expect(printSpy).toHaveBeenCalled();
  });
});
