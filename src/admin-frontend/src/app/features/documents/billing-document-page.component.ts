import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { AdminSessionService } from '../auth/admin-session.service';
import {
  BillingDocumentApi, invoiceScopeId, type InvoiceList, type InvoicePreview,
  type InvoiceScope, type InvoiceSupersessionPreview, type InvoiceVersion,
} from './billing-document-api.service';
import { BillingDocumentDrafts, type CleanupState } from './billing-document-drafts';
import {
  CLEANUP_FAILED_MESSAGE, assertOrderSnapshotUnchanged, authorityLossMessage, classifyAuthorityLoss,
  definiteRefusals, errorCode, formatHistoryRefreshMessage, formatInvoiceMoney, formatRefusalMessage,
  isRecentMfaRequired, isReloadRefusal, reloadCodes, type AuthorityLoss,
} from './billing-document-errors';
import {
  buildSupersedeIntent, buildUploadIntent, isSupersedeIntent,
  prepareCorrection, prepareInvoice, type InvoiceIntent,
} from './billing-document-file';

type Invocation = InvoiceScope & { current: () => boolean };

@Component({
  selector: 'adm-billing-document-page',
  imports: [ButtonDirective, NgTemplateOutlet, RouterLink],
  templateUrl: './billing-document-page.component.html',
  styleUrls: ['../form-page.css', './billing-document-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BillingDocumentPageComponent {
  private readonly api = inject(BillingDocumentApi);
  private readonly drafts = inject(BillingDocumentDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly params = toSignal(this.route.queryParamMap);
  private readonly sessionHasBilling = computed(() => this.session.capabilities().includes('billing'));

  protected readonly account = computed(() => invoiceScopeId(this.params()?.get('account') ?? null));
  protected readonly order = computed(() => invoiceScopeId(this.params()?.get('order') ?? null));
  protected readonly authorityLost = signal<'none' | AuthorityLoss>('none');
  protected readonly allowed = computed(() => this.sessionHasBilling() && this.authorityLost() === 'none');
  protected readonly view = signal<InvoiceList | null>(null);
  protected readonly currentDocument = computed(() => this.view()?.documents[0] ?? null);
  protected readonly nextBeforeVersion = computed(() => {
    const docs = this.view()?.documents ?? [];
    const last = docs[docs.length - 1];
    return last && last.version > 1 ? last.version : null;
  });
  protected readonly preview = signal<InvoicePreview | null>(null);
  protected readonly correctionPreview = signal<InvoiceSupersessionPreview | null>(null);
  protected readonly correcting = signal(false);
  protected readonly pending = signal<InvoiceIntent | null>(null);
  protected readonly busy = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly historyStale = signal(false);
  protected readonly confirmedVersion = signal<InvoiceVersion | null>(null);
  protected readonly cleanupKey = signal<string | null>(null);
  protected readonly savedMessage = signal('');
  protected readonly reauthenticate = signal(false);
  protected readonly error = signal('');
  protected readonly isCorrection = isSupersedeIntent;
  protected readonly money = formatInvoiceMoney;
  private cleanup: CleanupState | null = null;
  private lastIdentity = '';
  private revision = 0;
  private generation = 0;
  private reviewedBytes: ArrayBuffer | null = null;

  constructor() {
    effect(() => {
      const admin = this.session.adminId(), sid = this.session.sessionId(), hasBilling = this.sessionHasBilling();
      const identity = `${admin ?? ''}|${sid ?? ''}|${hasBilling}`;
      this.account();
      this.order();
      untracked(() => this.syncScope(identity, Boolean(admin && sid && hasBilling), Boolean(!admin && !sid)));
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected search(event: Event, accountInput: string, orderInput: string) {
    event.preventDefault();
    const account = invoiceScopeId(accountInput), order = invoiceScopeId(orderInput);
    if (!account || !order) {
      this.error.set('Enter a valid billing account ID and accepted order ID.');
      return;
    }
    if (this.account() === account && this.order() === order) {
      if (this.historyStale()) this.refreshHistory();
      else this.restore();
    } else void this.router.navigate([], { relativeTo: this.route, queryParams: { account, order } });
  }

  protected clearPreview() {
    this.reviewedBytes = null;
    this.preview.set(null);
    this.correctionPreview.set(null);
    this.confirmed.set(false);
  }

  protected startCorrection() {
    if (!this.currentDocument() || this.pending()) return;
    this.clearPreview();
    this.savedMessage.set('');
    this.correcting.set(true);
  }

  protected cancelCorrection() {
    this.clearPreview();
    this.correcting.set(false);
  }

  protected review(event: Event, file: File | null) {
    event.preventDefault();
    if (!this.attachable()) return;
    const form = event.target as HTMLFormElement;
    this.clearPreview();
    void this.run(async scope => {
      const { bytes, metadata } = await prepareInvoice(file, form);
      await this.requestPreview(scope, () => this.api.preview(scope, metadata), result => {
        this.reviewedBytes = bytes;
        this.preview.set(result);
      });
    });
  }

  protected reviewCorrection(event: Event, file: File | null) {
    event.preventDefault();
    const target = this.currentDocument();
    if (!target || this.pending()) return;
    const form = event.target as HTMLFormElement;
    this.clearPreview();
    void this.run(async scope => {
      const { bytes, metadata, reason } = await prepareCorrection(file, form);
      await this.requestPreview(scope, () => this.api.previewSupersede(scope, target.versionId, metadata, reason), result => {
        this.reviewedBytes = bytes;
        this.correctionPreview.set(result);
      });
    });
  }

  protected attach() {
    const preview = this.preview(), bytes = this.reviewedBytes;
    if (!preview || !bytes || !this.confirmed() || !this.attachable()) return;
    this.claimAndFinish(buildUploadIntent(preview.metadata, preview.previewDigest, bytes));
  }

  protected supersede() {
    const preview = this.correctionPreview(), current = this.currentDocument(), bytes = this.reviewedBytes;
    if (!preview || !current || !bytes || !this.confirmed() || this.pending()) return;
    this.claimAndFinish(buildSupersedeIntent(preview.metadata, preview.previewDigest, bytes,
      preview.supersedesVersionId, preview.supersedesVersion, current.issuerReference, preview.reason));
  }

  protected loadOlder() {
    const before = this.nextBeforeVersion();
    if (before === null) return;
    void this.run(async scope => {
      const page = await this.api.list(scope, before);
      if (!scope.current() || this.nextBeforeVersion() !== before) return;
      this.view.update(existing => existing && { ...existing, documents: [...existing.documents, ...page.documents] });
    });
  }

  protected recover(lookup: boolean) {
    const intent = this.pending();
    if (intent) void this.run(scope => this.finish(scope, intent, lookup));
  }

  protected refreshHistory() {
    if (!this.historyStale()) return;
    void this.run(async scope => {
      if (this.cleanup && !(await this.trySyncCleanup(scope))) {
        if (scope.current()) this.error.set(CLEANUP_FAILED_MESSAGE);
        return;
      }
      if (scope.current()) await this.refreshCanonicalHistory(scope);
    });
  }

  private async requestPreview<T extends { readonly order: InvoiceList['order'] }>(
    scope: Invocation,
    fetch: () => Promise<T>,
    apply: (value: T) => void,
  ) {
    if (!scope.current()) return;
    try {
      const result = await fetch();
      if (!scope.current()) return;
      assertOrderSnapshotUnchanged(this.view()?.order, result.order);
      apply(result);
    } catch (error) {
      if (scope.current() && isReloadRefusal(error) && await this.reloadAfterConflict(scope)) return;
      throw error;
    }
  }

  private syncScope(identity: string, hasLiveBilling: boolean, signedOut: boolean) {
    if (identity !== this.lastIdentity) {
      this.lastIdentity = identity;
      if (hasLiveBilling) this.authorityLost.set('none');
      else if (this.authorityLost() === 'session' && signedOut) {
        this.generation++;
        this.clearProtectedDisplay();
        this.busy.set(false);
        return;
      }
    }
    if (this.authorityLost() !== 'none') return;
    this.restore();
  }

  private attachable() {
    return this.view()?.documents.length === 0 && !this.pending();
  }

  private claimAndFinish(command: InvoiceIntent) {
    void this.run(async scope => {
      const draft = await this.drafts.claim(scope, this.revision, command);
      if (!scope.current()) return;
      this.revision = draft.revision;
      this.pending.set(draft.pending ?? null);
      this.clearPreview();
      if (draft.pending?.operationKey === command.operationKey) await this.finish(scope, command, false);
      else if (!draft.pending) await this.load(scope);
    });
  }

  private restore() {
    this.generation++;
    this.clearProtectedDisplay();
    this.error.set('');
    this.reauthenticate.set(false);
    this.busy.set(false);
    void this.run(scope => this.load(scope));
  }

  private clearProtectedDisplay() {
    this.invalidateView();
    this.historyStale.set(false);
    this.pending.set(null);
    this.cleanup = null;
    this.cleanupKey.set(null);
  }

  private applyHistory(view: InvoiceList) {
    this.view.set(view);
    this.historyStale.set(false);
    this.confirmedVersion.set(null);
  }

  private async load(scope: Invocation) {
    const draft = await this.drafts.read(scope);
    if (!scope.current()) return;
    this.revision = draft.revision;
    try {
      const view = await this.api.list(scope);
      if (!scope.current()) return;
      this.pending.set(draft.pending ?? null);
      this.applyHistory(view);
    } catch (error) {
      if (scope.current() && !classifyAuthorityLoss(error)) this.pending.set(draft.pending ?? null);
      throw error;
    }
  }

  private async finish(scope: Invocation, intent: InvoiceIntent, lookup: boolean) {
    let saved: InvoiceVersion;
    try {
      saved = await this.api.finish(scope, intent, lookup);
    } catch (error) {
      if (await this.handleFinishFailure(scope, intent, lookup, error)) return;
      throw error;
    }
    if (!scope.current()) return;
    this.recordConfirmedWrite(saved, intent);
    if (!(await this.trySyncCleanup(scope))) {
      if (scope.current()) this.error.set(CLEANUP_FAILED_MESSAGE);
      return;
    }
    if (scope.current()) await this.refreshCanonicalHistory(scope);
  }

  private async handleFinishFailure(scope: Invocation, intent: InvoiceIntent, lookup: boolean, error: unknown) {
    const code = errorCode(error);
    if (!scope.current() || lookup || !code || !definiteRefusals.has(code)) return false;
    if (reloadCodes.has(code)) this.invalidateView();
    this.pending.set(null);
    this.cleanup = { operationKey: intent.operationKey, settled: false };
    this.cleanupKey.set(intent.operationKey);
    if (!(await this.trySyncCleanup(scope))) {
      if (scope.current()) this.invalidateView();
      return false;
    }
    if (!scope.current()) return false;
    return reloadCodes.has(code) ? this.reloadAfterConflict(scope) : false;
  }

  private recordConfirmedWrite(saved: InvoiceVersion, intent: InvoiceIntent) {
    this.invalidateView();
    this.pending.set(null);
    this.confirmedVersion.set(saved);
    this.cleanup = { operationKey: intent.operationKey, settled: false };
    this.cleanupKey.set(intent.operationKey);
    this.savedMessage.set(isSupersedeIntent(intent)
      ? 'Corrective invoice version attached.'
      : 'Accounting invoice attached.');
  }

  private invalidateView() {
    this.clearPreview();
    this.correcting.set(false);
    this.savedMessage.set('');
    this.confirmedVersion.set(null);
    this.view.set(null);
    this.historyStale.set(true);
  }

  private async trySyncCleanup(scope: Invocation) {
    if (!this.cleanup) return true;
    const synced = await this.drafts.settleAndSync(scope, this.cleanup, scope.current);
    if (!synced) return false;
    this.revision = synced.revision;
    this.pending.set(synced.pending);
    this.cleanup = null;
    this.cleanupKey.set(null);
    return true;
  }

  private async refreshCanonicalHistory(scope: Invocation) {
    try {
      const refreshed = await this.api.list(scope);
      if (scope.current()) this.applyHistory(refreshed);
    } catch (error) {
      if (scope.current()) this.refuseHistoryRefresh(error);
    }
  }

  private async reloadAfterConflict(scope: Invocation) {
    this.invalidateView();
    try {
      const refreshed = await this.api.list(scope);
      if (scope.current()) this.applyHistory(refreshed);
      return false;
    } catch (error) {
      return scope.current() && this.applyAuthorityLoss(error);
    }
  }

  private refuseHistoryRefresh(error: unknown) {
    if (this.applyAuthorityLoss(error)) return;
    const recentMfa = isRecentMfaRequired(error);
    this.reauthenticate.set(recentMfa);
    this.error.set(formatHistoryRefreshMessage(recentMfa));
  }

  private applyAuthorityLoss(error: unknown) {
    const loss = classifyAuthorityLoss(error);
    if (!loss) return false;
    this.generation++;
    this.clearProtectedDisplay();
    this.busy.set(false);
    this.authorityLost.set(loss);
    this.reauthenticate.set(loss === 'session');
    this.error.set(authorityLossMessage(loss));
    if (loss === 'session' && typeof this.session.settle === 'function') this.session.settle('ended');
    return true;
  }

  private async run(work: (scope: Invocation) => Promise<void>) {
    const admin = this.session.adminId(), session = this.session.sessionId(), account = this.account(), order = this.order();
    if (!admin || !session || !account || !order || !this.allowed() || this.busy()) return;
    const generation = this.generation;
    const current = () => generation === this.generation && this.allowed()
      && this.session.adminId() === admin && this.session.sessionId() === session && this.account() === account && this.order() === order;
    this.busy.set(true);
    this.error.set('');
    this.reauthenticate.set(false);
    try {
      await work({ admin, session, account, order, current });
    } catch (error) {
      if (current()) this.refuse(error);
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }

  private refuse(error: unknown) {
    if (this.applyAuthorityLoss(error)) return;
    const recentMfa = isRecentMfaRequired(error);
    this.reauthenticate.set(recentMfa);
    this.error.set(formatRefusalMessage(error, Boolean(this.pending()), recentMfa));
  }
}
