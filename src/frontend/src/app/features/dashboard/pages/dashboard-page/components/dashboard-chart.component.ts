import { CommonModule } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { ChartData, ChartOptions } from 'chart.js';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';

import {
  DashboardDto,
  RevenueVsExpensesPointDto,
  SalesTrendPointDto,
} from '../../../services/dashboard.models';

const chartColors = {
  revenue: '#f27a20',
  revenueMuted: 'rgba(242, 122, 32, 0.82)',
  expenses: '#8b7355',
  expensesMuted: 'rgba(139, 115, 85, 0.78)',
} as const;

const rupeeFormatter = new Intl.NumberFormat('en-IN', {
  style: 'currency',
  currency: 'INR',
  maximumFractionDigits: 0,
});

function formatRupeeTick(value: string | number): string {
  const numericValue = typeof value === 'number' ? value : Number(value);
  return rupeeFormatter.format(Number.isFinite(numericValue) ? numericValue : 0);
}

function formatChartDateLabel(date: string): string {
  const parsed = new Date(`${date}T00:00:00`);
  if (Number.isNaN(parsed.getTime())) {
    return date;
  }

  return parsed.toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
}

function createCurrencyBarChartOptions(): ChartOptions<'bar'> {
  return {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: 'index',
      intersect: false,
    },
    plugins: {
      legend: {
        position: 'bottom',
        labels: {
          usePointStyle: true,
        },
      },
    },
    scales: {
      x: {
        offset: true,
        grid: {
          display: false,
        },
        ticks: {
          color: '#64748b',
          maxRotation: 0,
          autoSkip: true,
          maxTicksLimit: 7,
        },
      },
      y: {
        beginAtZero: true,
        grid: {
          color: 'rgba(148, 163, 184, 0.12)',
        },
        ticks: {
          color: '#64748b',
          callback: (value) => formatRupeeTick(value),
        },
      },
    },
  };
}

function mapTrendLabels<T extends { date: string }>(points: readonly T[]): string[] {
  return points.map((point) => formatChartDateLabel(point.date));
}

function buildSalesTrendData(
  points: readonly SalesTrendPointDto[],
  label: string,
): ChartData<'bar', number[], string> {
  return {
    labels: mapTrendLabels(points),
    datasets: [
      {
        label,
        data: points.map((point) => point.amount),
        backgroundColor: chartColors.revenueMuted,
        borderColor: chartColors.revenue,
        borderWidth: 1,
        borderRadius: 6,
        maxBarThickness: 28,
      },
    ],
  };
}

function buildRevenueVsExpensesData(
  points: readonly RevenueVsExpensesPointDto[],
  labels: { revenue: string; expenses: string },
): ChartData<'bar', number[], string> {
  return {
    labels: mapTrendLabels(points),
    datasets: [
      {
        label: labels.revenue,
        data: points.map((point) => point.revenue),
        backgroundColor: chartColors.revenueMuted,
        borderColor: chartColors.revenue,
        borderWidth: 1,
        borderRadius: 6,
        maxBarThickness: 22,
      },
      {
        label: labels.expenses,
        data: points.map((point) => point.expenses),
        backgroundColor: chartColors.expensesMuted,
        borderColor: chartColors.expenses,
        borderWidth: 1,
        borderRadius: 6,
        maxBarThickness: 22,
      },
    ],
  };
}

@Component({
  selector: 'app-dashboard-chart',
  standalone: true,
  imports: [CommonModule, CardModule, ChartModule, TranslocoPipe],
  styleUrl: './dashboard-chart.component.scss',
  template: `
    <section class="dashboard-charts" [attr.aria-label]="'dashboard.charts.ariaLabel' | transloco">
      <p-card class="dashboard-chart-card">
        <ng-template pTemplate="header">
          <div class="dashboard-chart-card__header">
            <p class="dashboard-chart-card__eyebrow">{{ 'dashboard.charts.trend' | transloco }}</p>
            <h2>{{ 'dashboard.charts.salesTrend' | transloco }}</h2>
            <p class="dashboard-chart-card__subtitle">{{ 'dashboard.charts.salesTrendSubtitle' | transloco }}</p>
          </div>
        </ng-template>

        <div class="dashboard-chart-card__canvas">
          <p-chart
            class="dashboard-chart-card__chart"
            type="bar"
            [data]="salesTrendChartData()"
            [options]="salesTrendChartOptions"
          />
        </div>
      </p-card>

      <p-card class="dashboard-chart-card">
        <ng-template pTemplate="header">
          <div class="dashboard-chart-card__header">
            <p class="dashboard-chart-card__eyebrow">{{ 'dashboard.charts.comparison' | transloco }}</p>
            <h2>{{ 'dashboard.charts.revenueVsExpenses' | transloco }}</h2>
            <p class="dashboard-chart-card__subtitle">{{ 'dashboard.charts.revenueVsExpensesSubtitle' | transloco }}</p>
          </div>
        </ng-template>

        <div class="dashboard-chart-card__canvas">
          <p-chart
            class="dashboard-chart-card__chart"
            type="bar"
            [data]="revenueVsExpensesChartData()"
            [options]="revenueVsExpensesChartOptions"
          />
        </div>
      </p-card>
    </section>
  `,
})
export class DashboardChartComponent {
  private readonly transloco = inject(TranslocoService);

  readonly dashboard = input<DashboardDto | null>(null);

  readonly salesTrendChartOptions = createCurrencyBarChartOptions();
  readonly revenueVsExpensesChartOptions = createCurrencyBarChartOptions();

  readonly salesTrendChartData = computed(() =>
    buildSalesTrendData(
      this.dashboard()?.salesTrendSeries ?? [],
      this.transloco.translate('dashboard.charts.grossSales'),
    ),
  );

  readonly revenueVsExpensesChartData = computed(() =>
    buildRevenueVsExpensesData(this.dashboard()?.revenueVsExpenses ?? [], {
      revenue: this.transloco.translate('dashboard.charts.revenue'),
      expenses: this.transloco.translate('dashboard.charts.expenses'),
    }),
  );
}
