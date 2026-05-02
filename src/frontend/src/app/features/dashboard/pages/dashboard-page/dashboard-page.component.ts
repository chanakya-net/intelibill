import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit, Signal, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { SelectModule } from 'primeng/select';

import {
  DashboardDto,
  PaymentMixTrendPointDto,
  PreviousPeriodSummaryDto,
  ProfitTrendPointDto,
  SalesTrendPointDto,
} from '../../services/dashboard.service';
import { DashboardPreset } from '../../state/dashboard.actions';
import { DashboardFacade } from '../../state/dashboard.facade';

const PRESETS: { label: string; value: DashboardPreset }[] = [
  { label: 'dashboard.presetToday', value: 'today' },
  { label: 'dashboard.presetLast7', value: 'last7' },
  { label: 'dashboard.presetLast30', value: 'last30' },
  { label: 'dashboard.presetThisMonth', value: 'thisMonth' },
  { label: 'dashboard.presetLastMonth', value: 'lastMonth' },
  { label: 'dashboard.presetCustom', value: 'custom' },
];

const MAX_RANGE_DAYS = 89;
const RANGE_STORAGE_KEY = 'intelibill_dashboard_range';

interface PersistedRange {
  startDate: string;
  endDate: string;
  preset: DashboardPreset;
}

type DashboardMetric = 'sales' | 'profit' | 'expense' | 'paymentMix';
type DashboardChartType = 'bar' | 'stackedBar' | 'line' | 'pie' | 'doughnut';

interface SelectOption<T> {
  label: string;
  value: T;
}

interface ChartDataset {
  label?: string;
  data: number[];
  fill?: boolean;
  tension?: number;
  backgroundColor?: string | string[];
  borderColor?: string | string[];
  borderWidth?: number;
}

interface ChartData {
  labels: string[];
  datasets: ChartDataset[];
}

function saveRange(startDate: string, endDate: string, preset: DashboardPreset): void {
  try {
    localStorage.setItem(RANGE_STORAGE_KEY, JSON.stringify({ startDate, endDate, preset }));
  } catch {
    // ignore storage errors
  }
}

function loadRange(): PersistedRange | null {
  try {
    const raw = localStorage.getItem(RANGE_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PersistedRange;
    const today = todayIso();
    // Validate: dates must be valid, start <= end, end <= today, range <= 89 days
    if (!parsed.startDate || !parsed.endDate) return null;
    if (parsed.startDate > parsed.endDate) return null;
    if (parsed.endDate > today) return null;
    if (daysBetween(parsed.startDate, parsed.endDate) > MAX_RANGE_DAYS) return null;
    return parsed;
  } catch {
    return null;
  }
}

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function daysBetween(start: string, end: string): number {
  return (new Date(end).getTime() - new Date(start).getTime()) / 86_400_000;
}

function computePresetDates(preset: DashboardPreset): { start: string; end: string } {
  const today = new Date();
  const toIso = (d: Date) => d.toISOString().slice(0, 10);
  const end = toIso(today);

  switch (preset) {
    case 'today':
      return { start: end, end };
    case 'last7': {
      const s = new Date(today);
      s.setDate(s.getDate() - 6);
      return { start: toIso(s), end };
    }
    case 'last30': {
      const s = new Date(today);
      s.setDate(s.getDate() - 29);
      return { start: toIso(s), end };
    }
    case 'thisMonth': {
      const s = new Date(today.getFullYear(), today.getMonth(), 1);
      return { start: toIso(s), end };
    }
    case 'lastMonth': {
      const last = new Date(today.getFullYear(), today.getMonth(), 0);
      const first = new Date(today.getFullYear(), today.getMonth() - 1, 1);
      return { start: toIso(first), end: toIso(last) };
    }
    default:
      return { start: '', end: '' };
  }
}

function formatTrendDateLabel(value: string): string {
  const [year, month, day] = value.split('-').map(Number);
  if (!year || !month || !day) return value;

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
  }).format(new Date(year, month - 1, day));
}

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  imports: [
    CommonModule,
    DatePipe,
    FormsModule,
    ButtonModule,
    CardModule,
    ChartModule,
    ProgressSpinnerModule,
    SelectModule,
    TranslocoPipe,
  ],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss',
})
export class DashboardPageComponent implements OnInit {
  private readonly facade = inject(DashboardFacade);
  private readonly transloco = inject(TranslocoService);

  readonly data: Signal<DashboardDto | null | undefined> = toSignal(this.facade.data$);
  readonly loading = toSignal(this.facade.loading$, { initialValue: false });
  readonly error = toSignal(this.facade.error$, { initialValue: '' });

  readonly presets = PRESETS;
  readonly selectedMetric = signal<DashboardMetric>('sales');
  readonly selectedChartType = signal<DashboardChartType>('bar');

  readonly presetOptions = computed(() =>
    PRESETS.map((preset) => ({
      label: this.transloco.translate(preset.label),
      value: preset.value,
    })),
  );

  readonly metricOptions = computed<SelectOption<DashboardMetric>[]>(() => [
    { label: this.transloco.translate('dashboard.salesTrendTitle'), value: 'sales' },
    { label: this.transloco.translate('dashboard.sectionExpenses'), value: 'expense' },
    { label: this.transloco.translate('dashboard.paymentMixTitle'), value: 'paymentMix' },
  ]);

  readonly chartTypeOptions: SelectOption<DashboardChartType>[] = [
    { label: 'Column Bar', value: 'bar' },
    { label: 'Column Bar Stacked', value: 'stackedBar' },
    { label: 'Line', value: 'line' },
    { label: 'Pie', value: 'pie' },
    { label: 'Donut', value: 'doughnut' },
  ];

  readonly selectedPreset = toSignal(this.facade.selectedPreset$, { initialValue: 'last30' as DashboardPreset });
  readonly activeStartDate = toSignal(this.facade.startDate$, { initialValue: '' });
  readonly activeEndDate = toSignal(this.facade.endDate$, { initialValue: '' });

  readonly pendingPreset = signal<DashboardPreset>('last30');
  readonly pendingStartDate = signal<string>('');
  readonly pendingEndDate = signal<string>(todayIso());
  readonly futureCorrected = signal(false);

  readonly isCustom = computed(() => this.pendingPreset() === 'custom');

  readonly rangeValidationKey = computed<string | null>(() => {
    const start = this.pendingStartDate();
    const end = this.pendingEndDate();
    if (!start || !end) return null;
    if (start > end) return 'dashboard.validationStartAfterEnd';
    if (daysBetween(start, end) > MAX_RANGE_DAYS) return 'dashboard.validationRangeExceeds90';
    return null;
  });

  readonly isRangeValid = computed(() => this.rangeValidationKey() === null);

  readonly applyDisabled = computed(() => !this.isRangeValid() || this.loading());

  readonly isLoadingWithData = computed(() => this.loading() && !!this.data());

  readonly salesChartData = computed<ChartData | null>(() => {
    const salesTrend = this.data()?.salesTrendSeries;
    const profitTrend = this.data()?.profitTrendSeries;
    if (!salesTrend || salesTrend.length === 0 || !profitTrend || profitTrend.length === 0) return null;

    return {
      labels: salesTrend.map((point: SalesTrendPointDto) => formatTrendDateLabel(point.date)),
      datasets: [
        {
          label: this.transloco.translate('dashboard.salesBooked'),
          data: salesTrend.map((point: SalesTrendPointDto) => point.amount),
          backgroundColor: '#0f766e',
          borderColor: '#0f766e',
          borderWidth: 1,
          tension: 0.25,
        },
        {
          label: this.transloco.translate('dashboard.profitBeforeTax'),
          data: profitTrend.map((point: ProfitTrendPointDto) => point.profitBeforeTax),
          backgroundColor: '#ca8a04',
          borderColor: '#ca8a04',
          borderWidth: 1,
          tension: 0.25,
        },
        {
          label: this.transloco.translate('dashboard.profitAfterTax'),
          data: profitTrend.map((point: ProfitTrendPointDto) => point.profitAfterTax),
          backgroundColor: '#7c3aed',
          borderColor: '#7c3aed',
          borderWidth: 1,
          tension: 0.25,
        },
      ],
    };
  });

  readonly profitChartData = computed<ChartData | null>(() => {
    const trend = this.data()?.profitTrendSeries;
    if (!trend || trend.length === 0) return null;
    return {
      labels: trend.map((p: ProfitTrendPointDto) => p.date),
      datasets: [
        {
          label: this.transloco.translate('dashboard.profitAfterTax'),
          data: trend.map((p: ProfitTrendPointDto) => p.profitAfterTax),
          fill: false,
          tension: 0.3,
        },
      ],
    };
  });

  readonly paymentMixDonutData = computed<ChartData | null>(() => {
    const mix = this.data()?.paymentMix;
    if (!mix) return null;
    const total = mix.cash + mix.upi + mix.card + mix.credit;
    if (total === 0) return null;
    return {
      labels: [
        this.transloco.translate('dashboard.paymentMixCash'),
        this.transloco.translate('dashboard.paymentMixUpi'),
        this.transloco.translate('dashboard.paymentMixCard'),
        this.transloco.translate('dashboard.paymentMixCredit'),
      ],
      datasets: [
        {
          data: [mix.cash, mix.upi, mix.card, mix.credit],
        },
      ],
    };
  });

  readonly paymentMixTrendChartData = computed<ChartData | null>(() => {
    const trend = this.data()?.paymentMixTrendSeries;
    if (!trend || trend.length === 0) return null;

    const hasAnyValue = trend.some((point: PaymentMixTrendPointDto) => point.cash + point.upi + point.card + point.credit > 0);
    if (!hasAnyValue) return null;

    return {
      labels: trend.map((point: PaymentMixTrendPointDto) => formatTrendDateLabel(point.date)),
      datasets: [
        {
          label: this.transloco.translate('dashboard.paymentMixCash'),
          data: trend.map((point: PaymentMixTrendPointDto) => point.cash),
          backgroundColor: '#3f9ad6',
          borderColor: '#3f9ad6',
          borderWidth: 1,
        },
        {
          label: this.transloco.translate('dashboard.paymentMixUpi'),
          data: trend.map((point: PaymentMixTrendPointDto) => point.upi),
          backgroundColor: '#f35b7a',
          borderColor: '#f35b7a',
          borderWidth: 1,
        },
        {
          label: this.transloco.translate('dashboard.paymentMixCard'),
          data: trend.map((point: PaymentMixTrendPointDto) => point.card),
          backgroundColor: '#f59e42',
          borderColor: '#f59e42',
          borderWidth: 1,
        },
        {
          label: this.transloco.translate('dashboard.paymentMixCredit'),
          data: trend.map((point: PaymentMixTrendPointDto) => point.credit),
          backgroundColor: '#f0c451',
          borderColor: '#f0c451',
          borderWidth: 1,
        },
      ],
    };
  });

  readonly lineChartOptions = {
    responsive: true,
    plugins: { legend: { display: false } },
    scales: {
      y: { beginAtZero: true },
    },
  };

  readonly donutChartOptions = {
    responsive: true,
    plugins: { legend: { position: 'bottom' as const } },
  };

  readonly selectedChartTitle = computed(() => {
    const match = this.metricOptions().find((o) => o.value === this.activeMetric());
    return match?.label ?? this.transloco.translate('dashboard.salesTrendTitle');
  });

  private metricHasData(metric: DashboardMetric): boolean {
    const dashboard = this.data();
    if (!dashboard) return false;

    if (metric === 'sales') {
      return !!dashboard.salesTrendSeries && dashboard.salesTrendSeries.length > 0;
    }

    if (metric === 'profit') {
      return !!dashboard.profitTrendSeries && dashboard.profitTrendSeries.length > 0;
    }

    if (metric === 'paymentMix') {
      const paymentMixTrend = dashboard.paymentMixTrendSeries;
      if (paymentMixTrend && paymentMixTrend.length > 0) {
        return paymentMixTrend.some((point) => point.cash + point.upi + point.card + point.credit > 0);
      }

      const mix = dashboard.paymentMix;
      if (!mix) return false;
      return mix.cash + mix.upi + mix.card + mix.credit > 0;
    }

    return dashboard.expenseRecorded !== null && dashboard.expenseCorrection !== null && dashboard.netExpense !== null;
  }

  readonly activeMetric = computed<DashboardMetric>(() => {
    const selected = this.selectedMetric();
    if (this.metricHasData(selected)) return selected;

    const fallbackOrder: DashboardMetric[] = ['sales', 'profit', 'expense', 'paymentMix'];
    for (const metric of fallbackOrder) {
      if (this.metricHasData(metric)) return metric;
    }

    return selected;
  });

  readonly selectedChartData = computed<ChartData | null>(() => {
    const metric = this.activeMetric();
    if (metric === 'sales') return this.salesChartData();
    if (metric === 'profit') return this.profitChartData();
    if (metric === 'paymentMix') {
      const chartType = this.selectedChartType();
      if (chartType === 'pie' || chartType === 'doughnut') {
        return this.paymentMixDonutData();
      }

      return this.paymentMixTrendChartData() ?? this.paymentMixDonutData();
    }

    const dashboard = this.data();
    if (!dashboard || dashboard.expenseRecorded === null || dashboard.expenseCorrection === null || dashboard.netExpense === null) {
      return null;
    }

    return {
      labels: [
        this.transloco.translate('dashboard.expenseRecorded'),
        this.transloco.translate('dashboard.expenseCorrection'),
        this.transloco.translate('dashboard.netExpense'),
      ],
      datasets: [
        {
          label: this.transloco.translate('dashboard.sectionExpenses'),
          data: [dashboard.expenseRecorded, dashboard.expenseCorrection, dashboard.netExpense],
        },
      ],
    };
  });

  readonly selectedPrimeChartType = computed<'bar' | 'line' | 'pie' | 'doughnut'>(() => {
    const type = this.selectedChartType();
    if (this.activeMetric() === 'sales' && (type === 'pie' || type === 'doughnut')) return 'bar';
    if (type === 'line') return 'line';
    if (type === 'pie') return 'pie';
    if (type === 'doughnut') return 'doughnut';
    return 'bar';
  });

  readonly selectedChartOptions = computed(() => {
    const showLegend = (this.selectedChartData()?.datasets.length ?? 0) > 1;
    const selectedType = this.selectedChartType();

    const currencyFormatter = new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      maximumFractionDigits: 0,
    });

    const commonOptions: any = {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: {
          display: showLegend,
          position: 'bottom' as const,
        },
        tooltip: {
          callbacks: {
            label: (context: any) => {
              let label = context.dataset.label || '';
              if (label) {
                label += ': ';
              }
              if (context.parsed.y !== undefined) {
                label += currencyFormatter.format(context.parsed.y);
              } else if (context.parsed !== undefined) {
                label += currencyFormatter.format(context.parsed);
              }
              return label;
            },
          },
        },
      },
    };

    if ((selectedType === 'pie' || selectedType === 'doughnut') && this.activeMetric() !== 'sales') {
      return commonOptions;
    }

    if (selectedType === 'stackedBar') {
      return {
        ...commonOptions,
        scales: {
          x: { stacked: true },
          y: { beginAtZero: true, stacked: true },
        },
      };
    }

    return {
      ...commonOptions,
      scales: {
        y: { beginAtZero: true },
      },
    };
  });

  readonly sectionExpanded = signal({
    expenses: false,
    paymentBehavior: false,
    stockRisk: false,
    receivables: false,
  });

  toggleSection(section: 'expenses' | 'paymentBehavior' | 'stockRisk' | 'receivables'): void {
    this.sectionExpanded.update((s) => ({ ...s, [section]: !s[section] }));
  }

  readonly previousPeriodComparisons = computed(() => {
    const d = this.data();
    const prev = d?.previousPeriodSummary;
    if (!prev || !d) return null;
    const cmp = (curr: number, p: number) => {
      const delta = curr - p;
      return { direction: (delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat') as 'up' | 'down' | 'flat', delta };
    };
    return {
      salesCount: cmp(d.salesCount, prev.salesCount),
      salesBooked: d.salesBooked !== null ? cmp(d.salesBooked, prev.salesBooked) : null,
      profitAfterTax: d.profitAfterTax !== null ? cmp(d.profitAfterTax, prev.profitAfterTax) : null,
      netExpense: d.netExpense !== null ? cmp(d.netExpense, prev.netExpense) : null,
      creditSalesPercentage: d.creditSalesPercentage !== null ? cmp(d.creditSalesPercentage, prev.creditSalesPercentage) : null,
    };
  });

  directionIcon(direction: 'up' | 'down' | 'flat'): string {
    return direction === 'up' ? '↑' : direction === 'down' ? '↓' : '→';
  }

  ngOnInit(): void {
    const persisted = loadRange();
    const { start, end } = persisted
      ? { start: persisted.startDate, end: persisted.endDate }
      : computePresetDates('last30');
    const preset = persisted?.preset ?? 'last30';
    this.pendingPreset.set(preset);
    this.pendingStartDate.set(start);
    this.pendingEndDate.set(end);
    this.facade.loadDashboard();
  }

  onSelectPreset(preset: DashboardPreset): void {
    this.pendingPreset.set(preset);
    this.futureCorrected.set(false);
    if (preset !== 'custom') {
      const { start, end } = computePresetDates(preset);
      this.pendingStartDate.set(start);
      this.pendingEndDate.set(end);
    }
  }

  onEndDateChange(value: string): void {
    const today = todayIso();
    if (value > today) {
      this.pendingEndDate.set(today);
      this.futureCorrected.set(true);
    } else {
      this.pendingEndDate.set(value);
      this.futureCorrected.set(false);
    }
  }

  onApply(): void {
    if (!this.isRangeValid()) return;
    const preset = this.pendingPreset();
    const startDate = this.pendingStartDate();
    const endDate = this.pendingEndDate();
    if (!startDate || !endDate) return;
    saveRange(startDate, endDate, preset);
    this.facade.applyRange(startDate, endDate, preset);
  }

  onRefresh(): void {
    this.facade.refresh();
  }
}
