import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';

import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';

import { AuthService } from '../../../../core/auth/auth.service';
import { SaleService } from '../../services/sale.service';
import type {
  CreditNoteDetailDto,
  CreditNoteListItemDto,
  CreditNoteListStatusFilter,
  CreditNotesQueryParams,
  CreditNoteStatus,
  VoidCreditNoteRequest,
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
    DialogModule,
    IconFieldModule,
    InputIconModule,
    InputTextModule,
    SelectModule,
    TableModule,
    TagModule,
    TextareaModule,
    TranslocoPipe,
  ],
  templateUrl: './credit-notes-page.component.html',
  styleUrl: './credit-notes-page.component.scss',
})
export class CreditNotesPageComponent implements OnInit, OnDestroy {
  private readonly authService = inject(AuthService);
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
  readonly selectedCreditNote = signal<CreditNoteDetailDto | null>(null);
  readonly loadingNotes = signal(false);
  readonly loadingDetail = signal(false);
  readonly listErrorMessage = signal('');
  readonly detailErrorMessage = signal('');
  readonly voidDialogVisible = signal(false);
  readonly voidTargetCreditNote = signal<CreditNoteDetailDto | null>(null);
  readonly voidReason = signal('');
  readonly voidLoading = signal(false);
  readonly voidErrorMessage = signal('');

  readonly activeShopRole = computed(() => {
    const session = this.authService.session();
    if (!session) return '';

    const activeShop =
      session.shops.find((shop) => shop.shopId === session.activeShopId) ??
      session.shops.find((shop) => shop.isDefault);

    return activeShop?.role ?? '';
  });

  readonly canManageCreditNotes = computed(() =>
    ['owner', 'manager'].includes(this.activeShopRole().toLowerCase()),
  );

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
    await this.loadCreditNoteDetail(note.code);
  }

  canVoidCreditNote(note: CreditNoteDetailDto | null): boolean {
    return !!note && this.canManageCreditNotes() && note.status === 'Active' && !note.isVoided;
  }

  openVoidDialog(): void {
    const note = this.selectedCreditNote();
    if (!this.canVoidCreditNote(note)) return;

    this.voidTargetCreditNote.set(note);
    this.voidReason.set('');
    this.voidErrorMessage.set('');
    this.voidDialogVisible.set(true);
  }

  closeVoidDialog(): void {
    this.voidDialogVisible.set(false);
    this.voidTargetCreditNote.set(null);
    this.voidReason.set('');
    this.voidErrorMessage.set('');
    this.voidLoading.set(false);
  }

  updateVoidReason(reason: string): void {
    this.voidReason.set(reason);
    if (this.voidErrorMessage()) {
      this.voidErrorMessage.set('');
    }
  }

  async submitVoidCreditNote(): Promise<void> {
    const note = this.voidTargetCreditNote();
    if (!note || !this.canVoidCreditNote(note) || this.voidLoading()) return;

    const reason = this.voidReason().trim();
    if (!reason) {
      this.voidErrorMessage.set('sales.creditNotes.void.errors.reasonRequired');
      return;
    }

    this.voidLoading.set(true);
    this.voidErrorMessage.set('');

    try {
      const payload: VoidCreditNoteRequest = { reason };
      await firstValueFrom(this.saleService.voidCreditNote(note.code, payload));
      this.selectedCreditNote.set({ ...note, status: 'Voided', isVoided: true, voidReason: reason });
      this.closeVoidDialog();
      void this.loadNotes();
    } catch {
      this.voidErrorMessage.set('sales.creditNotes.void.errors.submitFailed');
    } finally {
      this.voidLoading.set(false);
    }
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
    this.listErrorMessage.set('');

    try {
      const response = await firstValueFrom(this.saleService.getCreditNotes(params));
      this.notes.set([...response.items]);
      this.totalCount.set(response.totalCount);
      this.pageNumber.set(response.pageNumber);
      this.pageSize.set(response.pageSize);
    } catch {
      this.listErrorMessage.set('sales.creditNotes.errors.loadFailed');
    } finally {
      this.loadingNotes.set(false);
    }
  }

  private async loadCreditNoteDetail(code: string): Promise<void> {
    this.loadingDetail.set(true);
    this.detailErrorMessage.set('');
    this.selectedCreditNote.set(null);

    try {
      const detail = await firstValueFrom(this.saleService.verifyCreditNote(code));
      this.selectedCreditNote.set(detail);
    } catch {
      this.selectedCreditNote.set(null);
      this.detailErrorMessage.set('sales.creditNotes.errors.verifyFailed');
    } finally {
      this.loadingDetail.set(false);
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
