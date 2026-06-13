import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';

import { SaleService } from '../../services/sale.service';
import type {
  CreditNoteDetailDto,
  CreditNoteListItemDto,
  CreditNoteListStatusFilter,
  CreditNotesQueryParams,
  CreditNoteStatus,
} from '../../services/sale.models';

type CreditNoteStatusOption = {
  readonly label: string;
  readonly value: CreditNoteListStatusFilter;
};

@Component({
  selector: 'app-credit-notes-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    TranslocoPipe,
  ],
  templateUrl: './credit-notes-page.component.html',
  styleUrl: './credit-notes-page.component.scss',
})
export class CreditNotesPageComponent implements OnInit, OnDestroy {
  private readonly saleService = inject(SaleService);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly notes = signal<CreditNoteListItemDto[]>([]);
  readonly totalCount = signal(0);
  readonly pageNumber = signal(1);
  readonly pageSize = signal(20);
  readonly searchValue = signal('');
  readonly debouncedSearchValue = signal('');
  readonly statusFilter = signal<CreditNoteListStatusFilter>('all');
  readonly verifyCode = signal('');
  readonly selectedSummary = signal<CreditNoteListItemDto | null>(null);
  readonly selectedCreditNote = signal<CreditNoteDetailDto | null>(null);
  readonly loadingNotes = signal(false);
  readonly loadingDetail = signal(false);
  readonly errorMessage = signal('');

  readonly statusOptions: CreditNoteStatusOption[] = [
    { label: 'sales.creditNotes.filters.all', value: 'all' },
    { label: 'sales.creditNotes.filters.active', value: 'Active' },
    { label: 'sales.creditNotes.filters.fullyRedeemed', value: 'FullyRedeemed' },
    { label: 'sales.creditNotes.filters.expired', value: 'Expired' },
    { label: 'sales.creditNotes.filters.voided', value: 'Voided' },
  ];

  readonly pageSizeOptions = [
    { label: '10', value: 10 },
    { label: '20', value: 20 },
    { label: '50', value: 50 },
  ];

  ngOnInit(): void {
    void this.loadNotes();
  }

  ngOnDestroy(): void {
    this.clearSearchTimer();
  }

  getStatusSeverity(status: CreditNoteStatus): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case 'Active':
        return 'success';
      case 'FullyRedeemed':
        return 'info';
      case 'Expired':
        return 'warn';
      case 'Voided':
        return 'danger';
      default:
        return 'secondary';
    }
  }

  getStatusTranslationKey(status: CreditNoteStatus): string {
    return `sales.creditNotes.status.${status}`;
  }

  formatCurrency(value: number): string {
    return `₹${value.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
  }

  formatDateTime(value: string | null | undefined): string {
    if (!value) return '--';
    return new Date(value).toLocaleString('en-IN', {
      dateStyle: 'medium',
      timeStyle: 'short',
    });
  }

  onSearchChange(value: string): void {
    this.searchValue.set(value);
    this.pageNumber.set(1);
    this.scheduleSearchRefresh();
  }

  onStatusChange(value: CreditNoteListStatusFilter): void {
    this.statusFilter.set(value);
    this.pageNumber.set(1);
    this.clearSearchTimer();
    this.debouncedSearchValue.set(this.searchValue().trim());
    void this.loadNotes();
  }

  onPageChange(nextPage: number): void {
    const clamped = Math.min(Math.max(1, nextPage), this.totalPages());
    if (clamped === this.pageNumber()) return;
    this.pageNumber.set(clamped);
    void this.loadNotes();
  }

  onPageSizeChange(nextPageSize: number): void {
    this.pageSize.set(nextPageSize);
    this.pageNumber.set(1);
    this.clearSearchTimer();
    this.debouncedSearchValue.set(this.searchValue().trim());
    void this.loadNotes();
  }

  clearFilters(): void {
    this.clearSearchTimer();
    this.searchValue.set('');
    this.debouncedSearchValue.set('');
    this.statusFilter.set('all');
    this.pageNumber.set(1);
    this.pageSize.set(20);
    void this.loadNotes();
  }

  async onVerifyCode(): Promise<void> {
    const code = this.verifyCode().trim();
    if (!code) {
      return;
    }

    await this.loadCreditNoteDetail(code);
  }

  async openCreditNoteFromList(note: CreditNoteListItemDto): Promise<void> {
    await this.loadCreditNoteDetail(note.code, note);
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount() / Math.max(1, this.pageSize())));
  }

  private async loadNotes(): Promise<void> {
    const params: CreditNotesQueryParams = {
      search: this.debouncedSearchValue().trim() || undefined,
      status: this.statusFilter() === 'all' ? undefined : (this.statusFilter() as CreditNoteStatus),
      page: this.pageNumber(),
      pageSize: this.pageSize(),
    };

    this.loadingNotes.set(true);
    this.errorMessage.set('');

    try {
      const response = await firstValueFrom(this.saleService.getCreditNotes(params));
      this.notes.set([...response.items]);
      this.totalCount.set(response.totalCount);
      this.pageNumber.set(response.pageNumber);
      this.pageSize.set(response.pageSize);
    } catch {
      this.errorMessage.set('sales.creditNotes.errors.loadFailed');
    } finally {
      this.loadingNotes.set(false);
    }
  }

  private async loadCreditNoteDetail(code: string, summary?: CreditNoteListItemDto): Promise<void> {
    this.loadingDetail.set(true);
    this.errorMessage.set('');

    if (summary) {
      this.selectedSummary.set(summary);
    }

    try {
      const detail = await firstValueFrom(this.saleService.verifyCreditNote(code));
      const noteSummary = summary ?? (await this.findSummaryByCode(code));
      this.selectedSummary.set(noteSummary);
      this.selectedCreditNote.set(detail);
    } catch {
      this.selectedSummary.set(summary ?? null);
      this.selectedCreditNote.set(null);
      this.errorMessage.set('sales.creditNotes.errors.verifyFailed');
    } finally {
      this.loadingDetail.set(false);
    }
  }

  private async findSummaryByCode(code: string): Promise<CreditNoteListItemDto | null> {
    try {
      const response = await firstValueFrom(
        this.saleService.getCreditNotes({ search: code, page: 1, pageSize: 20 }),
      );
      return response.items.find((item) => item.code.toLowerCase() === code.toLowerCase()) ?? null;
    } catch {
      return null;
    }
  }

  private scheduleSearchRefresh(): void {
    this.clearSearchTimer();
    this.searchTimer = setTimeout(() => {
      this.debouncedSearchValue.set(this.searchValue().trim());
      void this.loadNotes();
    }, 300);
  }

  private clearSearchTimer(): void {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }
  }
}
