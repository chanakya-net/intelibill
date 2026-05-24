import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { SelectModule } from 'primeng/select';
import { TranslocoPipe } from '@ngneat/transloco';
import { formatLocalIsoDate } from '../../../shared/utils/date-time.util';
import { DashboardPreset } from '../state/dashboard.actions';

const RANGE_STORAGE_KEY = 'intelibill_dashboard_range';

export interface SelectOption<T> {
  label: string;
  value: T;
}

export interface DashboardDateRangeChange {
  startDate: string;
  endDate: string;
  preset: DashboardPreset;
}

@Component({
  selector: 'app-dashboard-date-range',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonModule, SelectModule, TranslocoPipe],
  templateUrl: './dashboard-date-range.component.html',
  styleUrl: './dashboard-date-range.component.scss',
})
export class DashboardDateRangeComponent implements OnInit, OnChanges {
  @Input() preset: DashboardPreset = 'last30';
  @Input() startDate = '';
  @Input() endDate = '';
  @Input() presets: SelectOption<DashboardPreset>[] = [];
  @Input() maxRangeDays = 89;
  @Input() loading = false;

  @Output() rangeChange = new EventEmitter<DashboardDateRangeChange>();

  pendingPreset: DashboardPreset = 'last30';
  pendingStartDate = '';
  pendingEndDate = '';
  futureCorrected = false;

  private initialized = false;

  get isCustomRange(): boolean {
    return this.pendingPreset === 'custom';
  }

  get rangeValidationKey(): string | null {
    if (!this.pendingStartDate || !this.pendingEndDate) return null;
    if (this.pendingStartDate > this.pendingEndDate) return 'dashboard.validationStartAfterEnd';
    if (this.daysBetween(this.pendingStartDate, this.pendingEndDate) > this.maxRangeDays) return 'dashboard.validationRangeExceeds90';
    return null;
  }

  get isRangeValid(): boolean {
    return this.rangeValidationKey === null;
  }

  get isApplyDisabled(): boolean {
    return !this.isRangeValid || this.loading;
  }

  ngOnInit(): void {
    this.initializeRange();
  }

  ngOnChanges(): void {
    if (!this.initialized) {
      return;
    }

    this.pendingPreset = this.preset;
    this.pendingStartDate = this.startDate;
    this.pendingEndDate = this.endDate;
    this.futureCorrected = false;
  }

  onSelectPreset(preset: DashboardPreset): void {
    this.pendingPreset = preset;
    this.futureCorrected = false;

    if (preset !== 'custom') {
      const { start, end } = this.computePresetDates(preset);
      this.pendingStartDate = start;
      this.pendingEndDate = end;
      return;
    }

    if (!this.pendingStartDate) {
      this.pendingStartDate = this.todayIso();
    }

    if (!this.pendingEndDate) {
      this.pendingEndDate = this.todayIso();
    }
  }

  onEndDateChange(value: string): void {
    const today = this.todayIso();
    if (!value) {
      this.pendingEndDate = '';
      return;
    }

    if (value > today) {
      this.pendingEndDate = today;
      this.futureCorrected = true;
      return;
    }

    this.pendingEndDate = value;
    this.futureCorrected = false;
  }

  onApply(): void {
    if (!this.isRangeValid || !this.pendingStartDate || !this.pendingEndDate) {
      return;
    }

    this.saveRange(this.pendingStartDate, this.pendingEndDate, this.pendingPreset);
    this.rangeChange.emit({
      startDate: this.pendingStartDate,
      endDate: this.pendingEndDate,
      preset: this.pendingPreset,
    });
  }

  private initializeRange(): void {
    if (this.initialized) return;

    const persisted = this.loadRange();
    if (persisted) {
      this.pendingPreset = persisted.preset;
      this.pendingStartDate = persisted.startDate;
      this.pendingEndDate = persisted.endDate;
    } else {
      this.pendingPreset = this.preset;
      this.pendingStartDate = this.startDate || this.todayIso();
      this.pendingEndDate = this.endDate || this.todayIso();
    }

    this.initialized = true;
  }

  private saveRange(startDate: string, endDate: string, preset: DashboardPreset): void {
    try {
      localStorage.setItem(RANGE_STORAGE_KEY, JSON.stringify({ startDate, endDate, preset }));
    } catch {
      // ignore storage errors
    }
  }

  private loadRange(): DashboardDateRangeChange | null {
    try {
      const raw = localStorage.getItem(RANGE_STORAGE_KEY);
      if (!raw) return null;

      const parsed = JSON.parse(raw) as DashboardDateRangeChange;
      if (!parsed.startDate || !parsed.endDate) return null;
      if (parsed.startDate > parsed.endDate) return null;
      if (parsed.endDate > this.todayIso()) return null;
      if (this.daysBetween(parsed.startDate, parsed.endDate) > this.maxRangeDays) return null;

      return parsed;
    } catch {
      return null;
    }
  }

  private daysBetween(start: string, end: string): number {
    return (new Date(end).getTime() - new Date(start).getTime()) / 86_400_000;
  }

  private todayIso(): string {
    return formatLocalIsoDate(new Date());
  }

  private computePresetDates(preset: DashboardPreset): { start: string; end: string } {
    const today = new Date();
    const end = formatLocalIsoDate(today);

    switch (preset) {
      case 'today':
        return { start: end, end };
      case 'last7': {
        const start = new Date(today);
        start.setDate(start.getDate() - 6);
        return { start: formatLocalIsoDate(start), end };
      }
      case 'last30': {
        const start = new Date(today);
        start.setDate(start.getDate() - 29);
        return { start: formatLocalIsoDate(start), end };
      }
      case 'thisMonth': {
        const start = new Date(today.getFullYear(), today.getMonth(), 1);
        return { start: formatLocalIsoDate(start), end };
      }
      case 'lastMonth': {
        const last = new Date(today.getFullYear(), today.getMonth(), 0);
        const first = new Date(today.getFullYear(), today.getMonth() - 1, 1);
        return { start: formatLocalIsoDate(first), end: formatLocalIsoDate(last) };
      }
      default:
        return { start: '', end: '' };
    }
  }
}
