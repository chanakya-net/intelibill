import { CommonModule } from '@angular/common';
import { Component, computed, input } from '@angular/core';

import { ChartData, ChartOptions } from 'chart.js';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';

import {
  DashboardDto,
  RevenueVsExpensesPointDto,
  SalesTrendPointDto,
} from '../../../services/dashboard.models';

const chartColors = {
  sales: {
    border: '#2563eb',
    background: 'rgba(37, 99, 235, 0.16)',
  },
  netSales: {
    border: '#0f766e',
    background: 'rgba(15, 118, 110, 0.16)',
  },
  revenue: {
    border: '#16a34a',
    background: 'rgba(22, 163, 74, 0.16)',
  },
  expenses: {
    border: '#f97316',
    background: 'rgba(249, 115, 22, 0.16)',
  },
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

function createLineChartOptions(): ChartOptions<'line'> {
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
        grid: {
          color: 'rgba(148, 163, 184, 0.12)',
        },
        ticks: {
          color: '#64748b',
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
    elements: {
      line: {
        tension: 0.35,
      },
      point: {
        radius: 2,
        hoverRadius: 4,
      },
    },
  };
}

function mapTrendLabels<T extends { date: string }>(points: readonly T[]): string[] {
  return points.map((point) => point.date);
}

function buildSalesTrendData(points: readonly SalesTrendPointDto[]): ChartData<'line', number[], string> {
  return {
    labels: mapTrendLabels(points),
    datasets: [
      {
        label: 'Gross Sales',
        data: points.map((point) => point.amount),
        borderColor: chartColors.sales.border,
        backgroundColor: chartColors.sales.background,
        fill: false,
      },
      {
        label: 'Net Sales',
        data: points.map((point) => point.netAmount),
        borderColor: chartColors.netSales.border,
        backgroundColor: chartColors.netSales.background,
        fill: false,
      },
    ],
  };
}

function buildRevenueVsExpensesData(
  points: readonly RevenueVsExpensesPointDto[],
): ChartData<'line', number[], string> {
  return {
    labels: mapTrendLabels(points),
    datasets: [
      {
        label: 'Revenue',
        data: points.map((point) => point.revenue),
        borderColor: chartColors.revenue.border,
        backgroundColor: chartColors.revenue.background,
        fill: false,
      },
      {
        label: 'Expenses',
        data: points.map((point) => point.expenses),
        borderColor: chartColors.expenses.border,
        backgroundColor: chartColors.expenses.background,
        fill: false,
      },
    ],
  };
}

@Component({
  selector: 'app-dashboard-chart',
  standalone: true,
  imports: [CommonModule, CardModule, ChartModule],
  styleUrl: './dashboard-chart.component.scss',
  template: `
    <section class="dashboard-charts" aria-label="Dashboard charts">
      <p-card class="dashboard-chart-card">
        <ng-template pTemplate="header">
          <div class="dashboard-chart-card__header">
            <p class="dashboard-chart-card__eyebrow">Trend</p>
            <h2>Sales Trend</h2>
          </div>
        </ng-template>

        <div class="dashboard-chart-card__canvas">
          <p-chart
            class="dashboard-chart-card__chart"
            type="line"
            [data]="salesTrendChartData()"
            [options]="salesTrendChartOptions"
          />
        </div>
      </p-card>

      <p-card class="dashboard-chart-card">
        <ng-template pTemplate="header">
          <div class="dashboard-chart-card__header">
            <p class="dashboard-chart-card__eyebrow">Comparison</p>
            <h2>Revenue vs Expenses</h2>
          </div>
        </ng-template>

        <div class="dashboard-chart-card__canvas">
          <p-chart
            class="dashboard-chart-card__chart"
            type="line"
            [data]="revenueVsExpensesChartData()"
            [options]="revenueVsExpensesChartOptions"
          />
        </div>
      </p-card>
    </section>
  `,
})
export class DashboardChartComponent {
  readonly dashboard = input<DashboardDto | null>(null);

  readonly salesTrendChartOptions = createLineChartOptions();
  readonly revenueVsExpensesChartOptions = createLineChartOptions();

  readonly salesTrendChartData = computed(() =>
    buildSalesTrendData(this.dashboard()?.salesTrendSeries ?? []),
  );

  readonly revenueVsExpensesChartData = computed(() =>
    buildRevenueVsExpensesData(this.dashboard()?.revenueVsExpenses ?? []),
  );
}
