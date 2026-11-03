import { CommonModule } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe, TranslocoService } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
import { SelectModule } from 'primeng/select';
import { SelectButtonModule } from 'primeng/selectbutton';
import { SkeletonModule } from 'primeng/skeleton';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';

import { AuthService } from '../../../../core/auth/auth.service';
import { ShopPermissionsService } from '../../../../core/layout/shop-permissions.service';
import { NetworkStatusService } from '../../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../../core/storage/offline-sales-device-settings.storage';
import { BlobDownloadService } from '../../../../shared/utils/blob-download.service';
import type { OfflineQueuedSaleRecord } from '../../services/offline-sale-core.types';
import type {
  ReconciliationAction,
  ReconciliationCase,
  ReconciliationCasePage,
  ReconciliationPerson,
  ReconciliationStatusFilter,
  ResolveReconciliationCaseRequest,
  RecoverySubmissionResult,
} from '../../services/offline-reconciliation.models';
import { OfflineReconciliationService } from '../../services/offline-reconciliation.service';
import { OfflineSalesQueueIndexedDbService } from '../../services/offline-sales-queue-indexeddb.service';
import { OfflineSalesQueueSyncService } from '../../services/offline-sales-queue-sync.service';
import { assessDeviceHandoff } from '../../utils/offline-device-handoff';
import { buildSupportBundle } from '../../utils/offline-recovery-bundle';
import { isSubmittableBy, offlineReviewReasonKey } from '../../utils/offline-sale-sync.mapper';
import {
  BUNDLE_TOO_LARGE_KEY,
  CASE_CHANGED_KEY,
  GENERIC_ERROR_KEY,
  LINKED_OPERATION_ACTIONS,
  NETWORK_ERROR_KEY,
  RESOLVE_REASON_MAX_LENGTH,
  STATUS_FILTER_OPTIONS,
  actionLabelKey,
  caseStatusLabelKey,
  caseStatusSeverity,
  isUncertainFailure,
  issueTypeLabelKey,
  paymentMethodLabelKey,
  reconciliationErrorKey,
  recoveryStatusLabelKey,
  recoveryStatusSeverity,
  serverErrorCode,
  submissionSourceLabelKey,
} from './offline-reconciliation-page.messages';

const PAGE_SIZE = 20;
const MAX_BUNDLE_BYTES = 2 * 1024 * 1024;
const LAST_EXPORT_KEY_PREFIX = 'intelibill.offlineSales.lastSupportExport.v1';
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const UNSENT_STATUSES = new Set(['Pending', 'Syncing', 'Failed']);

type LoadState = 'idle' | 'loading' | 'ready' | 'error';

/** The last support bundle this browser downloaded; the hand-off check counts its invoices as safe. */
interface SupportExportRecord {
  readonly exportedAt: string;
  readonly fileName: string;
  readonly checksum: string;
  readonly clientSaleIds: readonly string[];
}

interface PendingSubmission {
  readonly signature: string;
  readonly operationId: string;
}

interface ConsoleMessage {
  readonly kind: 'success' | 'error';
  readonly key: string;
}

/**
 * The offline reconciliation console. Local rows come from this browser's queue; everything about a case comes
 * from the server's last answer and is never changed here before the server confirms it.
 */
@Component({
  selector: 'app-offline-reconciliation-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    InputTextModule,
    PaginatorModule,
    SelectModule,
    SelectButtonModule,
    SkeletonModule,
    TableModule,
    TagModule,
    TextareaModule,
    TranslocoPipe,
  ],
  templateUrl: './offline-reconciliation-page.component.html',
  styleUrl: './offline-reconciliation-page.component.scss',
})
export class OfflineReconciliationPageComponent {
  private readonly reconciliation = inject(OfflineReconciliationService);
  private readonly queueSync = inject(OfflineSalesQueueSyncService);
  private readonly queueDb = inject(OfflineSalesQueueIndexedDbService);
  private readonly deviceSettings = inject(OfflineSalesDeviceSettingsStorage);
  private readonly permissions = inject(ShopPermissionsService);
  private readonly auth = inject(AuthService);
  private readonly network = inject(NetworkStatusService);
  private readonly downloads = inject(BlobDownloadService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly transloco = inject(TranslocoService);
  /** Option labels are read out by screen readers, so they are translated text and follow language changes. */
  private readonly activeLang = toSignal(this.transloco.langChanges$, { initialValue: this.transloco.getActiveLang() });

  private readonly detailHeading = viewChild<ElementRef<HTMLElement>>('detailHeading');

  readonly pageSize = PAGE_SIZE;
  readonly reasonMaxLength = RESOLVE_REASON_MAX_LENGTH;
  readonly statusFilterOptions = computed(() => {
    this.activeLang();
    return STATUS_FILTER_OPTIONS.map((option) => ({ value: option.value, label: this.transloco.translate(option.label) }));
  });
  readonly reviewReasonKey = offlineReviewReasonKey;
  readonly actionLabelKey = actionLabelKey;
  readonly caseStatusLabelKey = caseStatusLabelKey;
  readonly caseStatusSeverity = caseStatusSeverity;
  readonly issueTypeLabelKey = issueTypeLabelKey;
  readonly paymentMethodLabelKey = paymentMethodLabelKey;
  readonly recoveryStatusLabelKey = recoveryStatusLabelKey;
  readonly recoveryStatusSeverity = recoveryStatusSeverity;
  readonly reconciliationErrorKey = reconciliationErrorKey;
  readonly submissionSourceLabelKey = submissionSourceLabelKey;

  readonly canReachApi = this.network.canReachApi;
  readonly isOffline = computed(() => !this.canReachApi());
  readonly canRecover = this.permissions.canRecoverOfflineInvoices;
  private readonly shopId = computed(() => this.auth.session()?.activeShopId ?? null);
  private readonly userId = computed(() => this.auth.session()?.user.id ?? null);
  private readonly deviceId = computed(() => {
    const shopId = this.shopId();
    return shopId ? this.deviceSettings.loadSettings(shopId)?.deviceId ?? null : null;
  });

  // Device queue (this browser's IndexedDB).
  readonly visibleCounts = this.queueSync.visibleCounts;
  readonly reviewRows = this.queueSync.reviewSales;
  readonly awaitingRows = this.queueSync.awaitingIssuerSales;
  readonly localRecords = signal<readonly OfflineQueuedSaleRecord[]>([]);
  readonly localState = signal<LoadState>('loading');
  readonly retryingIds = signal<ReadonlySet<string>>(new Set());
  readonly unsentRows = computed(() => {
    const userId = this.userId();
    return this.localRecords()
      .filter((record) => UNSENT_STATUSES.has(record.status) && isSubmittableBy(record, userId))
      .sort((a, b) => a.soldAt.localeCompare(b.soldAt));
  });
  readonly hasLocalRows = computed(
    () => this.unsentRows().length + this.reviewRows().length + this.awaitingRows().length > 0,
  );

  // Server cases.
  readonly statusFilter = signal<ReconciliationStatusFilter>('open');
  readonly page = signal(1);
  readonly casePage = signal<ReconciliationCasePage | null>(null);
  readonly casesState = signal<LoadState>('idle');
  readonly cases = computed(() => [...(this.casePage()?.items ?? [])]);
  readonly totalCases = computed(() => this.casePage()?.totalCount ?? 0);
  readonly skeletonRows = [1, 2, 3, 4];
  private casesRequest = 0;

  // Case detail.
  readonly selectedCaseId = signal<string | null>(null);
  readonly selectedCase = signal<ReconciliationCase | null>(null);
  readonly detailState = signal<LoadState>('idle');
  private detailRequest = 0;

  // Resolve form.
  readonly resolveAction = signal<ReconciliationAction | null>(null);
  readonly resolveReason = signal('');
  readonly resolveLinkedOperationId = signal('');
  readonly resolveAttempted = signal(false);
  readonly resolving = signal(false);
  readonly resolveMessage = signal<ConsoleMessage | null>(null);
  private resolveIntent: PendingSubmission | null = null;
  readonly actionOptions = computed(() => {
    this.activeLang();
    return (this.selectedCase()?.availableActions ?? []).map((value) => ({
      value,
      label: this.transloco.translate(actionLabelKey(value)),
    }));
  });
  readonly needsLinkedOperation = computed(() => {
    const action = this.resolveAction();
    return action !== null && LINKED_OPERATION_ACTIONS.includes(action);
  });
  readonly trimmedReasonLength = computed(() => this.resolveReason().trim().length);
  readonly actionErrorKey = computed(() =>
    this.resolveAction() === null ? 'sales.offlineReconciliation.resolve.validation.actionRequired' : null,
  );
  readonly reasonErrorKey = computed(() => {
    const length = this.trimmedReasonLength();
    if (length === 0) return 'sales.offlineReconciliation.resolve.validation.reasonRequired';
    return length > RESOLVE_REASON_MAX_LENGTH ? 'sales.offlineReconciliation.resolve.validation.reasonTooLong' : null;
  });
  readonly linkedErrorKey = computed(() => {
    if (!this.needsLinkedOperation()) return null;
    const value = this.resolveLinkedOperationId().trim();
    if (!value) return 'sales.offlineReconciliation.resolve.validation.linkedRequired';
    return GUID_PATTERN.test(value) ? null : 'sales.offlineReconciliation.resolve.validation.linkedInvalid';
  });
  readonly canResolve = computed(() => {
    const current = this.selectedCase();
    return (
      this.canReachApi() &&
      !this.resolving() &&
      current !== null &&
      current.status !== 'Resolved' &&
      current.availableActions.length > 0
    );
  });

  // Support bundle.
  readonly lastExport = signal<SupportExportRecord | null>(null);
  readonly exporting = signal(false);
  readonly exportErrorKey = signal<string | null>(null);
  readonly importFile = signal<{ readonly name: string; readonly text: string } | null>(null);
  readonly importing = signal(false);
  readonly importErrorKey = signal<string | null>(null);
  readonly importResults = signal<RecoverySubmissionResult | null>(null);
  private importIntent: PendingSubmission | null = null;

  readonly handoff = computed(() => assessDeviceHandoff(this.localRecords(), this.lastExport()));

  constructor() {
    let previouslyReachable: boolean | null = null;
    effect(() => {
      const reachable = this.canReachApi();
      untracked(() => {
        if (reachable && previouslyReachable !== true) {
          this.loadCases();
          if (this.selectedCaseId()) this.loadSelectedCase();
        }
        previouslyReachable = reachable;
      });
    });

    this.lastExport.set(this.readLastExport());
    void this.reloadLocalRecords();
  }

  // ---- Device queue -------------------------------------------------------------------------------------------

  offlinePrintHref(clientSaleId: string): string {
    return `/sales/${encodeURIComponent(clientSaleId)}/print?offline=1`;
  }

  salePrintHref(saleId: string): string {
    return `/sales/${encodeURIComponent(saleId)}/print`;
  }

  isRetrying(clientSaleId: string): boolean {
    return this.retryingIds().has(clientSaleId);
  }

  async retry(clientSaleId: string): Promise<void> {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    if (!shopId || !deviceId || !this.canReachApi() || this.isRetrying(clientSaleId)) return;

    this.retryingIds.update((ids) => new Set([...ids, clientSaleId]));
    try {
      await this.queueSync.retryQueuedSale(shopId, deviceId, clientSaleId);
    } finally {
      this.retryingIds.update((ids) => new Set([...ids].filter((id) => id !== clientSaleId)));
      await this.reloadLocalRecords();
    }
  }

  async reloadLocalRecords(): Promise<void> {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    if (!shopId || !deviceId) {
      this.localRecords.set([]);
      this.localState.set('ready');
      return;
    }

    try {
      this.localRecords.set(await this.queueDb.getAllByShopDevice(shopId, deviceId));
      this.localState.set('ready');
    } catch {
      this.localState.set('error');
    }

    try {
      await this.queueSync.refreshActiveStatusCounts();
    } catch {
      // The grouped rows above keep the queue's last known state; nothing here depends on the fresh counts.
    }
  }

  // ---- Server cases ---------------------------------------------------------------------------------------------

  onStatusFilterChange(value: ReconciliationStatusFilter | null): void {
    if (!value || this.isOffline()) return;
    this.statusFilter.set(value);
    this.page.set(1);
    this.loadCases();
  }

  onPageChange(event: PaginatorState): void {
    if (this.isOffline()) return;
    this.page.set((event.page ?? 0) + 1);
    this.loadCases();
  }

  refreshCases(): void {
    this.loadCases();
  }

  private loadCases(): void {
    if (!this.canReachApi()) return;

    const request = ++this.casesRequest;
    this.casesState.set('loading');
    this.reconciliation
      .listCases(this.statusFilter(), this.page(), PAGE_SIZE)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (request !== this.casesRequest) return;
          this.casePage.set({
            items: result?.items ?? [],
            page: result?.page ?? this.page(),
            pageSize: result?.pageSize ?? PAGE_SIZE,
            totalCount: result?.totalCount ?? 0,
            unresolvedCount: result?.unresolvedCount ?? 0,
          });
          this.casesState.set('ready');
        },
        error: () => {
          if (request !== this.casesRequest) return;
          this.casesState.set('error');
        },
      });
  }

  // ---- Case detail ----------------------------------------------------------------------------------------------

  openCase(caseId: string): void {
    if (this.selectedCaseId() !== caseId) {
      this.selectedCase.set(null);
      this.resetResolveForm();
      this.resolveMessage.set(null);
    }
    this.selectedCaseId.set(caseId);
    this.loadSelectedCase(true);
  }

  closeCase(): void {
    this.detailRequest += 1;
    this.selectedCaseId.set(null);
    this.selectedCase.set(null);
    this.detailState.set('idle');
    this.resetResolveForm();
    this.resolveMessage.set(null);
  }

  /** A case carries one reason code today; the first one names it. */
  reasonKey(reasonCodes: readonly string[]): string {
    return offlineReviewReasonKey(reasonCodes[0] ?? null);
  }

  personName(person: ReconciliationPerson | null): string | null {
    const name = person?.displayName.trim() ?? '';
    return name.length > 0 ? name : null;
  }

  private loadSelectedCase(focusWhenLoaded = false): void {
    const caseId = this.selectedCaseId();
    if (!caseId) return;
    if (!this.canReachApi()) {
      if (!this.selectedCase()) this.detailState.set('idle');
      return;
    }

    const request = ++this.detailRequest;
    this.detailState.set('loading');
    this.reconciliation
      .getCase(caseId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          if (request !== this.detailRequest) return;
          this.selectedCase.set(detail);
          this.detailState.set('ready');
          const action = this.resolveAction();
          if (action && !detail.availableActions.includes(action)) this.resolveAction.set(null);
          if (focusWhenLoaded) this.focusDetail();
        },
        error: () => {
          if (request !== this.detailRequest) return;
          this.detailState.set('error');
        },
      });
  }

  private focusDetail(): void {
    afterNextRender(() => this.detailHeading()?.nativeElement.focus(), { injector: this.injector });
  }

  // ---- Resolve --------------------------------------------------------------------------------------------------

  submitResolve(): void {
    this.resolveAttempted.set(true);
    const current = this.selectedCase();
    const action = this.resolveAction();
    if (
      !current ||
      !action ||
      !this.canResolve() ||
      this.reasonErrorKey() !== null ||
      this.linkedErrorKey() !== null ||
      !current.availableActions.includes(action)
    ) {
      return;
    }

    const reason = this.resolveReason().trim();
    const linkedOperationId = this.needsLinkedOperation() ? this.resolveLinkedOperationId().trim() : null;
    const signature = JSON.stringify([current.caseId, current.version, action, reason, linkedOperationId]);
    if (this.resolveIntent?.signature !== signature) {
      this.resolveIntent = { signature, operationId: crypto.randomUUID() };
    }

    const request: ResolveReconciliationCaseRequest = {
      operationId: this.resolveIntent.operationId,
      expectedVersion: current.version,
      action,
      reason,
      linkedOperationId,
    };

    this.resolving.set(true);
    this.resolveMessage.set(null);
    this.reconciliation
      .resolveCase(current.caseId, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.resolveIntent = null;
          this.resolving.set(false);
          if (this.selectedCaseId() === updated.caseId) this.selectedCase.set(updated);
          this.resetResolveForm();
          this.resolveMessage.set({ kind: 'success', key: 'sales.offlineReconciliation.resolve.success' });
          this.loadCases();
        },
        error: (error: unknown) => {
          this.resolving.set(false);
          if (isUncertainFailure(error)) {
            // The server may have committed it: the same submission keeps its operation id so a retry replays.
            this.resolveMessage.set({ kind: 'error', key: this.uncertainFailureKey(error) });
            return;
          }

          this.resolveIntent = null;
          const status = (error as { status?: number }).status;
          const code = serverErrorCode(error);
          if (status === 409) {
            const key = reconciliationErrorKey(code);
            this.resolveMessage.set({ kind: 'error', key: key === GENERIC_ERROR_KEY ? CASE_CHANGED_KEY : key });
            this.loadSelectedCase();
            this.loadCases();
            return;
          }

          this.resolveMessage.set({ kind: 'error', key: reconciliationErrorKey(code) });
        },
      });
  }

  private resetResolveForm(): void {
    this.resolveAction.set(null);
    this.resolveReason.set('');
    this.resolveLinkedOperationId.set('');
    this.resolveAttempted.set(false);
    this.resolveIntent = null;
  }

  // ---- Support bundle -------------------------------------------------------------------------------------------

  async exportBundle(): Promise<void> {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    if (!shopId || !deviceId || this.exporting()) return;

    this.exporting.set(true);
    this.exportErrorKey.set(null);
    try {
      const records = await this.queueDb.getAllByShopDevice(shopId, deviceId);
      this.localRecords.set(records);
      const exportedAt = new Date().toISOString();
      const bundle = await buildSupportBundle(records, { shopId, deviceId, exportedAt });
      if (bundle.clientSaleIds.length === 0) {
        this.exportErrorKey.set('sales.offlineReconciliation.bundle.exportEmpty');
        return;
      }

      this.downloads.download(new Blob([bundle.text], { type: 'application/json' }), bundle.fileName);
      const exported: SupportExportRecord = {
        exportedAt,
        fileName: bundle.fileName,
        checksum: bundle.checksum,
        clientSaleIds: bundle.clientSaleIds,
      };
      this.writeLastExport(exported);
      this.lastExport.set(exported);
    } catch {
      this.exportErrorKey.set('sales.offlineReconciliation.bundle.exportError');
    } finally {
      this.exporting.set(false);
    }
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement | null;
    const file = input?.files?.[0] ?? null;
    if (!file) return;

    this.importResults.set(null);
    this.importErrorKey.set(null);
    try {
      if (file.size > MAX_BUNDLE_BYTES) {
        this.importFile.set(null);
        this.importErrorKey.set(BUNDLE_TOO_LARGE_KEY);
        return;
      }
      this.importFile.set({ name: file.name, text: await readFileText(file) });
    } catch {
      this.importFile.set(null);
      this.importErrorKey.set('sales.offlineReconciliation.bundle.readError');
    } finally {
      // Lets the same file be chosen again, for example to retry after a lost connection.
      if (input) input.value = '';
    }
  }

  submitImport(): void {
    const file = this.importFile();
    if (!file || !this.canRecover() || !this.canReachApi() || this.importing()) return;

    if (this.importIntent?.signature !== file.text) {
      this.importIntent = { signature: file.text, operationId: crypto.randomUUID() };
    }
    const operationId = this.importIntent.operationId;

    this.importing.set(true);
    this.importErrorKey.set(null);
    this.reconciliation
      .submitRecoveryBundle(file.text, operationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.importIntent = null;
          this.importing.set(false);
          this.importFile.set(null);
          this.importResults.set(result);
          this.loadCases();
        },
        error: (error: unknown) => {
          this.importing.set(false);
          if (isUncertainFailure(error)) {
            this.importErrorKey.set(this.uncertainFailureKey(error));
            return;
          }

          this.importIntent = null;
          const status = (error as { status?: number }).status;
          this.importErrorKey.set(status === 413 ? BUNDLE_TOO_LARGE_KEY : reconciliationErrorKey(serverErrorCode(error)));
        },
      });
  }

  private uncertainFailureKey(error: unknown): string {
    return (error as { status?: number }).status === 0 ? NETWORK_ERROR_KEY : GENERIC_ERROR_KEY;
  }

  private lastExportStorageKey(): string | null {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    return shopId && deviceId ? `${LAST_EXPORT_KEY_PREFIX}:${shopId}:${deviceId}` : null;
  }

  private readLastExport(): SupportExportRecord | null {
    const key = this.lastExportStorageKey();
    if (!key) return null;
    try {
      const parsed = JSON.parse(localStorage.getItem(key) ?? 'null') as Partial<SupportExportRecord> | null;
      if (
        !parsed ||
        typeof parsed.exportedAt !== 'string' ||
        typeof parsed.fileName !== 'string' ||
        typeof parsed.checksum !== 'string' ||
        !Array.isArray(parsed.clientSaleIds) ||
        !parsed.clientSaleIds.every((id) => typeof id === 'string')
      ) {
        return null;
      }
      return parsed as SupportExportRecord;
    } catch {
      return null;
    }
  }

  private writeLastExport(record: SupportExportRecord): void {
    const key = this.lastExportStorageKey();
    if (!key) return;
    try {
      localStorage.setItem(key, JSON.stringify(record));
    } catch {
      // Storage full or blocked: the hand-off notice still counts this export until the page is reloaded.
    }
  }
}

async function readFileText(file: File): Promise<string> {
  if (typeof file.text === 'function') return await file.text();
  return await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(file);
  });
}
