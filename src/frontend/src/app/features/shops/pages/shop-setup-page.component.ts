import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { finalize, type Subscription } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { ManageShopOverlayComponent } from '../components/manage-shop-overlay.component';
import type { ShopSetupStatus } from '../services/shop-setup.models';
import { setupReasonKey } from '../services/shop-setup-reason';
import { ShopSetupService } from '../services/shop-setup.service';

@Component({
  selector: 'app-shop-setup-page',
  imports: [ButtonModule, ManageShopOverlayComponent, RouterLink, TranslocoPipe],
  templateUrl: './shop-setup-page.component.html',
  styleUrl: './shop-setup-page.component.scss',
})
export class ShopSetupPageComponent {
  private readonly service = inject(ShopSetupService);
  private readonly context = inject(ShopContextService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private read?: Subscription;

  readonly status = signal<ShopSetupStatus | null>(null);
  readonly loading = signal(false);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly saveError = signal<string | null>(null);
  readonly settingsStep = signal<1 | 2 | null>(null);
  readonly activeShops = computed(() =>
    this.auth.session()?.shops.filter((shop) => shop.shopId === this.context.scope()?.shopId) ?? [],
  );
  readonly reasonKey = setupReasonKey;

  constructor() {
    this.destroyRef.onDestroy(this.context.onDetach(() => {
      this.read?.unsubscribe();
      this.status.set(null);
      this.settingsStep.set(null);
      this.loadFailed.set(false);
      this.saveError.set(null);
    }));
    this.refresh();
  }

  refresh(): void {
    const scope = this.context.scope();
    this.read?.unsubscribe();
    this.loadFailed.set(false);
    if (!scope?.shopId) {
      this.loadFailed.set(true);
      return;
    }

    this.loading.set(true);
    this.read = this.service.getStatus(scope).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false)),
    ).subscribe({
      next: (status) => {
        if (!this.isCurrent(scope)) return;
        if (status.shopId === scope.shopId && status.contractVersion === 1) {
          this.status.set(status);
        } else {
          this.status.set(null);
          this.loadFailed.set(true);
        }
      },
      error: () => {
        if (!this.isCurrent(scope)) return;
        this.status.set(null);
        this.loadFailed.set(true);
      },
    });
  }

  closeSettings(): void {
    this.settingsStep.set(null);
    this.refresh();
  }

  leave(path: '/dashboard' | '/sales/new'): void {
    if (this.saving()) return;
    const status = this.status();
    const scope = this.context.scope();
    if (!status || !scope?.shopId || status.shopId !== scope.shopId ||
        !status.permissions.canSavePreferences || !status.preferences.showOnStartup) {
      void this.router.navigateByUrl(path);
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    this.service.save(scope, {
      expectedVersion: status.preferences.version,
      showOnStartup: false,
      acknowledgeSample: false,
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.saving.set(false)),
    ).subscribe({
      next: () => {
        if (this.isCurrent(scope)) void this.router.navigateByUrl(path);
      },
      error: (error: { status?: number }) => {
        if (!this.isCurrent(scope)) return;
        this.saveError.set(error.status === 409 ? 'shopSetup.conflict' : 'shopSetup.saveFailed');
        this.refresh();
      },
    });
  }

  private isCurrent(scope: ShopScope): boolean {
    return this.context.isCurrent(scope) && this.context.epoch() === scope.epoch;
  }
}
