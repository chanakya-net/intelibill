import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit, Signal, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ChartModule } from 'primeng/chart';
import { ProgressSpinnerModule } from 'primeng/progressspinner';

import { DashboardDto, SalesTrendPointDto } from '../../services/dashboard.service';
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

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
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

  readonly isCustom = computed(() => this.pendingPreset() === 'custom');

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

  readonly salesChartOptions = {
    responsive: true,
    plugins: { legend: { display: false } },
    scales: {
      y: { beginAtZero: true },
    },
  };

  ngOnInit(): void {
    const { start, end } = computePresetDates('last30');
    this.pendingPreset.set('last30');
    this.pendingStartDate.set(start);
    this.pendingEndDate.set(end);
    this.facade.loadDashboard();
  }

  onSelectPreset(preset: DashboardPreset): void {
    this.pendingPreset.set(preset);
    if (preset !== 'custom') {
      const { start, end } = computePresetDates(preset);
      this.pendingStartDate.set(start);
      this.pendingEndDate.set(end);
    }
  }

  onApply(): void {
    const preset = this.pendingPreset();
    const startDate = this.pendingStartDate();
    const endDate = this.pendingEndDate();
    if (!startDate || !endDate) return;
    this.facade.applyRange(startDate, endDate, preset);
  }

  onRefresh(): void {
    this.facade.refresh();
  }
}
