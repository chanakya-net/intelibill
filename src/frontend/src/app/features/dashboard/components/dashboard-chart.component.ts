import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Output, computed, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { ChartData, DashboardChartType } from '../utils/dashboard-chart-builders';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

type ChartTypeOption = {
  labelKey: string;
  value: DashboardChartType;
};

const CHART_TYPE_OPTIONS: ChartTypeOption[] = [
  { labelKey: 'dashboard.chartTypeBar', value: 'bar' as const },
  { labelKey: 'dashboard.chartTypeStackedBar', value: 'stackedBar' as const },
  { labelKey: 'dashboard.chartTypeLine', value: 'line' as const },
  { labelKey: 'dashboard.chartTypePie', value: 'pie' as const },
  { labelKey: 'dashboard.chartTypeDoughnut', value: 'doughnut' as const },
];

@Component({
  selector: 'app-dashboard-chart',
  standalone: true,
  imports: [CommonModule, FormsModule, CardModule, ChartModule, ProgressSpinnerModule, SelectModule, TranslocoPipe],
  templateUrl: './dashboard-chart.component.html',
  styleUrl: './dashboard-chart.component.scss',
})
export class DashboardChartComponent {
  readonly chartData = input<ChartData | null>(null);
  readonly chartType = input<DashboardChartType>('bar');
  readonly loading = input(false);
  readonly title = input('');
  readonly chartOptions = input<Record<string, unknown> | null>(null);

  @Output() readonly chartTypeChange = new EventEmitter<DashboardChartType>();
  private readonly transloco = inject(TranslocoService);

  readonly chartTypeOptions: ChartTypeOption[] = CHART_TYPE_OPTIONS;
  readonly translatedChartData = computed<ChartData | null>(() => this.translateChartData(this.chartData()));

  get renderChartType(): 'bar' | 'line' | 'pie' | 'doughnut' {
    const type = this.chartType();
    if (type === 'stackedBar') return 'bar';
    return type;
  }

  onChartTypeChange(next: DashboardChartType): void {
    this.chartTypeChange.emit(next);
  }

  private translateChartData(chartData: ChartData | null): ChartData | null {
    if (!chartData) return null;

    return {
      labels: chartData.labels.map((label) => this.transloco.translate(label)),
      datasets: chartData.datasets.map((dataset) => ({
        ...dataset,
        label: dataset.label ? this.transloco.translate(dataset.label) : dataset.label,
      })),
    };
  }
}
