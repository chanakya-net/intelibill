import { TestBed } from '@angular/core/testing';
import { ProfitLossPageComponent } from './profit-loss-page.component';
import { SalesFacade } from '../../state/sales.facade';
import { signal } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { TableModule } from 'primeng/table';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

describe('ProfitLossPageComponent', () => {
  const mockSalesFacade = {
    profitLossReport: signal([]),
    loadingProfitLossReport: signal(false),
    errorMessage: signal(''),
    loadProfitLossReport: vi.fn(),
  };

  async function setup() {
    await TestBed.configureTestingModule({
      imports: [
        ProfitLossPageComponent,
        TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true }),
        TableModule,
        NoopAnimationsModule,
      ],
      providers: [{ provide: SalesFacade, useValue: mockSalesFacade }],
    }).compileComponents();

    const fixture = TestBed.createComponent(ProfitLossPageComponent);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    return { fixture, component };
  }

  it('loads report on init', async () => {
    await setup();
    expect(mockSalesFacade.loadProfitLossReport).toHaveBeenCalled();
  });

  it('filters report based on search value', async () => {
    const { component } = await setup();
    mockSalesFacade.profitLossReport.set([
      {
        saleId: 'sale-1',
        referenceNumber: 'INV-1',
        occurredAt: '2026-05-05T10:00:00Z',
        partyName: 'Alice',
        totalCost: 0,
        wastageCost: 0,
        revenueBeforeTax: 0,
        revenueAfterTax: 0,
        profitBeforeTax: 0,
        profitAfterTax: 0,
        rowType: 'Sale',
        inventoryAdjustmentId: null,
      },
      {
        saleId: 'sale-2',
        referenceNumber: 'INV-2 / RET-1',
        occurredAt: '2026-05-06T10:00:00Z',
        partyName: 'Bob',
        totalCost: -10,
        wastageCost: 0,
        revenueBeforeTax: -20,
        revenueAfterTax: -22,
        profitBeforeTax: -12,
        profitAfterTax: -10,
        rowType: 'SaleReturn',
        inventoryAdjustmentId: null,
      },
    ] as any);

    component.searchValue.set('Alice');
    expect(component.filteredReport().length).toBe(1);
    expect(component.filteredReport()[0].referenceNumber).toBe('INV-1');

    component.searchValue.set('RET-1');
    expect(component.filteredReport().length).toBe(1);
    expect(component.filteredReport()[0].partyName).toBe('Bob');
  });

  it('returns correct severity for profit/loss', async () => {
    const { component } = await setup();
    expect(component.getProfitSeverity(100)).toBe('success');
    expect(component.getProfitSeverity(0)).toBe('success');
    expect(component.getProfitSeverity(-50)).toBe('danger');
  });
});
