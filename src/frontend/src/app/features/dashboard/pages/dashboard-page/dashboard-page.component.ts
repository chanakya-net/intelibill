import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopPermissionsService } from '../../../../core/layout/shop-permissions.service';
import { DashboardDto } from '../../services/dashboard.models';
import { DashboardService } from '../../services/dashboard.service';

@Component({
  selector: 'app-dashboard-page',
  standalone: true,
  template: `
    <section class="dashboard-page">
      <header>
        <h1>Dashboard</h1>
        <p>{{ activeShopLabel() }}</p>
      </header>
      <p>{{ isLoading() ? 'Loading dashboard data...' : dashboardStatus() }}</p>
      @if (dashboard(); as d) {
        <div class="kpi-cards">
          <div class="kpi-card" data-testid="stock-value-kpi">
            <span class="kpi-label">Stock Value</span>
            <span class="kpi-value">{{ formattedStockValue() }}</span>
          </div>
        </div>
      }
    </section>
  `,
})
export class DashboardPageComponent {
  private readonly authService = inject(AuthService);
  private readonly dashboardService = inject(DashboardService);
  private readonly permissions = inject(ShopPermissionsService);
  private readonly router = inject(Router);

  readonly dashboard = signal<DashboardDto | null>(null);
  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly activeShopLabel = computed(() => {
    const activeShop = this.permissions.activeShop();

    if (!activeShop) {
      return 'No active shop';
    }

    return `${activeShop.shopName} · ${activeShop.role}`;
  });
  readonly dashboardStatus = computed(() => {
    if (this.errorMessage()) {
      return this.errorMessage();
    }

    const dashboard = this.dashboard();
    if (!dashboard) {
      return 'Dashboard ready.';
    }

    return `Sales count: ${dashboard.salesCount}`;
  });
  readonly formattedStockValue = computed(() => {
    const d = this.dashboard();
    const value = d?.stockValue ?? 0;
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(value);
  });

  constructor() {
    if (!this.permissions.isOwnerOrManagerOfActiveShop()) {
      void this.router.navigateByUrl('/sales');
      return;
    }

    this.loadDashboard();
  }

  private loadDashboard(): void {
    this.isLoading.set(true);
    this.errorMessage.set('');

    this.dashboardService.getDashboard({}).subscribe({
      next: (dashboard) => {
        this.dashboard.set(dashboard);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('dashboard.loadFailed');
        this.isLoading.set(false);
      },
    });
  }
}
