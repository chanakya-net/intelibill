import { TillPendingActionsComponent } from '../../../tills/components/till-pending-actions.component';
import type { TenderMethod } from '../../../tills/services/till.models';
import { CommonModule, formatDate, formatNumber } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  LOCALE_ID,
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
import type {
  ReconciliationAction,
  ReconciliationCase,
  ReconciliationCasePage,
  ReconciliationLinkCandidate,
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
const UNSENT_STATUSES = new Set(['Pending', 'Syncing', 'Failed']);
const DATE_FORMAT = 'dd MMM yyyy, h:mm a';
/** Which candidates each link action may name. */
const CANDIDATE_KIND: Readonly<
  Partial<Record<ReconciliationAction, ReconciliationLinkCandidate['kind']>>
> = {
  LinkStockCorrection: 'Adjustment',
  LinkExistingSale: 'Sale',
};

type LoadState = 'idle' | 'loading' | 'ready' | 'error';

interface SupportExportFile {
  readonly fileName: string;
  readonly checksum: string;
}

/**
 * The last support export this browser's user confirmed saving, every file of it; the hand-off check counts its
 * invoices as safe. A download that was only started is never recorded.
 */
interface SupportExportRecord {
  readonly exportedAt: string;
  readonly files: readonly SupportExportFile[];
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
    TillPendingActionsComponent,
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
  private readonly locale = inject(LOCALE_ID);
  /** Option labels are read out by screen readers, so they are translated text and follow language changes. */
  private readonly activeLang = toSignal(this.transloco.langChanges$, {
    initialValue: this.transloco.getActiveLang(),
  });

  private readonly detailHeading = viewChild<ElementRef<HTMLElement>>('detailHeading');

  readonly pageSize = PAGE_SIZE;
  readonly reasonMaxLength = RESOLVE_REASON_MAX_LENGTH;
  readonly statusFilterOptions = computed(() => {
    this.activeLang();
    return STATUS_FILTER_OPTIONS.map((option) => ({
      value: option.value,
      label: this.transloco.translate(option.label),
    }));
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
  /** Owners and managers see every action the server allows; staff may only escalate their own cases. */
  readonly isReviewer = this.permissions.isOwnerOrManagerOfActiveShop;
  private readonly shopId = computed(() => this.auth.session()?.activeShopId ?? null);
  private readonly userId = computed(() => this.auth.session()?.user.id ?? null);
  private readonly deviceId = computed(() => {
    const shopId = this.shopId();
    return shopId ? (this.deviceSettings.loadSettings(shopId)?.deviceId ?? null) : null;
  });

  // Device queue (this browser's IndexedDB).
  readonly visibleCounts = this.queueSync.visibleCounts;
  readonly reviewRows = this.queueSync.reviewSales;
  readonly awaitingRows = this.queueSync.awaitingIssuerSales;
  /** Live from the sync service, so a background sync run moves a row between groups without a reload. */
  readonly localRecords = this.queueSync.unsentSales;
  readonly localState = signal<LoadState>('loading');
  readonly retryingIds = signal<ReadonlySet<string>>(new Set());
  readonly resubmittingIds = signal<ReadonlySet<string>>(new Set());
  readonly resubmitFailedInvoice = signal<string | null>(null);
  /** Whether each held row's server case kept the original invoice (true), learnt from its detail; absent until known. */
  readonly caseEnvelopes = signal<ReadonlyMap<string, boolean>>(new Map());
  private readonly envelopeLookups = new Set<string>();
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
  readonly resolveLocked = signal(false);
  evidenceStatus: '' | 'Recorded' | 'Unknown' = '';
  evidenceReason = '';
  evidenceMethod: TenderMethod | '' = '';
  readonly evidenceMethods: TenderMethod[] = ['Cash', 'Upi', 'Card', 'BankTransfer', 'Cheque'];
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
  /** The server's link candidates for the chosen action, as translated option labels. */
  readonly linkOptions = computed(() => {
    this.activeLang();
    const kind = CANDIDATE_KIND[this.resolveAction() ?? 'Escalate'];
    if (!kind) return [];
    return (this.selectedCase()?.linkCandidates ?? [])
      .filter((candidate) => candidate.kind === kind)
      .map((candidate) => ({
        value: candidate.operationId,
        label: this.candidateLabel(candidate),
      }));
  });
  readonly trimmedReasonLength = computed(() => this.resolveReason().trim().length);
  readonly actionErrorKey = computed(() =>
    this.resolveAction() === null
      ? 'sales.offlineReconciliation.resolve.validation.actionRequired'
      : null,
  );
  readonly reasonErrorKey = computed(() => {
    const length = this.trimmedReasonLength();
    if (length === 0) return 'sales.offlineReconciliation.resolve.validation.reasonRequired';
    return length > RESOLVE_REASON_MAX_LENGTH
      ? 'sales.offlineReconciliation.resolve.validation.reasonTooLong'
      : null;
  });
  readonly linkedErrorKey = computed(() => {
    if (!this.needsLinkedOperation()) return null;
    const value = this.resolveLinkedOperationId();
    return this.linkOptions().some((option) => option.value === value)
      ? null
      : 'sales.offlineReconciliation.resolve.validation.linkedRequired';
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
  /** Files just handed to the browser to save; they count for the hand-off only once the user confirms. */
  readonly pendingExport = signal<SupportExportRecord | null>(null);
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

    // Learns, once per case, whether each held row's case kept its original invoice (see canResubmit).
    effect(() => {
      const rows = this.reviewRows();
      const reachable = this.canReachApi();
      untracked(() => {
        if (reachable)
          for (const row of rows) if (row.reviewCaseId) this.lookUpEnvelope(row.reviewCaseId);
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

  /**
   * A held invoice may be sent once more, by its issuer (or anyone, when unstamped), when the server has no
   * original copy of it: no case was recorded, or its case was filed before the server kept original invoices.
   */
  canResubmit(row: {
    readonly clientSaleId: string;
    readonly reviewCaseId: string | null;
  }): boolean {
    const record = this.localRecords().find(
      (candidate) => candidate.clientSaleId === row.clientSaleId,
    );
    if (!record || record.status !== 'NeedsReview' || !isSubmittableBy(record, this.userId()))
      return false;
    return row.reviewCaseId === null || this.caseEnvelopes().get(row.reviewCaseId) === false;
  }

  isResubmitting(clientSaleId: string): boolean {
    return this.resubmittingIds().has(clientSaleId);
  }

  async resubmit(row: {
    readonly clientSaleId: string;
    readonly invoiceNumber: string;
  }): Promise<void> {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    const clientSaleId = row.clientSaleId;
    if (!shopId || !deviceId || !this.canReachApi() || this.isResubmitting(clientSaleId)) return;

    const caseIdBefore =
      this.reviewRows().find((candidate) => candidate.clientSaleId === clientSaleId)
        ?.reviewCaseId ?? null;
    this.resubmitFailedInvoice.set(null);
    this.resubmittingIds.update((ids) => new Set([...ids, clientSaleId]));
    try {
      const result = await this.queueSync.resubmitForReview(shopId, deviceId, clientSaleId);
      if (result.failedCount > 0) this.resubmitFailedInvoice.set(row.invoiceNumber);
    } catch {
      this.resubmitFailedInvoice.set(row.invoiceNumber);
    } finally {
      this.resubmittingIds.update((ids) => new Set([...ids].filter((id) => id !== clientSaleId)));
      await this.reloadLocalRecords();
      // The server may hold an envelope now: forget what was learned so the next refresh asks again.
      const caseIdAfter =
        this.reviewRows().find((candidate) => candidate.clientSaleId === clientSaleId)
          ?.reviewCaseId ?? null;
      for (const caseId of new Set([caseIdBefore, caseIdAfter])) {
        if (!caseId) continue;
        this.envelopeLookups.delete(caseId);
        this.lookUpEnvelope(caseId);
      }
      this.loadCases();
    }
  }

  private lookUpEnvelope(caseId: string): void {
    if (this.envelopeLookups.has(caseId)) return;
    this.envelopeLookups.add(caseId);
    this.reconciliation
      .getCase(caseId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => this.rememberEnvelope(detail),
        // Unknown stays unknown (the row offers no resend); a later page load asks again.
        error: () => this.envelopeLookups.delete(caseId),
      });
  }

  private rememberEnvelope(detail: ReconciliationCase): void {
    this.caseEnvelopes.update((known) =>
      new Map(known).set(detail.caseId, detail.hasOriginalEnvelope),
    );
  }

  async reloadLocalRecords(): Promise<void> {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    if (!shopId || !deviceId) {
      this.localState.set('ready');
      return;
    }

    try {
      await this.queueSync.refreshActiveStatusCounts();
      this.localState.set('ready');
    } catch {
      this.localState.set('error');
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
          this.rememberEnvelope(detail);
          this.detailState.set('ready');
          const action = this.resolveAction();
          if (action && !detail.availableActions.includes(action)) this.resolveAction.set(null);
          // A reload may withdraw a candidate (linked elsewhere, voided): never submit one the server no longer offers.
          if (
            !this.linkOptions().some((option) => option.value === this.resolveLinkedOperationId())
          ) {
            this.resolveLinkedOperationId.set('');
          }
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

  /** Reloads the open case, for example after recording the adjustment a link needs. */
  reloadCase(): void {
    this.loadSelectedCase();
  }

  formatWhen(value: string): string {
    return formatDate(value, DATE_FORMAT, this.locale);
  }

  formatQuantity(value: number | null): string {
    return formatNumber(value ?? 0, this.locale, '1.0-3');
  }

  private candidateLabel(candidate: ReconciliationLinkCandidate): string {
    const date = this.formatWhen(candidate.occurredAt);
    return candidate.kind === 'Adjustment'
      ? this.transloco.translate('sales.offlineReconciliation.resolve.candidateAdjustment', {
          number: candidate.label,
          quantity: this.formatQuantity(candidate.quantity),
          date,
        })
      : this.transloco.translate('sales.offlineReconciliation.resolve.candidateSale', {
          invoice: candidate.label,
          date,
        });
  }

  // ---- Resolve --------------------------------------------------------------------------------------------------

  submitResolve(): void {
    if (this.resolveLocked()) return;
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
    const linkedOperationId = this.needsLinkedOperation() ? this.resolveLinkedOperationId() : null;
    const signature = JSON.stringify([
      current.caseId,
      current.version,
      action,
      reason,
      linkedOperationId,
    ]);
    if (this.resolveIntent?.signature !== signature) {
      this.resolveIntent = { signature, operationId: crypto.randomUUID() };
    }

    let tillReconciliationEvidence: ResolveReconciliationCaseRequest['tillReconciliationEvidence'];
    if (action === 'ReconcileTillSale') {
      const invoice = current.submission?.invoice;
      const hasFrozen = !!(
        invoice &&
        (invoice.till != null ||
          invoice.tenderLines != null ||
          invoice.tillBusinessDate != null ||
          invoice.tillCashierId != null)
      );
      if (
        !invoice ||
        !this.evidenceStatus ||
        !this.evidenceReason.trim() ||
        (this.evidenceStatus === 'Unknown' && hasFrozen) ||
        (this.evidenceStatus === 'Recorded' &&
          invoice.paidAmount > 0 &&
          invoice.tenderLines == null &&
          !this.evidenceMethod)
      ) {
        this.resolveMessage.set({ kind: 'error', key: 'tills.evidenceRequired' });
        return;
      }
      tillReconciliationEvidence = {
        evidenceStatus: this.evidenceStatus,
        reason: this.evidenceReason.trim(),
        tenderLines:
          this.evidenceStatus === 'Unknown'
            ? []
            : structuredClone(
                invoice.tenderLines ??
                  (invoice.paidAmount > 0
                    ? [
                        {
                          method: this.evidenceMethod as TenderMethod,
                          appliedAmount: invoice.paidAmount,
                        },
                      ]
                    : []),
              ),
      };
    }
    const request: ResolveReconciliationCaseRequest = {
      operationId: this.resolveIntent.operationId,
      tillOwner: { userId: this.userId()!, shopId: this.shopId()! },
      ...(action === 'ReconcileTillSale' ? { tillReconciliationEvidence } : {}),
      expectedVersion: current.version,
      action,
      reason,
      linkedOperationId,
    };

    const submittedCaseId = current.caseId;
    this.resolving.set(true);
    this.resolveMessage.set(null);
    this.reconciliation
      .resolveCase(current.caseId, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.resolving.set(false);
          this.loadCases();
          // The reply belongs to the case it was sent for; another case may be open by now.
          if (this.selectedCaseId() !== submittedCaseId) return;
          this.resolveIntent = null;
          this.selectedCase.set(updated);
          this.resetResolveForm();
          this.resolveMessage.set({
            kind: 'success',
            key: 'sales.offlineReconciliation.resolve.success',
          });
        },
        error: (error: unknown) => {
          this.resolving.set(false);
          const stillOpen = this.selectedCaseId() === submittedCaseId;
          if (isUncertainFailure(error)) {
            // The server may have committed it: the same submission keeps its operation id so a retry replays.
            if (!stillOpen) return;
            this.resolveMessage.set({ kind: 'error', key: this.uncertainFailureKey(error) });
            return;
          }

          if (stillOpen) this.resolveIntent = null;
          const status = (error as { status?: number }).status;
          const code = serverErrorCode(error);
          if (status === 409) {
            this.loadCases();
            if (!stillOpen) return;
            const key = reconciliationErrorKey(code);
            this.resolveMessage.set({
              kind: 'error',
              key: key === GENERIC_ERROR_KEY ? CASE_CHANGED_KEY : key,
            });
            this.loadSelectedCase();
            return;
          }

          if (stillOpen)
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

  /**
   * Downloads every part of the support export (each at most 100 invoices and under the server's size limit, each
   * with its own checksum). Starting a download does not prove the files were kept, so the export counts for the
   * hand-off only after the user confirms saving every file (`confirmExportSaved`).
   */
  async exportBundle(): Promise<void> {
    const shopId = this.shopId();
    const deviceId = this.deviceId();
    if (!shopId || !deviceId || this.exporting()) return;

    this.exporting.set(true);
    this.exportErrorKey.set(null);
    this.pendingExport.set(null);
    try {
      const records = await this.queueDb.getAllByShopDevice(shopId, deviceId);
      const exportedAt = new Date().toISOString();
      const parts = await buildSupportBundle(records, { shopId, deviceId, exportedAt });
      if (parts.length === 0) {
        this.exportErrorKey.set('sales.offlineReconciliation.bundle.exportEmpty');
        return;
      }

      for (const part of parts) {
        this.downloads.download(new Blob([part.text], { type: 'application/json' }), part.fileName);
      }
      this.pendingExport.set({
        exportedAt,
        files: parts.map((part) => ({ fileName: part.fileName, checksum: part.checksum })),
        clientSaleIds: parts.flatMap((part) => part.clientSaleIds),
      });
    } catch {
      this.exportErrorKey.set('sales.offlineReconciliation.bundle.exportError');
    } finally {
      this.exporting.set(false);
    }
  }

  /** The user saw every file saved: from now on the hand-off counts these invoices as carried by the export. */
  async confirmExportSaved(): Promise<void> {
    const exported = this.pendingExport();
    if (!exported) return;

    this.writeLastExport(exported);
    this.lastExport.set(exported);
    this.pendingExport.set(null);
    // Brings the live queue level with what the export captured, so the hand-off compares like with like.
    await this.queueSync.refreshActiveStatusCounts().catch(() => undefined);
  }

  discardPendingExport(): void {
    this.pendingExport.set(null);
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
          this.importErrorKey.set(
            status === 413 ? BUNDLE_TOO_LARGE_KEY : reconciliationErrorKey(serverErrorCode(error)),
          );
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
      const parsed = JSON.parse(localStorage.getItem(key) ?? 'null') as
        | (Partial<SupportExportRecord> & {
            readonly fileName?: unknown;
            readonly checksum?: unknown;
          })
        | null;
      if (
        !parsed ||
        typeof parsed.exportedAt !== 'string' ||
        !Array.isArray(parsed.clientSaleIds) ||
        !parsed.clientSaleIds.every((id) => typeof id === 'string')
      ) {
        return null;
      }

      // Exports recorded before multi-file exports named their single file directly.
      const files =
        typeof parsed.fileName === 'string' && typeof parsed.checksum === 'string'
          ? [{ fileName: parsed.fileName, checksum: parsed.checksum }]
          : parsed.files;
      if (
        !Array.isArray(files) ||
        files.length === 0 ||
        !files.every(
          (file) => typeof file?.fileName === 'string' && typeof file?.checksum === 'string',
        )
      ) {
        return null;
      }

      return { exportedAt: parsed.exportedAt, files, clientSaleIds: parsed.clientSaleIds };
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
