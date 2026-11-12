import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { finalize, type Subscription } from 'rxjs';

import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { SaleInvoiceA4Component } from '../../sales/components/sale-invoice-a4.component';
import { SaleInvoiceThermalComponent } from '../../sales/components/sale-invoice-thermal.component';
import type { ShopSetupPreferences, ShopSetupSample } from '../services/shop-setup.models';
import { setupReasonKey } from '../services/shop-setup-reason';
import { ShopSetupService } from '../services/shop-setup.service';

@Component({
  selector: 'app-shop-setup-sample-page',
  imports: [ButtonModule, RouterLink, SaleInvoiceA4Component, SaleInvoiceThermalComponent, TranslocoPipe],
  templateUrl: './shop-setup-sample-page.component.html',
  styleUrl: './shop-setup-sample-page.component.scss',
})
export class ShopSetupSamplePageComponent {
  private readonly service = inject(ShopSetupService);
  private readonly context = inject(ShopContextService);
  private readonly destroyRef = inject(DestroyRef);
  private reads: Subscription[] = [];

  readonly sample = signal<ShopSetupSample | null>(null);
  readonly format = signal<'a4' | 'thermal'>('a4');
  readonly preferences = signal<ShopSetupPreferences | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly failureKey = signal<string | null>(null);
  readonly failureReasons = signal<readonly string[]>([]);
  readonly saveError = signal<string | null>(null);

  constructor() {
    this.destroyRef.onDestroy(this.context.onDetach(() => {
      this.cancelReads();
      this.sample.set(null);
      this.preferences.set(null);
      this.failureKey.set(null);
      this.failureReasons.set([]);
      this.saveError.set(null);
    }));
    this.refresh();
  }

  refresh(): void {
    this.cancelReads();
    this.failureKey.set(null);
    this.failureReasons.set([]);
    this.sample.set(null);
    this.preferences.set(null);
    const scope = this.context.scope();
    if (!scope?.shopId) {
      this.failureKey.set('shopSetup.loadFailed');
      return;
    }

    this.loading.set(true);
    const read = this.service.getSample(scope).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: (sample) => {
        if (!this.isCurrent(scope)) return;
        if (sample.isSample === true && sample.document?.version === 2 && sample.document.number === '') {
          this.sample.set(sample);
        } else {
          this.failureKey.set('shopSetup.loadFailed');
        }
      },
      error: (error: { status?: number; error?: { errors?: Array<{ code: string }> } }) => {
        if (!this.isCurrent(scope)) return;
        this.failureKey.set(error.status === 409 ? 'shopSetup.sampleUnavailable' : 'shopSetup.loadFailed');
        if (error.status === 409) {
          this.failureReasons.set((error.error?.errors ?? [])
            .filter(({ code }) => code !== 'ShopSetup.SampleUnavailable').map(({ code }) => setupReasonKey(code)));
        }
      },
    });
    this.reads.push(read);
    this.loadPreferences(scope);
  }

  acknowledge(): void {
    const scope = this.context.scope();
    const preferences = this.preferences();
    if (!scope?.shopId || !preferences || !this.sample() || this.saving()) return;

    this.saving.set(true);
    this.saveError.set(null);
    this.service.save(scope, {
      expectedVersion: preferences.version,
      showOnStartup: preferences.showOnStartup,
      acknowledgeSample: true,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(false)),
    ).subscribe({
      next: (saved) => {
        if (this.isCurrent(scope)) this.preferences.set(saved);
      },
      error: (error: { status?: number }) => {
        if (!this.isCurrent(scope)) return;
        this.saveError.set(error.status === 409 ? 'shopSetup.conflict' : 'shopSetup.saveFailed');
        this.loadPreferences(scope);
      },
    });
  }

  print(): void {
    if (this.sample()) window.print();
  }

  private loadPreferences(scope: ShopScope): void {
    const read = this.service.getPreferences(scope).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (preferences) => {
        if (this.isCurrent(scope)) this.preferences.set(preferences);
      },
      error: () => {
        if (this.isCurrent(scope)) this.preferences.set(null);
      },
    });
    this.reads.push(read);
  }

  private cancelReads(): void {
    this.reads.forEach((read) => read.unsubscribe());
    this.reads = [];
  }

  private isCurrent(scope: ShopScope): boolean {
    return this.context.isCurrent(scope) && this.context.epoch() === scope.epoch;
  }
}
