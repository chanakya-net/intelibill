import { CommonModule, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { merge, startWith } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { SkeletonModule } from 'primeng/skeleton';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import type { Service, ServiceStatusFilter, ServiceSummary } from '../services/service.models';
import { ServiceService } from '../services/service.service';
import { AddServiceOverlayComponent } from '../components/add-service-overlay.component';
import { EditServiceOverlayComponent } from '../components/edit-service-overlay.component';
import { ServicesTableComponent } from '../components/services-table.component';

interface StatusOption {
  readonly label: string;
  readonly value: ServiceStatusFilter;
}

const STATUS_FILTER_KEYS: readonly { labelKey: string; value: ServiceStatusFilter }[] = [
  { labelKey: 'common.all', value: 'all' },
  { labelKey: 'services.active', value: 'active' },
  { labelKey: 'services.inactive', value: 'inactive' },
];

@Component({
  selector: 'app-services-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SelectModule,
    SkeletonModule,
    AddServiceOverlayComponent,
    EditServiceOverlayComponent,
    ServicesTableComponent,
    TranslocoPipe,
    DecimalPipe,
  ],
  templateUrl: './services-page.component.html',
  styleUrl: './services-page.component.scss',
})
export class ServicesPageComponent {
  private readonly serviceService = inject(ServiceService);
  private readonly permissions = inject(ShopPermissionsService);
  private readonly transloco = inject(TranslocoService);

  /** Recomputes translated labels when the language changes or a translation bundle loads. */
  private readonly translationsChanged = toSignal(
    merge(this.transloco.langChanges$, this.transloco.events$).pipe(startWith(null)),
    { initialValue: null },
  );

  readonly searchValue = signal('');
  readonly statusFilter = signal<ServiceStatusFilter>('all');
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly services = signal<readonly Service[]>([]);
  readonly isLoading = signal(false);
  readonly loadErrorMessage = signal('');
  readonly actionErrorMessage = signal('');
  readonly isMutating = signal(false);
  readonly showAddOverlay = signal(false);
  readonly editingService = signal<Service | null>(null);

  readonly canManageServices = this.permissions.canManageServices;

  /**
   * PrimeNG derives each option's accessible name from `optionLabel`, so the labels must be
   * resolved here instead of translated in the template — otherwise assistive technology
   * announces the raw translation keys.
   */
  readonly statusOptions = computed<StatusOption[]>(() => {
    this.translationsChanged();
    return STATUS_FILTER_KEYS.map(({ labelKey, value }) => ({
      label: this.transloco.translate(labelKey),
      value,
    }));
  });

  readonly filteredServices = computed(() => {
    const services = this.services();
    const filter = this.statusFilter();
    if (filter === 'active') {
      return services.filter((service) => service.isActive);
    }

    if (filter === 'inactive') {
      return services.filter((service) => !service.isActive);
    }

    return services;
  });

  readonly pagedServices = computed(() => {
    const start = (this.pageNumber() - 1) * this.pageSize();
    return this.filteredServices().slice(start, start + this.pageSize());
  });

  readonly summary = computed<ServiceSummary>(() => {
    const filtered = this.filteredServices();
    return {
      totalServices: filtered.length,
      activeServices: filtered.filter((service) => service.isActive).length,
      inactiveServices: filtered.filter((service) => !service.isActive).length,
      totalValue: filtered.reduce((sum, service) => sum + service.price, 0),
    };
  });

  readonly footerStart = computed(() => {
    const total = this.filteredServices().length;
    if (total === 0) {
      return 0;
    }

    return (this.pageNumber() - 1) * this.pageSize() + 1;
  });

  readonly footerEnd = computed(() => {
    const total = this.filteredServices().length;
    if (total === 0) {
      return 0;
    }

    const end = this.pageNumber() * this.pageSize();
    return end > total ? total : end;
  });

  private searchTimeout: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.loadServices();
  }

  loadServices(): void {
    this.isLoading.set(true);
    this.loadErrorMessage.set('');

    this.serviceService
      .getServices({
        search: this.searchValue(),
        includeInactive: this.statusFilter() !== 'active',
      })
      .subscribe({
        next: (services) => {
          this.services.set(services);
          this.isLoading.set(false);
          this.pageNumber.set(1);
        },
        error: () => {
          this.loadErrorMessage.set('services.loadFailed');
          this.isLoading.set(false);
        },
      });
  }

  onSearchChange(value: string): void {
    this.searchValue.set(value);
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    this.searchTimeout = setTimeout(() => {
      this.pageNumber.set(1);
      this.loadServices();
    }, 280);
  }

  onStatusFilterChange(statusFilter: ServiceStatusFilter): void {
    this.statusFilter.set(statusFilter);
    this.pageNumber.set(1);
    this.loadServices();
  }

  onTablePageChange(event: { page: number; rows: number }): void {
    this.pageNumber.set(event.page);
    this.pageSize.set(event.rows);
  }

  onOpenAddService(): void {
    if (!this.canManageServices()) {
      return;
    }

    this.actionErrorMessage.set('');
    this.showAddOverlay.set(true);
  }

  onCloseAddService(): void {
    this.showAddOverlay.set(false);
  }

  onOpenEditService(service: Service): void {
    if (!this.canManageServices()) {
      return;
    }

    this.actionErrorMessage.set('');
    this.editingService.set(service);
  }

  onCloseEditService(): void {
    this.editingService.set(null);
  }

  onServiceSaved(): void {
    this.showAddOverlay.set(false);
    this.editingService.set(null);
    this.loadServices();
  }

  onToggleService(service: Service): void {
    if (!this.canManageServices()) {
      return;
    }

    this.isMutating.set(true);
    this.actionErrorMessage.set('');

    const request = service.isActive
      ? this.serviceService.deactivateService(service.serviceId)
      : this.serviceService.activateService(service.serviceId);

    request.subscribe({
      next: () => {
        this.isMutating.set(false);
        this.loadServices();
      },
      error: () => {
        this.actionErrorMessage.set('services.toggleFailed');
        this.isMutating.set(false);
      },
    });
  }
}
