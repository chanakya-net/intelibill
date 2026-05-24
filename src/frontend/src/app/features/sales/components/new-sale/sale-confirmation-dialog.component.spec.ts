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
    const printA4Spy = vi.fn();
    const printThermalSpy = vi.fn();

    component.closed.subscribe(closeSpy);
    component.printA4Requested.subscribe(printA4Spy);
    component.printThermalRequested.subscribe(printThermalSpy);

    component.onClose();
    component.onPrintA4();
    component.onPrintThermal();

    expect(closeSpy).toHaveBeenCalled();
    expect(printA4Spy).toHaveBeenCalled();
    expect(printThermalSpy).toHaveBeenCalled();
  });

  it('renders offline queued state copy and icon', () => {
    TestBed.configureTestingModule({
      imports: [SaleConfirmationDialogComponent, TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true })],
    });

    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    fixture.componentInstance.visible = true;
    fixture.componentInstance.saleResult = { ...sale, isOffline: true };
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    const icon = fixture.nativeElement.querySelector('i') as HTMLElement;
    expect(text).toContain('sales.newSale.offline.confirmation.queued');
    expect(text).toContain('sales.newSale.offline.confirmation.description');
    expect(icon.className).toContain('pi-clock');
  });
});
