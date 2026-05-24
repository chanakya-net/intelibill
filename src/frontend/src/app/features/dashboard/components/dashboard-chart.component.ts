import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { ChartData, DashboardChartType } from '../utils/dashboard-chart-builders';
import { TranslocoPipe } from '@ngneat/transloco';

const CHART_TYPE_OPTIONS = [
  { label: 'Column Bar', value: 'bar' as const },
  { label: 'Column Bar Stacked', value: 'stackedBar' as const },
  { label: 'Line', value: 'line' as const },
  { label: 'Pie', value: 'pie' as const },
  { label: 'Donut', value: 'doughnut' as const },
];

@Component({
  selector: 'app-dashboard-chart',
  standalone: true,
  imports: [CommonModule, FormsModule, CardModule, ChartModule, ProgressSpinnerModule, SelectModule, TranslocoPipe],
  templateUrl: './dashboard-chart.component.html',
  styleUrl: './dashboard-chart.component.scss',
})
export class DashboardChartComponent {
  @Input() chartData: ChartData | null = null;
  @Input() chartType: DashboardChartType = 'bar';
  @Input() loading = false;
  @Input() title = '';
  @Input() chartOptions: Record<string, unknown> | null = null;

  @Output() chartTypeChange = new EventEmitter<DashboardChartType>();

  readonly chartTypeOptions = CHART_TYPE_OPTIONS;

  get renderChartType(): 'bar' | 'line' | 'pie' | 'doughnut' {
    return this.chartType === 'stackedBar' ? 'bar' : this.chartType;
  }

  onChartTypeChange(next: DashboardChartType): void {
    this.chartTypeChange.emit(next);
  }
}
