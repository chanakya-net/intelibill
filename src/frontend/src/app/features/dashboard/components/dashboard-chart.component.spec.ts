import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { vi } from 'vitest';
import { TranslocoTestingModule } from '@ngneat/transloco';

import { DashboardChartComponent } from './dashboard-chart.component';
import { DashboardChartType } from '../utils/dashboard-chart-builders';

describe('DashboardChartComponent', () => {
  let fixture: ComponentFixture<DashboardChartComponent>;
  let component: DashboardChartComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [DashboardChartComponent, TranslocoTestingModule.forRoot({ langs: {}, preloadLangs: true })],
    });

    fixture = TestBed.createComponent(DashboardChartComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('chartData', {
      labels: ['A', 'B'],
      datasets: [{ data: [1, 2] }],
    });
    fixture.detectChanges();
  });

  it('renders chart when data exists', () => {
    expect(fixture.debugElement.query(By.css('p-chart'))).not.toBeNull();
  });

  it('emits chartTypeChange from select action', () => {
    const onChange = vi.fn();
    component.chartTypeChange.subscribe(onChange);

    component.onChartTypeChange('doughnut');

    expect(onChange).toHaveBeenCalledWith('doughnut' as DashboardChartType);
  });

  it('supports donut chart type', () => {
    fixture.componentRef.setInput('chartType', 'doughnut');
    fixture.detectChanges();

    const chart = fixture.debugElement.query(By.css('p-chart'));
    expect(component.renderChartType).toBe('doughnut');
    expect(chart).not.toBeNull();
    expect(chart?.componentInstance.type).toBe('doughnut');
  });
});
