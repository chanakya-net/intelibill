import { CommonModule, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';

import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { SkeletonModule } from 'primeng/skeleton';

import { ShopPermissionsService } from '../../../core/layout/shop-permissions.service';
import type {
  Service,
  ServiceStatusFilter,
  ServiceSummary,
} from '../services/service.models';
import { ServiceService } from '../services/service.service';
import { AddServiceOverlayComponent } from '../components/add-service-overlay.component';
import { EditServiceOverlayComponent } from '../components/edit-service-overlay.component';
import { ServicesTableComponent } from '../components/services-table.component';

interface StatusOption {
  readonly label: string;
  readonly value: ServiceStatusFilter;
}

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

  readonly searchValue = signal('');
  readonly statusFilter = signal<ServiceStatusFilter>('all');
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly services = signal<readonly Service[]>([]);
  readonly isLoading = signal(false);
  readonly errorMessage = signal('');
  readonly isMutating = signal(false);
  readonly showAddOverlay = signal(false);
  readonly editingService = signal<Service | null>(null);

  readonly canManageServices = this.permissions.canManageServices;

  readonly statusOptions: StatusOption[] = [
    { label: 'common.all', value: 'all' },
    { label: 'services.active', value: 'active' },
    { label: 'services.inactive', value: 'inactive' },
  ];

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
    this.errorMessage.set('');

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
          this.errorMessage.set('services.loadFailed');
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

    this.errorMessage.set('');
    this.showAddOverlay.set(true);
  }

  onCloseAddService(): void {
    this.showAddOverlay.set(false);
  }

  onOpenEditService(service: Service): void {
    if (!this.canManageServices()) {
      return;
    }

    this.errorMessage.set('');
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
    this.errorMessage.set('');

    const request = service.isActive
      ? this.serviceService.deactivateService(service.serviceId)
      : this.serviceService.activateService(service.serviceId);

    request.subscribe({
      next: () => {
        this.isMutating.set(false);
        this.loadServices();
      },
      error: () => {
        this.errorMessage.set('services.toggleFailed');
        this.isMutating.set(false);
      },
    });
  }
}
