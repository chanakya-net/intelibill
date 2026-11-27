import { LocalizationService } from '../../../../core/i18n/localization.service';
import { TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it, vi } from 'vitest';

import {
  CreateSaleResponse,
  SaleConfirmationDialogComponent,
} from './sale-confirmation-dialog.component';

const enIN = JSON.parse(
  readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8'),
) as Record<string, unknown>;

beforeEach(() => {
  TestBed.configureTestingModule({
    providers: [{ provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } }],
  });
});

describe('SaleConfirmationDialogComponent', () => {
  it('focuses confirmation and restores a detached opener to the page fallback', async () => {
    TestBed.configureTestingModule({
      providers: [{ provide: LocalizationService, useValue: { currentLanguage: () => 'en-IN' } }],
      imports: [
        SaleConfirmationDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });
    const opener = document.createElement('button');
    const fallback = document.createElement('h1');
    fallback.tabIndex = -1;
    fallback.dataset['dialogFocusFallback'] = '';
    document.body.append(opener, fallback);
    opener.focus();
    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(
      fixture.nativeElement.querySelector('[role="dialog"]')?.contains(document.activeElement),
    ).toBe(true);
    opener.remove();
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.activeElement).toBe(fallback);
    fallback.remove();
  });
  const sale: CreateSaleResponse = {
    saleId: 'sale-1',
    invoiceNumber: 'INV-001',
    totalAmount: 120,
  };

  it('renders sale details when visible', () => {
    TestBed.configureTestingModule({
      imports: [
        SaleConfirmationDialogComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    });

    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    fixture.componentInstance.visible = true;
    fixture.componentInstance.saleResult = sale;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('INV-001');
    expect(text).toContain('120.00');
  });

  it('renders credit note settlement details when present', () => {
    TestBed.configureTestingModule({
      imports: [
        SaleConfirmationDialogComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
    });

    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    fixture.componentInstance.visible = true;
    fixture.componentInstance.saleResult = {
      ...sale,
      creditNoteAppliedAmount: 25,
      creditNoteRedemptions: [
        { creditNoteId: 'cn-1', code: 'CN-001', appliedAmount: 15 },
        { creditNoteId: 'cn-2', code: 'CN-002', appliedAmount: 10 },
      ],
    };
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Credit Note Settlement');
    expect(text).toContain('₹25.00');
    expect(text).toContain('Credit Note Codes');
    expect(text).toContain('CN-001 · ₹15.00');
    expect(text).toContain('CN-002 · ₹10.00');
  });

  it('emits closed and print events', () => {
    TestBed.configureTestingModule({
      imports: [
        SaleConfirmationDialogComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
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
      imports: [
        SaleConfirmationDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
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

  it('leaves a usable print link when the browser blocked the print window', () => {
    TestBed.configureTestingModule({
      imports: [
        SaleConfirmationDialogComponent,
        TranslocoTestingModule.forRoot({ langs: { en: {} }, preloadLangs: true }),
      ],
    });

    const fixture = TestBed.createComponent(SaleConfirmationDialogComponent);
    fixture.componentInstance.visible = true;
    fixture.componentInstance.saleResult = { ...sale, isOffline: true };
    fixture.detectChanges();
    expect(document.querySelector('a.sale-confirmation__blocked-print')).toBeNull();

    fixture.componentInstance.blockedPrintUrl = '/sales/s-1/print?template=a4&offline=1';
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();

    const link = document.querySelector(
      'a.sale-confirmation__blocked-print',
    ) as HTMLAnchorElement | null;
    expect(link?.getAttribute('href')).toBe('/sales/s-1/print?template=a4&offline=1');
    expect(link?.getAttribute('target')).toBe('_blank');
    // A noopener tab does not inherit sessionStorage, where a session the cashier did not ask to remember lives.
    expect(link?.getAttribute('rel')).toBe('opener');
    expect(document.body.textContent).toContain('sales.invoice.printBlocked');
  });
});
