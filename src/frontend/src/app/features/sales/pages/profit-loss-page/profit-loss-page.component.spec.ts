import { TestBed } from '@angular/core/testing';
import { ProfitLossPageComponent } from './profit-loss-page.component';
import { SalesFacade } from '../../state/sales.facade';
import { signal } from '@angular/core';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { TableModule } from 'primeng/table';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';

describe('ProfitLossPageComponent', () => {
  const mockSalesFacade = {
    profitLossItems: signal<any[]>([]),
    loadingProfitLossReport: signal(false),
    errorMessage: signal(''),
    loadProfitLossReport: vi.fn(),
  };

  async function setup(report: any[] = []) {
    mockSalesFacade.profitLossItems.set(report);
    mockSalesFacade.loadingProfitLossReport.set(false);
    mockSalesFacade.errorMessage.set('');
    mockSalesFacade.loadProfitLossReport.mockClear();

    await TestBed.configureTestingModule({
      imports: [
        ProfitLossPageComponent,
        TranslocoTestingModule.forRoot({
          langs: {
            en: {
              sales: {
                searchPlaceholder: 'Search',
                history: { walkIn: 'Walk-in' },
                profitLoss: {
                  eyebrow: 'Sales Report',
                  title: 'Profit & Loss Report',
                  subtitle: 'Analyze your margins from each sale',
                  search: 'Search',
                  invoice: 'Reference',
                  date: 'Date',
                  customer: 'Party',
                  rowType: 'Type',
                  cost: 'Total Cost',
                  wastageCost: 'Wastage Cost',
                  revenueExclTax: 'Rev Excl Tax',
                  revenueInclTax: 'Rev Incl Tax',
                  profitBeforeTax: 'Profit Before Tax',
                  profitAfterTax: 'Profit After Tax',
                  noData: 'No data',
                  adjustmentParty: 'Inventory adjustment',
                  rowTypes: {
                    Sale: 'Sale',
                    SaleReturn: 'Sale return',
                    InventoryAdjustment: 'Inventory adjustment',
                  },
                },
              },
            },
          },
          translocoConfig: { availableLangs: ['en'], defaultLang: 'en' },
          preloadLangs: true,
        }),
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
    const { component } = await setup([
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
    ]);

    component.searchValue.set('Alice');
    expect(component.filteredReport().length).toBe(1);
    expect(component.filteredReport()[0].referenceNumber).toBe('INV-1');

    component.searchValue.set('RET-1');
    expect(component.filteredReport().length).toBe(1);
    expect(component.filteredReport()[0].partyName).toBe('Bob');
  });

  it('renders inventory adjustment rows without customer or sale link assumptions', async () => {
    const { fixture } = await setup([
      {
        saleId: null,
        referenceNumber: 'ADJ-LOSS-1',
        occurredAt: '2026-05-05T10:00:00Z',
        partyName: null,
        totalCost: 0,
        wastageCost: 80,
        revenueBeforeTax: 0,
        revenueAfterTax: 0,
        profitBeforeTax: -80,
        profitAfterTax: -80,
        rowType: 'InventoryAdjustment',
        inventoryAdjustmentId: 'adjustment-1',
      },
    ]);

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('ADJ-LOSS-1');
    expect(text).toContain('Inventory adjustment');
    expect(text).not.toContain('Walk-in');
  });

  it('returns correct severity for profit/loss', async () => {
    const { component } = await setup();
    expect(component.getProfitSeverity(100)).toBe('success');
    expect(component.getProfitSeverity(0)).toBe('success');
    expect(component.getProfitSeverity(-50)).toBe('danger');
  });
});
