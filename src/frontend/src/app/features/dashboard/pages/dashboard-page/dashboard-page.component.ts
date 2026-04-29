import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit, Signal, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { DashboardDto, PreviousPeriodSummaryDto, ProfitTrendPointDto, SalesTrendPointDto } from '../../services/dashboard.service';
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
    TranslocoPipe,
  ],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss',
})
export class DashboardPageComponent implements OnInit {
  private readonly facade = inject(DashboardFacade);

  readonly data: Signal<DashboardDto | null | undefined> = toSignal(this.facade.data$);
  readonly loading = toSignal(this.facade.loading$, { initialValue: false });
  readonly error = toSignal(this.facade.error$, { initialValue: '' });

  readonly presets = PRESETS;

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

  readonly salesChartData = computed(() => {
    const trend = this.data()?.salesTrendSeries;
    if (!trend || trend.length === 0) return null;
    return {
      labels: trend.map((p: SalesTrendPointDto) => p.date),
      datasets: [
        {
          label: 'Sales Booked',
          data: trend.map((p: SalesTrendPointDto) => p.amount),
          fill: false,
          tension: 0.3,
        },
      ],
    };
  });

  readonly profitChartData = computed(() => {
    const trend = this.data()?.profitTrendSeries;
    if (!trend || trend.length === 0) return null;
    return {
      labels: trend.map((p: ProfitTrendPointDto) => p.date),
      datasets: [
        {
          label: 'Profit After Tax',
          data: trend.map((p: ProfitTrendPointDto) => p.profitAfterTax),
          fill: false,
          tension: 0.3,
        },
      ],
    };
  });

  readonly paymentMixDonutData = computed(() => {
    const mix = this.data()?.paymentMix;
    if (!mix) return null;
    const total = mix.cash + mix.upi + mix.card + mix.credit;
    if (total === 0) return null;
    return {
      labels: ['Cash', 'UPI', 'Card', 'Credit'],
      datasets: [
        {
          data: [mix.cash, mix.upi, mix.card, mix.credit],
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
