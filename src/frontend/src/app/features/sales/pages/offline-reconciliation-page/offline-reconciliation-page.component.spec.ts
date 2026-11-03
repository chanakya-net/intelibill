import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoTestingModule } from '@ngneat/transloco';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { AuthService } from '../../../../core/auth/auth.service';
import type { AuthSession } from '../../../../core/auth/auth.models';
import { NetworkStatusService } from '../../../../core/services/network-status.service';
import { OfflineSalesDeviceSettingsStorage } from '../../../../core/storage/offline-sales-device-settings.storage';
import { BlobDownloadService } from '../../../../shared/utils/blob-download.service';
import type { OfflineQueuedSaleRecord, OfflineSaleQueueStatus } from '../../services/offline-sale-core.types';
import type {
  ReconciliationCase,
  ReconciliationCasePage,
  ReconciliationCaseSummary,
  RecoverySubmissionResult,
} from '../../services/offline-reconciliation.models';
import { OfflineReconciliationService } from '../../services/offline-reconciliation.service';
import { OfflineSalesQueueIndexedDbService } from '../../services/offline-sales-queue-indexeddb.service';
import {
  OfflineSalesQueueSyncService,
  type OfflineAwaitingIssuerSale,
  type OfflineReviewSale,
} from '../../services/offline-sales-queue-sync.service';
import { OfflineReconciliationPageComponent } from './offline-reconciliation-page.component';

const enIN = JSON.parse(readFileSync(join(process.cwd(), 'public/assets/i18n/en-IN.json'), 'utf-8')) as Record<string, unknown>;

describe('OfflineReconciliationPageComponent', () => {
  const session = signal<AuthSession | null>(makeSession('Owner'));
  const canReachApi = signal(true);
  const reviewSales = signal<readonly OfflineReviewSale[]>([]);
  const awaitingIssuerSales = signal<readonly OfflineAwaitingIssuerSale[]>([]);
  const unsentSales = signal<readonly OfflineQueuedSaleRecord[]>([]);
  const visibleCounts = signal({ pending: 0, syncing: 0, failed: 0, warning: 0, needsReview: 0, totalVisible: 0 });

  const reconciliation = {
    listCases: vi.fn<OfflineReconciliationService['listCases']>(),
    getCase: vi.fn<OfflineReconciliationService['getCase']>(),
    resolveCase: vi.fn<OfflineReconciliationService['resolveCase']>(),
    submitRecoveryBundle: vi.fn<OfflineReconciliationService['submitRecoveryBundle']>(),
  };
  const queueSync = {
    visibleCounts,
    reviewSales,
    awaitingIssuerSales,
    unsentSales,
    retryQueuedSale: vi.fn<OfflineSalesQueueSyncService['retryQueuedSale']>(),
    refreshActiveStatusCounts: vi.fn<OfflineSalesQueueSyncService['refreshActiveStatusCounts']>(),
  };
  const queueDb = {
    getAllByShopDevice: vi.fn<OfflineSalesQueueIndexedDbService['getAllByShopDevice']>(),
  };
  const deviceSettings = {
    loadSettings: vi.fn<OfflineSalesDeviceSettingsStorage['loadSettings']>(),
  };
  const downloads = { download: vi.fn<BlobDownloadService['download']>() };

  let fixture: ComponentFixture<OfflineReconciliationPageComponent>;
  let component: OfflineReconciliationPageComponent;
  let uuidCounter = 0;

  beforeEach(() => {
    TestBed.resetTestingModule();
    vi.stubGlobal('localStorage', new MemoryStorage());
    vi.clearAllMocks();
    uuidCounter = 0;
    vi.spyOn(crypto, 'randomUUID').mockImplementation(
      () => `00000000-0000-4000-8000-${String(++uuidCounter).padStart(12, '0')}` as `${string}-${string}-${string}-${string}-${string}`,
    );

    session.set(makeSession('Owner'));
    canReachApi.set(true);
    reviewSales.set([]);
    awaitingIssuerSales.set([]);
    unsentSales.set([]);

    reconciliation.listCases.mockReturnValue(of(casePage([summary('case-1', 'INV-001'), summary('case-2', 'INV-002')], 45)));
    reconciliation.getCase.mockImplementation((caseId) => of(caseDetail({ caseId })));
    queueSync.retryQueuedSale.mockResolvedValue({
      attemptedCount: 1, syncedCount: 1, warningCount: 0, needsReviewCount: 0, failedCount: 0,
    });
    queueSync.refreshActiveStatusCounts.mockResolvedValue(visibleCounts());
    queueDb.getAllByShopDevice.mockResolvedValue([]);
    deviceSettings.loadSettings.mockReturnValue({
      shopId: 'shop-1',
      deviceId: 'device-1',
      label: 'Counter 1',
      enabled: true,
      enabledAt: null,
      enabledByUserId: null,
      enabledByUserName: null,
      lastCompleteSnapshotAt: null,
      lastApiVerifiedAt: null,
      lastSnapshotWarningMarker: null,
      lastReservedLease: null,
    });

    TestBed.configureTestingModule({
      imports: [
        OfflineReconciliationPageComponent,
        TranslocoTestingModule.forRoot({
          langs: { 'en-IN': enIN },
          translocoConfig: { defaultLang: 'en-IN', availableLangs: ['en-IN'] },
          preloadLangs: true,
        }),
      ],
      providers: [
        { provide: AuthService, useValue: { session } },
        { provide: NetworkStatusService, useValue: { canReachApi } },
        { provide: OfflineReconciliationService, useValue: reconciliation },
        { provide: OfflineSalesQueueSyncService, useValue: queueSync },
        { provide: OfflineSalesQueueIndexedDbService, useValue: queueDb },
        { provide: OfflineSalesDeviceSettingsStorage, useValue: deviceSettings },
        { provide: BlobDownloadService, useValue: downloads },
      ],
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(OfflineReconciliationPageComponent);
    component = fixture.componentInstance;
    await settle();
  }

  async function settle(): Promise<void> {
    for (let i = 0; i < 5; i += 1) {
      fixture.detectChanges();
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
    fixture.detectChanges();
  }

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function buttonByText(root: Element, text: string): HTMLButtonElement | undefined {
    return Array.from(root.querySelectorAll('button')).find((button) => button.textContent?.trim() === text) as
      | HTMLButtonElement
      | undefined;
  }

  async function openCase(caseId = 'case-1'): Promise<void> {
    component.openCase(caseId);
    await settle();
  }

  async function fillResolveForm(action: ReconciliationCase['availableActions'][number], reason: string, linked = ''): Promise<void> {
    component.resolveAction.set(action);
    component.resolveReason.set(reason);
    component.resolveLinkedOperationId.set(linked);
    await settle();
  }

  async function submitResolve(): Promise<void> {
    const submit = el().querySelector<HTMLButtonElement>('.resolve-form button[type="submit"]');
    expect(submit).toBeTruthy();
    submit!.click();
    await settle();
  }

  it('lists server cases with paging and filter', async () => {
    await render();

    expect(reconciliation.listCases).toHaveBeenCalledWith('open', 1, 20);
    const rows = el().querySelectorAll('.case-row');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('INV-001');
    expect(rows[0].textContent).toContain('Reached the server after its invoice range expired');
    expect(el().querySelector('.unresolved-count')?.textContent).toContain('3 unresolved');

    reconciliation.listCases.mockClear();
    reconciliation.listCases.mockReturnValue(of(casePage([summary('case-9', 'INV-009', { status: 'Resolved' })], 21)));
    component.onStatusFilterChange('resolved');
    await settle();
    expect(reconciliation.listCases).toHaveBeenCalledWith('resolved', 1, 20);
    expect(el().querySelector('.case-row')?.textContent).toContain('INV-009');

    component.onPageChange({ page: 1, first: 20, rows: 20 });
    await settle();
    expect(reconciliation.listCases).toHaveBeenLastCalledWith('resolved', 2, 20);

    component.onStatusFilterChange('all');
    await settle();
    expect(reconciliation.listCases).toHaveBeenLastCalledWith('all', 1, 20);
  });

  it('shows an empty state and a load error for the case list', async () => {
    reconciliation.listCases.mockReturnValue(of(casePage([], 0)));
    await render();
    expect(el().querySelector('.case-empty')?.textContent).toContain('No cases match this filter.');

    reconciliation.listCases.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    component.refreshCases();
    await settle();
    expect(el().querySelector('.cases-error')?.textContent).toContain('Review cases could not be loaded.');
  });

  it('shows only availableActions and requires a reason', async () => {
    reconciliation.getCase.mockReturnValue(of(caseDetail({ availableActions: ['LinkExistingSale', 'Escalate'] })));
    reconciliation.resolveCase.mockReturnValue(of(caseDetail({ status: 'Escalated', version: 8, availableActions: [] })));
    await render();
    await openCase();

    expect(component.actionOptions().map((option) => option.value)).toEqual(['LinkExistingSale', 'Escalate']);
    // Option labels are announced by screen readers, so they must be translated text, never raw keys.
    expect(component.actionOptions().map((option) => option.label)).toEqual(['Link to an existing sale', 'Escalate']);
    expect(component.statusFilterOptions().map((option) => option.label)).toEqual(['Open', 'Escalated', 'Resolved', 'All']);

    await fillResolveForm('Escalate', '   ');
    await submitResolve();
    expect(reconciliation.resolveCase).not.toHaveBeenCalled();
    expect(el().querySelector('.resolve-form')?.textContent).toContain('Enter a reason.');

    await fillResolveForm('Escalate', 'x'.repeat(501));
    await submitResolve();
    expect(reconciliation.resolveCase).not.toHaveBeenCalled();
    expect(el().querySelector('.resolve-form')?.textContent).toContain('Keep the reason within 500 characters.');

    await fillResolveForm('LinkExistingSale', 'Matches the posted sale');
    expect(el().querySelector('.linked-operation-input')).toBeTruthy();
    await submitResolve();
    expect(reconciliation.resolveCase).not.toHaveBeenCalled();
    expect(el().querySelector('.resolve-form')?.textContent).toContain('Enter the ID this action links to.');

    await fillResolveForm('Escalate', '  Needs the owner  ');
    expect(el().querySelector('.linked-operation-input')).toBeNull();
    await submitResolve();
    expect(reconciliation.resolveCase).toHaveBeenCalledWith('case-1', {
      operationId: '00000000-0000-4000-8000-000000000001',
      expectedVersion: 7,
      action: 'Escalate',
      reason: 'Needs the owner',
      linkedOperationId: null,
    });
    expect(el().querySelector('.case-detail .case-status')?.textContent).toContain('Escalated');
    expect(el().querySelector('.resolve-message.success')?.textContent).toContain('The server updated this case.');
  });

  it('reloads on 409 version conflict without showing success', async () => {
    reconciliation.getCase
      .mockReturnValueOnce(of(caseDetail({ version: 7, availableActions: ['Escalate'] })))
      .mockReturnValueOnce(of(caseDetail({ version: 9, availableActions: ['Escalate'], message: 'Reloaded copy' })));
    reconciliation.resolveCase.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { title: 'Reconciliation.CaseVersionConflict' } })),
    );
    await render();
    await openCase();

    await fillResolveForm('Escalate', 'Checked the drawer');
    await submitResolve();

    expect(reconciliation.getCase).toHaveBeenCalledTimes(2);
    expect(el().querySelector('.resolve-message.error')?.textContent).toContain(
      'This case changed while you were looking at it. It has been reloaded; check it and try again.',
    );
    expect(el().querySelector('.resolve-message.success')).toBeNull();
    expect(el().querySelector('.case-detail .case-status')?.textContent).toContain('Open');
    expect(el().querySelector('.case-detail')?.textContent).toContain('Reloaded copy');
    expect(component.selectedCase()?.version).toBe(9);
  });

  it('reuses the operation id when the same resolution is retried after a network failure', async () => {
    reconciliation.getCase.mockReturnValue(of(caseDetail({ availableActions: ['Escalate'] })));
    reconciliation.resolveCase
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValue(of(caseDetail({ status: 'Escalated', version: 8, availableActions: [] })));
    await render();
    await openCase();

    await fillResolveForm('Escalate', 'Checked the drawer');
    await submitResolve();
    expect(el().querySelector('.resolve-message.error')?.textContent).toContain('The server could not be reached.');
    expect(el().querySelector('.case-detail .case-status')?.textContent).toContain('Open');
    await submitResolve();
    await fillResolveForm('Escalate', 'Checked the drawer twice');
    await submitResolve();

    const operationIds = reconciliation.resolveCase.mock.calls.map(([, request]) => request.operationId);
    expect(operationIds[0]).toBe(operationIds[1]);
    expect(operationIds[2]).not.toBe(operationIds[0]);
  });

  it('names people, unknown users and legacy cases without an envelope', async () => {
    reconciliation.getCase.mockReturnValue(
      of(
        caseDetail({
          hasOriginalEnvelope: true,
          saleId: 'sale-77',
          submission: submission({
            submittedBy: { userId: 'user-9', displayName: '' },
            originalIssuer: null,
            recoveryOperationId: 'recovery-op-1',
          }),
          resolvedBy: { userId: 'user-1', displayName: 'Asha Owner' },
          events: [
            {
              action: 'Escalate',
              fromStatus: 'Open',
              toStatus: 'Escalated',
              actor: { userId: 'user-1', displayName: 'Asha Owner' },
              reason: 'Needs a second look',
              linkedOperationId: null,
              occurredAt: '2026-11-02T10:00:00.000Z',
            },
          ],
        }),
      ),
    );
    await render();
    await openCase();

    const people = el().querySelector('.people-list')?.textContent ?? '';
    expect(people).toContain('Unknown user');
    expect(people).toContain('Not recorded');
    expect(people).toContain('recovery-op-1');
    expect(people).toContain('Asha Owner');
    expect(el().querySelector('.event-list')?.textContent).toContain('Needs a second look');
    expect(el().querySelector('.snapshot-lines')?.textContent).toContain('Masala chai');
    expect(el().querySelector<HTMLAnchorElement>('.linked-sale a')?.getAttribute('href')).toBe('/sales/sale-77/print');

    reconciliation.getCase.mockReturnValue(of(caseDetail({ caseId: 'case-2', hasOriginalEnvelope: false, submission: null })));
    await openCase('case-2');
    expect(el().querySelector('.envelope-missing')?.textContent).toContain('Original envelope not recorded');
    expect(el().querySelector('.invoice-snapshot')).toBeNull();
  });

  it('hides import for staff', async () => {
    session.set(makeSession('Staff'));
    await render();
    expect(el().querySelector('.import-box')).toBeNull();
    expect(el().querySelector('.export-bundle-btn')).toBeTruthy();

    fixture.destroy();
    session.set(makeSession('Manager'));
    await render();
    expect(el().querySelector('.import-box input[type="file"]')).toBeTruthy();
  });

  it('export downloads a bundle and marks hand-off safe', async () => {
    queueDb.getAllByShopDevice.mockResolvedValue([record('own-1', 'Pending'), record('done', 'Synced')]);
    unsentSales.set([record('own-1', 'Pending')]);
    await render();

    expect(el().querySelector('.handoff-notice.unsafe')?.textContent).toContain(
      '1 invoices have not reached the server. Export a support bundle before clearing or retiring this browser.',
    );

    await component.exportBundle();
    await settle();

    expect(downloads.download).toHaveBeenCalledTimes(1);
    const [blob, fileName] = downloads.download.mock.calls[0];
    expect(fileName).toMatch(/^offline-recovery-device-1-\d{12}\.json$/);
    const text = await (blob as Blob).text();
    expect(JSON.parse(text).invoices.map((invoice: { clientSaleId: string }) => invoice.clientSaleId)).toEqual(['own-1']);
    expect(el().querySelector('.handoff-notice.safe')?.textContent).toContain('your last support export carries all 1 unsynced invoices');
    expect(el().querySelector('.last-export')?.textContent).toContain('1 invoices');

    fixture.destroy();
    await render();
    expect(el().querySelector('.handoff-notice.safe')).toBeTruthy();
  });

  it('moves a record between groups when a background sync changes it, and flags new unsynced work after export', async () => {
    const pending = record('own-1', 'Pending');
    queueDb.getAllByShopDevice.mockResolvedValue([pending]);
    unsentSales.set([pending]);
    await render();
    expect(el().querySelectorAll('.queue-group.unsent .queue-row')).toHaveLength(1);
    expect(el().querySelector('.queue-group.review')).toBeNull();

    // The app-wide sync run comes back with NeedsReview while the page is open.
    unsentSales.set([{ ...pending, status: 'NeedsReview' }]);
    reviewSales.set([
      { clientSaleId: 'own-1', invoiceNumber: 'INV-own-1', soldAt: pending.soldAt, errorCode: null, errorMessage: null, reviewCaseId: 'case-held', mismatches: [] },
    ]);
    await settle();
    expect(el().querySelector('.queue-group.unsent')).toBeNull();
    expect(el().querySelectorAll('.queue-group.review .queue-row')).toHaveLength(1);
    expect(el().querySelector('.queue-group.review')?.textContent).toContain('Review case case-held');

    await component.exportBundle();
    await settle();
    expect(el().querySelector('.handoff-notice.safe')).toBeTruthy();

    // Another tab bills a new invoice.
    unsentSales.update((rows) => [...rows, record('new-1', 'Pending')]);
    await settle();
    expect(el().querySelector('.handoff-notice.safe')).toBeNull();
    expect(el().querySelector('.handoff-notice.unsafe')).toBeTruthy();
  });

  describe.each([
    ['success', () => of(caseDetail({ caseId: 'case-1', status: 'Escalated', version: 8, availableActions: [] }))],
    ['409 conflict', () => throwError(() => new HttpErrorResponse({ status: 409, error: { title: 'Reconciliation.CaseVersionConflict' } }))],
  ])('a %s resolve reply that arrives after another case was opened', (_name, reply) => {
    it('leaves the open case, its form and banner alone', async () => {
      const inFlight = new Subject<ReconciliationCase>();
      reconciliation.getCase.mockImplementation((caseId) => of(caseDetail({ caseId, availableActions: ['Escalate'] })));
      reconciliation.resolveCase.mockReturnValue(inFlight);
      await render();
      await openCase('case-1');
      await fillResolveForm('Escalate', 'Checked the drawer');
      await submitResolve();
      expect(reconciliation.resolveCase).toHaveBeenCalledTimes(1);

      await openCase('case-2');
      component.resolveReason.set('Typed for case two');
      await settle();
      reconciliation.getCase.mockClear();

      const outcome = reply();
      outcome.subscribe({ next: (value) => inFlight.next(value), error: (error) => inFlight.error(error) });
      await settle();

      expect(component.selectedCaseId()).toBe('case-2');
      expect(component.selectedCase()?.caseId).toBe('case-2');
      expect(component.resolveReason()).toBe('Typed for case two');
      expect(component.resolveMessage()).toBeNull();
      expect(reconciliation.getCase).not.toHaveBeenCalled();
    });
  });

  it('local NeedsReview row opens its server case', async () => {
    reviewSales.set([
      {
        clientSaleId: 'held-1',
        invoiceNumber: 'INV-HELD',
        soldAt: '2026-11-01T10:00:00.000Z',
        errorCode: 'OfflineReconciliation.HistoricalLeaseReviewRequired',
        errorMessage: null,
        reviewCaseId: 'case-held',
        mismatches: [],
      },
    ]);
    await render();

    const row = el().querySelector('.queue-group.review .queue-row')!;
    expect(row.textContent).toContain('INV-HELD');
    expect(row.querySelector<HTMLAnchorElement>('a.print-link')?.getAttribute('href')).toBe('/sales/held-1/print?offline=1');
    buttonByText(row, 'Open case')!.click();
    await settle();

    expect(reconciliation.getCase).toHaveBeenCalledWith('case-held');
    expect(component.selectedCaseId()).toBe('case-held');
    expect(el().querySelector('.case-detail')).toBeTruthy();
  });

  it('awaiting-issuer rows are not retried', async () => {
    unsentSales.set([
      record('own-failed', 'Failed', 'user-1'),
      record('other-pending', 'Pending', 'user-2'),
    ]);
    awaitingIssuerSales.set([
      { clientSaleId: 'other-pending', invoiceNumber: 'INV-other-pending', soldAt: '2026-11-01T10:00:00.000Z', issuedByUserId: 'user-2', status: 'Pending' },
    ]);
    await render();

    const unsentRows = el().querySelectorAll('.queue-group.unsent .queue-row');
    expect(unsentRows).toHaveLength(1);
    expect(unsentRows[0].textContent).toContain('INV-own-failed');

    const awaitingRow = el().querySelector('.queue-group.awaiting .queue-row')!;
    expect(awaitingRow.textContent).toContain('user-2');
    expect(buttonByText(awaitingRow, 'Retry')).toBeUndefined();
    expect(awaitingRow.querySelector<HTMLAnchorElement>('a.print-link')?.getAttribute('href')).toBe(
      '/sales/other-pending/print?offline=1',
    );

    buttonByText(unsentRows[0], 'Retry')!.click();
    await settle();
    expect(queueSync.retryQueuedSale).toHaveBeenCalledTimes(1);
    expect(queueSync.retryQueuedSale).toHaveBeenCalledWith('shop-1', 'device-1', 'own-failed');
  });

  it('imports a bundle and lists per-invoice results with their cases', async () => {
    const result: RecoverySubmissionResult = {
      operationId: 'x',
      results: [
        { clientSaleId: 'r-1', invoiceNumber: 'INV-R1', status: 'ReviewOpened', caseId: 'case-r1', saleId: null, errors: [] },
        {
          clientSaleId: 'r-2',
          invoiceNumber: 'INV-R2',
          status: 'Rejected',
          caseId: null,
          saleId: null,
          errors: [{ code: 'Recovery.LeaseNotOwned', message: 'server text' }],
        },
      ],
    };
    reconciliation.submitRecoveryBundle
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValue(of(result));
    await render();

    await component.onImportFileSelected(fileEvent('bundle.json', '{"format":"intelibill.offline-recovery"}'));
    await settle();
    component.submitImport();
    await settle();
    expect(el().querySelector('.import-error')?.textContent).toContain('The server could not be reached.');

    await component.onImportFileSelected(fileEvent('bundle.json', '{"format":"intelibill.offline-recovery"}'));
    await settle();
    component.submitImport();
    await settle();

    const calls = reconciliation.submitRecoveryBundle.mock.calls;
    expect(calls).toHaveLength(2);
    expect(calls[0][0]).toBe('{"format":"intelibill.offline-recovery"}');
    expect(calls[1][1]).toBe(calls[0][1]);

    const rows = el().querySelectorAll('.import-result');
    expect(rows).toHaveLength(2);
    expect(rows[1].textContent).toContain('The invoice number was not reserved for the bundle');
    buttonByText(rows[0], 'Open case')!.click();
    await settle();
    expect(reconciliation.getCase).toHaveBeenCalledWith('case-r1');
  });

  it('maps a rejected bundle to a translated message', async () => {
    reconciliation.submitRecoveryBundle.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: { title: 'Recovery.BundleChecksumMismatch' } })),
    );
    await render();
    await component.onImportFileSelected(fileEvent('bundle.json', '{}'));
    component.submitImport();
    await settle();
    expect(el().querySelector('.import-error')?.textContent).toContain("The bundle's invoices do not match its checksum.");
  });

  it('offline: actions disabled and no resolution shown', async () => {
    reconciliation.getCase.mockReturnValue(of(caseDetail({ availableActions: ['Escalate'] })));
    await render();
    await openCase();
    await component.onImportFileSelected(fileEvent('bundle.json', '{}'));
    await fillResolveForm('Escalate', 'Checked the drawer');

    canReachApi.set(false);
    await settle();

    expect(el().querySelector('.stale-notice')?.textContent).toContain('The server cannot be reached.');
    expect(el().querySelector<HTMLButtonElement>('.resolve-form button[type="submit"]')?.disabled).toBe(true);
    expect(el().querySelector<HTMLButtonElement>('.refresh-cases-btn button, button.refresh-cases-btn')?.disabled).toBe(true);
    expect(el().querySelector<HTMLButtonElement>('.submit-import-btn button, button.submit-import-btn')?.disabled).toBe(true);

    component.submitResolve();
    component.submitImport();
    component.refreshCases();
    await settle();

    expect(reconciliation.resolveCase).not.toHaveBeenCalled();
    expect(reconciliation.submitRecoveryBundle).not.toHaveBeenCalled();
    expect(reconciliation.listCases).toHaveBeenCalledTimes(1);
    expect(el().querySelector('.case-detail .case-status')?.textContent).toContain('Open');
    expect(el().querySelector('.resolve-message.success')).toBeNull();
    expect(el().querySelector<HTMLButtonElement>('.export-bundle-btn button, button.export-bundle-btn')).toBeTruthy();
  });
});

class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>();

  get length(): number {
    return this.values.size;
  }

  clear(): void {
    this.values.clear();
  }

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.values.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }

  setItem(key: string, value: string): void {
    this.values.set(key, String(value));
  }
}

function makeSession(role: 'Owner' | 'Manager' | 'Staff'): AuthSession {
  return {
    accessToken: 'token',
    refreshToken: 'refresh',
    accessTokenExpiresAt: '2099-01-01T00:00:00.000Z',
    refreshTokenExpiresAt: '2099-01-01T00:00:00.000Z',
    rememberMe: true,
    user: { id: 'user-1', email: 'a@b.com', phoneNumber: null, firstName: 'Test', lastName: 'User', language: 'en-IN' },
    activeShopId: 'shop-1',
    shops: [{ shopId: 'shop-1', shopName: 'Main', role, isDefault: true, lastUsedAt: null }],
  };
}

function summary(
  caseId: string,
  invoiceNumber: string,
  overrides: Partial<ReconciliationCaseSummary> = {},
): ReconciliationCaseSummary {
  return {
    caseId,
    version: 7,
    invoiceNumber,
    clientSaleId: `client-${caseId}`,
    deviceId: 'device-1',
    issueType: 'IssueTimingReview',
    status: 'Open',
    reasonCodes: ['OfflineReconciliation.HistoricalLeaseReviewRequired'],
    message: 'Server message',
    saleId: null,
    resolution: null,
    createdAt: '2026-11-02T09:00:00.000Z',
    createdBy: { userId: 'user-1', displayName: 'Test User' },
    resolvedAt: null,
    ...overrides,
  };
}

function casePage(items: readonly ReconciliationCaseSummary[], totalCount: number): ReconciliationCasePage {
  return { items, page: 1, pageSize: 20, totalCount, unresolvedCount: 3 };
}

function submission(overrides: Partial<NonNullable<ReconciliationCase['submission']>> = {}): NonNullable<ReconciliationCase['submission']> {
  return {
    submissionId: 'sub-1',
    source: 'Recovery',
    claimedSoldAt: '2026-11-01T10:00:00.000Z',
    receivedAt: '2026-11-02T09:00:00.000Z',
    payloadHash: 'hash',
    originalLeaseId: 'lease-1',
    submittedBy: { userId: 'user-1', displayName: 'Test User' },
    assertedIssuer: null,
    originalIssuer: { userId: 'user-2', displayName: 'Ravi Cashier' },
    recoveryOperationId: null,
    invoice: {
      invoiceNumber: 'INV-001',
      soldAt: '2026-11-01T10:00:00.000Z',
      customerName: null,
      customerPhone: null,
      paymentMethod: 'Cash',
      subtotalBeforeDiscount: 100,
      totalDiscountAmount: 0,
      totalTaxAmount: 5,
      totalAmount: 105,
      paidAmount: 105,
      dueAmount: 0,
      lines: [
        {
          itemName: 'Masala chai',
          barcode: 'CHAI-1',
          batchNumber: 'B-1',
          quantity: 2,
          salesPrice: 50,
          taxRatePercent: 5,
          taxAmount: 5,
          totalAmount: 105,
        },
      ],
    },
    ...overrides,
  };
}

function caseDetail(overrides: Partial<ReconciliationCase> = {}): ReconciliationCase {
  return {
    ...summary(overrides.caseId ?? 'case-1', 'INV-001'),
    itemId: null,
    inventoryBatchId: null,
    printedQuantity: null,
    availableQuantity: null,
    consumedQuantity: null,
    unresolvedQuantity: null,
    resolutionReason: null,
    linkedOperationId: null,
    resolutionStockTransactionId: null,
    resolvedBy: null,
    hasOriginalEnvelope: true,
    submission: submission(),
    availableActions: ['RetryHistoricalLease', 'LinkExistingSale', 'Escalate'],
    events: [],
    ...overrides,
  };
}

function record(clientSaleId: string, status: OfflineSaleQueueStatus, issuedByUserId?: string): OfflineQueuedSaleRecord {
  const soldAt = '2026-11-01T10:00:00.000Z';
  return {
    key: `shop-1::device-1::${clientSaleId}`,
    shopId: 'shop-1',
    deviceId: 'device-1',
    clientSaleId,
    idempotencyKey: `idem-${clientSaleId}`,
    invoiceNumber: `INV-${clientSaleId}`,
    soldAt,
    payload: {
      clientSaleId,
      idempotencyKey: `idem-${clientSaleId}`,
      shopId: 'shop-1',
      deviceId: 'device-1',
      invoiceNumber: `INV-${clientSaleId}`,
      soldAt,
      pricing: {
        lines: [],
        totals: { totalBeforeDiscount: 100, totalDiscount: 0, totalTax: 0, grandTotal: 100, paidAmount: 100, dueAmount: 0 },
      },
      paymentMethod: 1,
      customerId: null,
      customerName: null,
      customerPhone: null,
      ...(issuedByUserId ? { issuedByUserId } : {}),
    },
    status,
    warnings: [],
    errorCode: null,
    errorMessage: null,
    serverSaleId: null,
    createdAt: soldAt,
    updatedAt: soldAt,
    syncAttemptCount: 0,
    lastSyncAttemptAt: null,
    syncAttempts: [],
  };
}

function fileEvent(name: string, text: string): Event {
  const input = document.createElement('input');
  input.type = 'file';
  const file = new File([text], name, { type: 'application/json' });
  Object.defineProperty(input, 'files', { value: [file] });
  return { target: input } as unknown as Event;
}
