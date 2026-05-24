import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { CardModule } from 'primeng/card';
import { TranslocoPipe } from '@ngneat/transloco';

export interface DashboardMetricCardDelta {
  direction: 'up' | 'down' | 'flat';
  value: number;
  currency?: boolean;
  percent?: boolean;
}

@Component({
  selector: 'app-dashboard-metric-card',
  standalone: true,
  imports: [CommonModule, CardModule, TranslocoPipe],
  templateUrl: './dashboard-metric-card.component.html',
  styleUrl: './dashboard-metric-card.component.scss',
})
export class DashboardMetricCardComponent {
  @Input() label = '';
  @Input() value: number | string | null | undefined = null;
  @Input() loading = false;
  @Input() delta: DashboardMetricCardDelta | null = null;
  @Input() currency = false;

  get valueText(): string {
    return this.formatValue(this.value, this.currency);
  }

  get hasDelta(): boolean {
    return this.delta !== null;
  }

  get deltaText(): string {
    if (!this.delta) return '';
    return this.formatValue(this.delta.value, this.delta.currency ?? this.currency, this.delta.percent);
  }

  get directionClass(): string {
    return `kpi-comparison--${this.delta?.direction ?? 'flat'}`;
  }

  get directionIcon(): string {
    switch (this.delta?.direction) {
      case 'up':
        return '↑';
      case 'down':
        return '↓';
      default:
        return '→';
    }
  }

  private formatValue(value: number | string | null | undefined, useCurrency: boolean, asPercent = false): string {
    if (value === null || value === undefined) {
      return '-';
    }

    if (typeof value === 'number') {
      if (useCurrency) {
        return new Intl.NumberFormat('en-IN', {
          style: 'currency',
          currency: 'INR',
          maximumFractionDigits: 0,
        }).format(value);
      }

      if (asPercent) {
        return new Intl.NumberFormat('en-IN', {
          style: 'percent',
          minimumFractionDigits: 1,
          maximumFractionDigits: 1,
        }).format(value);
      }
    }

    return String(value);
  }
}
