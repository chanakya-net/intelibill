import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { PaginatorModule } from 'primeng/paginator';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import type { Service } from '../services/service.models';

@Component({
  selector: 'app-services-table',
  standalone: true,
  imports: [CommonModule, ButtonModule, CardModule, PaginatorModule, TableModule, TagModule, TranslocoPipe],
  templateUrl: './services-table.component.html',
  styleUrl: './services-table.component.scss',
})
export class ServicesTableComponent {
  @Input({ required: true }) services: readonly Service[] = [];
  @Input() canManageServices = false;
  @Input() isSubmitting = false;
  @Input() pageNumber = 1;
  @Input() pageSize = 20;
  @Input() totalCount = 0;
  @Input() footerStart = 0;
  @Input() footerEnd = 0;

  @Output() editService = new EventEmitter<Service>();
  @Output() toggleService = new EventEmitter<Service>();
  @Output() pageChange = new EventEmitter<{ page: number; rows: number }>();

  get tableServices(): Service[] {
    return [...this.services];
  }

  onEditService(service: Service): void {
    this.editService.emit(service);
  }

  onToggleService(service: Service): void {
    this.toggleService.emit(service);
  }

  onPageChange(event: any): void {
    const rows = event.rows ?? this.pageSize;
    const isPageSizeChange = rows !== this.pageSize;
    const page = isPageSizeChange ? 1 : (event.page ?? 0) + 1;
    this.pageChange.emit({ page, rows });
  }

  paginationLabel(): string {
    if (this.totalCount === 0) {
      return '';
    }

    return `${this.footerStart} - ${this.footerEnd} of ${this.totalCount}`;
  }

  statusSeverity(isActive: boolean): 'success' | 'danger' {
    return isActive ? 'success' : 'danger';
  }

  taxModeLabelKey(isIncluded: boolean): string {
    return isIncluded ? 'services.taxIncluded' : 'services.taxExcluded';
  }
}
