import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DashboardMetricCardComponent, DashboardMetricCardDelta } from './dashboard-metric-card.component';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { By } from '@angular/platform-browser';

describe('DashboardMetricCardComponent', () => {
  let fixture: ComponentFixture<DashboardMetricCardComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardMetricCardComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    });

    fixture = TestBed.createComponent(DashboardMetricCardComponent);
  });

  it('renders currency-formatted value', () => {
    fixture.componentInstance.label = 'dashboard.salesBooked';
    fixture.componentInstance.value = 2500;
    fixture.componentInstance.currency = true;
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('₹');
  });

  it('renders loading state', () => {
    fixture.componentInstance.label = 'dashboard.salesBooked';
    fixture.componentInstance.loading = true;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('—');
  });

  it('renders delta arrow and value', () => {
    const delta: DashboardMetricCardDelta = { direction: 'up', value: 120, currency: true };
    fixture.componentInstance.label = 'dashboard.netSalesBooked';
    fixture.componentInstance.value = 1200;
    fixture.componentInstance.currency = true;
    fixture.componentInstance.delta = delta;
    fixture.detectChanges();

    const badge = fixture.debugElement.query(By.css('.kpi-comparison'));
    expect(badge).not.toBeNull();
    expect((badge.nativeElement as HTMLElement).textContent).toContain('↑');
  });

  it('renders percent delta', () => {
    fixture.componentInstance.label = 'dashboard.creditSalesPercentage';
    fixture.componentInstance.value = '25%';
    fixture.componentInstance.delta = { direction: 'down', value: -0.05, percent: true };
    fixture.detectChanges();

    const badge = fixture.debugElement.query(By.css('.kpi-comparison'));
    expect(badge?.nativeElement.textContent).toContain('↓');
  });

  it('does not translate label when disabled', () => {
    fixture.componentInstance.label = 'Acme Holdings';
    fixture.componentInstance.value = 100;
    fixture.componentInstance.translateLabel = false;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Acme Holdings');
  });
});
