import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit, Signal, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';
import { 
  ChartData,
  DashboardChartType,
  buildPaymentMixDonutChartData,
  buildPaymentMixTrendChartData,
  buildProfitTrendChartData,
  buildSalesTrendChartData,
} from '../../utils/dashboard-chart-builders';
import { DashboardDto, PaymentMixDto, PaymentMixTrendPointDto } from '../../models/dashboard-dto';
import { DashboardPreset } from '../../state/dashboard.actions';
import { DashboardFacade } from '../../state/dashboard.facade';
import { DashboardDateRangeChange, DashboardDateRangeComponent, SelectOption } from '../../components/dashboard-date-range.component';
import { DashboardMetricCardComponent, DashboardMetricCardDelta } from '../../components/dashboard-metric-card.component';
import { DashboardChartComponent } from '../../components/dashboard-chart.component';

type DashboardMetric = 'sales' | 'profit' | 'expense' | 'paymentMix';
type Comparison = { direction: 'up' | 'down' | 'flat'; delta: number };
interface ComparisonSummary {
  salesBooked: Comparison | null;
  netSalesBooked: Comparison | null;
  profitAfterTax: Comparison | null;
  netExpense: Comparison | null;
  creditSalesPercentage: Comparison | null;
}

const PRESETS: { labelKey: string; value: DashboardPreset }[] = [
  { labelKey: 'dashboard.presetToday', value: 'today' },
  { labelKey: 'dashboard.presetLast7', value: 'last7' },
  { labelKey: 'dashboard.presetLast30', value: 'last30' },
  { labelKey: 'dashboard.presetThisMonth', value: 'thisMonth' },
  { labelKey: 'dashboard.presetLastMonth', value: 'lastMonth' },
  { labelKey: 'dashboard.presetCustom', value: 'custom' },
];

const SALES_CHART_LABELS: SalesChartLabels = {
  salesBooked: 'Sales Booked',
  netSalesBooked: 'Net Sales',
  profitBeforeTax: 'Profit Before Tax',
  profitAfterTax: 'Profit After Tax',
};

const PAYMENT_MIX_CHART_LABELS: PaymentMixChartLabels = {
  cash: 'Cash',
  upi: 'UPI',
  card: 'Card',
  credit: 'Credit',
};

const CHART_TITLES: Record<DashboardMetric, string> = {
  sales: 'Sales Trend',
  expense: 'Expenses',
  paymentMix: 'Payment Behavior',
  profit: 'Profit',
};

const EXPENSE_CHART_LABELS = {
  expenseRecorded: 'Expense Recorded',
  expenseCorrection: 'Expense Correction',
  netExpense: 'Net Expense',
};

type SalesChartLabels = {
  salesBooked: string;
  netSalesBooked: string;
  profitBeforeTax: string;
  profitAfterTax: string;
};

type PaymentMixChartLabels = {
  cash: string;
  upi: string;
  card: string;
  credit: string;
};

type MetricOption = {
  labelKey: string;
  value: DashboardMetric;
};

const METRIC_OPTIONS: ReadonlyArray<MetricOption> = [
  { labelKey: 'dashboard.salesTrendTitle', value: 'sales' },
  { labelKey: 'dashboard.profitAfterTax', value: 'profit' },
  { labelKey: 'dashboard.sectionExpenses', value: 'expense' },
  { labelKey: 'dashboard.paymentMixTitle', value: 'paymentMix' },
];

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [ButtonModule, CardModule, CommonModule, DatePipe, DashboardChartComponent, DashboardDateRangeComponent, DashboardMetricCardComponent, FormsModule, ProgressSpinnerModule, SelectModule, TranslocoPipe],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss',
})
export class DashboardPageComponent implements OnInit {
  private readonly facade = inject(DashboardFacade);

  readonly data: Signal<DashboardDto | null | undefined> = toSignal(this.facade.data$);
  readonly loading = toSignal(this.facade.loading$, { initialValue: false });
  readonly error = toSignal(this.facade.error$, { initialValue: '' });
  readonly selectedPreset = toSignal(this.facade.selectedPreset$, { initialValue: 'last30' as DashboardPreset });
  readonly activeStartDate = toSignal(this.facade.startDate$, { initialValue: '' });
  readonly activeEndDate = toSignal(this.facade.endDate$, { initialValue: '' });
  readonly maxRangeDays = 89;

  readonly selectedMetric = signal<DashboardMetric>('sales');
  readonly selectedChartType = signal<DashboardChartType>('bar');
  readonly sectionExpanded = signal({ sales: false, expenses: false, paymentBehavior: false, stockRisk: false, receivables: false });

  readonly presetOptions = computed<SelectOption<DashboardPreset>[]>(() => PRESETS.map((preset) => ({ label: preset.labelKey, value: preset.value })));
  readonly metricOptions = computed<SelectOption<DashboardMetric>[]>(() => METRIC_OPTIONS.map((metric) => ({ label: metric.labelKey, value: metric.value })));

  readonly isLoadingWithData = computed(() => this.loading() && !!this.data());
  readonly salesChartData = computed<ChartData | null>(() => buildSalesTrendChartData(
    this.data()?.salesTrendSeries,
    this.data()?.profitTrendSeries,
    SALES_CHART_LABELS,
  ));

  readonly profitChartData = computed<ChartData | null>(() => buildProfitTrendChartData(this.data()?.profitTrendSeries, 'Profit After Tax'));

  readonly paymentMixTrendChartData = computed<ChartData | null>(() => buildPaymentMixTrendChartData(this.data()?.paymentMixTrendSeries, PAYMENT_MIX_CHART_LABELS));

  readonly paymentMixDonutData = computed<ChartData | null>(() => buildPaymentMixDonutChartData(this.data()?.paymentMix, PAYMENT_MIX_CHART_LABELS));

  readonly activeMetric = computed<DashboardMetric>(() => {
    const selected = this.selectedMetric();
    if (this.metricHasData(selected)) return selected;
    return (['sales', 'profit', 'expense', 'paymentMix'].find((metric) => this.metricHasData(metric as DashboardMetric)) as DashboardMetric | null) ?? selected;
  });

  readonly selectedChartData = computed<ChartData | null>(() => {
    const metric = this.activeMetric();
    if (metric === 'sales') return this.salesChartData();
    if (metric === 'profit') return this.profitChartData();
    if (metric === 'paymentMix') return this.selectedChartType() === 'pie' || this.selectedChartType() === 'doughnut' ? this.paymentMixDonutData() : this.paymentMixTrendChartData() ?? this.paymentMixDonutData();

    const dashboard = this.data();
    if (!dashboard || dashboard.expenseRecorded === null || dashboard.expenseCorrection === null || dashboard.netExpense === null) return null;

    return {
      labels: [
        EXPENSE_CHART_LABELS.expenseRecorded,
        EXPENSE_CHART_LABELS.expenseCorrection,
        EXPENSE_CHART_LABELS.netExpense,
      ],
      datasets: [{ label: 'Expenses', data: [dashboard.expenseRecorded, dashboard.expenseCorrection, dashboard.netExpense] }],
    };
  });

  readonly selectedPrimeChartType = computed<'bar' | 'line' | 'pie' | 'doughnut'>(() => {
    const nextType = this.selectedChartType();
    if (this.activeMetric() === 'sales' && (nextType === 'pie' || nextType === 'doughnut')) return 'bar';
    if (nextType === 'stackedBar') return 'bar';
    return nextType;
  });

  readonly selectedChartOptions = computed(() => {
    const showLegend = (this.selectedChartData()?.datasets.length ?? 0) > 1;
    const selectedType = this.selectedChartType();
    const currencyFormatter = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 });
    const commonOptions = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: showLegend, position: 'bottom' as const },
        tooltip: {
          callbacks: {
            label: (context: { dataset: { label?: string }; parsed?: { y?: number }; parsedValue?: number }) => {
              const value = context.parsed?.y ?? context.parsedValue ?? 0;
              const prefix = context.dataset.label ? `${context.dataset.label}: ` : '';
              return `${prefix}${currencyFormatter.format(value)}`;
            },
          },
        },
      },
    };
    return (selectedType === 'stackedBar')
      ? { ...commonOptions, scales: { x: { stacked: true }, y: { beginAtZero: true, stacked: true } } }
      : ((selectedType === 'pie' || selectedType === 'doughnut') && this.activeMetric() !== 'sales')
        ? commonOptions
        : { ...commonOptions, scales: { y: { beginAtZero: true } } };
  });

  readonly selectedChartTitle = computed(() => {
    return CHART_TITLES[this.activeMetric()];
  });

  readonly previousPeriodComparisons = computed<ComparisonSummary | null>(() => {
    const dashboard = this.data();
    const previous = dashboard?.previousPeriodSummary;
    if (!dashboard || !previous) return null;
    const cmp = (current: number, prior: number): Comparison => {
      const delta = current - prior;
      return { direction: delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat', delta };
    };

    return {
      salesBooked: dashboard.salesBooked === null ? null : cmp(dashboard.salesBooked, previous.salesBooked),
      netSalesBooked: dashboard.netSalesBooked === null ? null : cmp(dashboard.netSalesBooked, previous.netSalesBooked),
      profitAfterTax: dashboard.profitAfterTax === null ? null : cmp(dashboard.profitAfterTax, previous.profitAfterTax),
      netExpense: dashboard.netExpense === null ? null : cmp(dashboard.netExpense, previous.netExpense),
      creditSalesPercentage: dashboard.creditSalesPercentage === null ? null : cmp(dashboard.creditSalesPercentage, previous.creditSalesPercentage),
    };
  });

  ngOnInit(): void { this.facade.loadDashboard(); }

  metricHasData(metric: DashboardMetric): boolean {
    const dashboard = this.data();
    if (!dashboard) return false;
    if (metric === 'sales') return !!dashboard.salesTrendSeries?.length && !!dashboard.profitTrendSeries?.length;
    if (metric === 'profit') return !!dashboard.profitTrendSeries?.length;
    if (metric === 'paymentMix') {
      if (dashboard.paymentMixTrendSeries?.length) {
        return dashboard.paymentMixTrendSeries.some((point: PaymentMixTrendPointDto) => point.cash + point.upi + point.card + point.credit > 0);
      }
      return this.paymentMixTotal(dashboard.paymentMix) > 0;
    }

    return dashboard.expenseRecorded !== null && dashboard.expenseCorrection !== null && dashboard.netExpense !== null;
  }

  metricCardDeltaFromComparison(comparison: Comparison | null | undefined, isPercent = false): DashboardMetricCardDelta | null {
    if (!comparison) return null;
    return { direction: comparison.direction, value: comparison.delta, percent: isPercent, currency: !isPercent };
  }

  onRangeChange(range: DashboardDateRangeChange): void { this.facade.applyRange(range.startDate, range.endDate, range.preset); }
  onChartTypeChange(chartType: DashboardChartType): void {
    if (this.activeMetric() === 'sales' && (chartType === 'pie' || chartType === 'doughnut')) {
      this.selectedChartType.set('bar');
      return;
    }
    this.selectedChartType.set(chartType);
  }

  onMetricChange(metric: DashboardMetric): void { this.selectedMetric.set(metric); }
  onRefresh(): void { this.facade.refresh(); }
  toggleSection(section: 'sales' | 'expenses' | 'paymentBehavior' | 'stockRisk' | 'receivables'): void {
    this.sectionExpanded.update((state) => ({ ...state, [section]: !state[section] }));
  }

  private paymentMixTotal(paymentMix: PaymentMixDto | null | undefined): number {
    return paymentMix ? paymentMix.cash + paymentMix.upi + paymentMix.card + paymentMix.credit : 0;
  }
}
