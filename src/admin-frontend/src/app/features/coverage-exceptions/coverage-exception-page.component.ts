import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { formatIstExact } from '../accounts/account-status-api.service';
import { AdminSessionService } from '../auth/admin-session.service';
import {
  CoverageExceptionApi, normalizeReviewReason, type ApproveCoverageExceptionCommand,
  type CoverageExceptionApproval, type CoverageExceptionPreview, type CoverageExceptionSummary,
} from './coverage-exception-api.service';
import { CoverageExceptionDrafts } from './coverage-exception-drafts';

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const RELEASING_CODES = new Set([
  'Coverage.ExceptionChanged', 'Coverage.ExceptionConsentsRequired', 'Coverage.ExceptionInvalid',
]);

interface RunScope {
  readonly admin: string;
  readonly session: string;
  readonly account: string;
  readonly exceptionId: string;
  readonly current: () => boolean;
}

@Component({
  selector: 'adm-coverage-exception-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './coverage-exception-page.component.html',
  styleUrl: '../form-page.css',
  styles: `
    section, dd { overflow-wrap: anywhere; }
    textarea { inline-size: 100%; border: 1px solid var(--app-sand-400); border-radius: 0.5rem; padding: 0.5rem; }
    .exc-list { display: grid; gap: 0.75rem; list-style: none; padding: 0; margin: 0; }
    .exc-row { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 0.75rem; }
    .exc-check { display: flex; gap: 0.5rem; align-items: flex-start; cursor: pointer; }
    @media (min-width: 40rem) { dl { display: grid; grid-template-columns: 15rem minmax(0, 1fr); gap: 0.5rem 1rem; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoverageExceptionPageComponent {
  private readonly api = inject(CoverageExceptionApi);
  private readonly drafts = inject(CoverageExceptionDrafts);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot.paramMap });
  private readonly query = toSignal(this.route.queryParamMap, { initialValue: this.route.snapshot.queryParamMap });

  protected readonly allowed = computed(() => this.session.capabilities().includes('licensing-manager'));
  protected readonly accountInput = signal('');
  protected readonly account = signal('');
  protected readonly selectedId = signal('');
  protected readonly items = signal<readonly CoverageExceptionSummary[]>([]);
  protected readonly preview = signal<CoverageExceptionPreview | null>(null);
  protected readonly pending = signal<ApproveCoverageExceptionCommand | null>(null);
  protected readonly receipt = signal<CoverageExceptionApproval | null>(null);
  protected readonly reason = signal('');
  protected readonly confirmed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly reauthenticate = signal(false);
  protected readonly instant = formatIstExact;

  protected readonly canSubmit = computed(() => {
    const p = this.preview();
    return !!p?.canApprove && !this.pending() && !this.busy()
      && !!normalizeReviewReason(this.reason()) && this.confirmed();
  });

  private generation = 0;
  private revision = 0;

  constructor() {
    effect(() => {
      const fromParam = this.params().get('accountId') ?? this.query().get('account') ?? '';
      const fromException = this.query().get('exception') ?? '';
      this.session.adminId();
      this.session.sessionId();
      this.allowed();
      untracked(() => this.init(fromParam, fromException));
    });
    inject(DestroyRef).onDestroy(() => this.generation++);
  }

  protected lookup(event: Event) {
    event.preventDefault();
    const clean = this.accountInput().trim().toLowerCase();
    if (!UUID_RE.test(clean)) {
      this.error.set('Enter a valid billing account UUID.');
      return;
    }
    this.account.set(clean);
    this.selectedId.set('');
    this.clearDetail();
    void this.run(clean, '', scope => this.loadList(scope));
  }

  protected select(exceptionId: string) {
    const acc = this.account();
    if (!acc || !UUID_RE.test(exceptionId)) return;
    this.selectedId.set(exceptionId);
    this.clearDetail();
    void this.run(acc, exceptionId, scope => this.loadPreview(scope));
  }

  protected refresh() {
    const acc = this.account();
    if (!acc) return;
    const id = this.selectedId();
    void this.run(acc, id, async scope => {
      await this.loadList(scope);
      if (id && scope.current()) await this.loadPreview(scope);
    });
  }

  protected editReason(event: Event) {
    this.reason.set((event.target as HTMLTextAreaElement).value);
  }

  protected toggleConfirm(event: Event) {
    this.confirmed.set((event.target as HTMLInputElement).checked);
  }

  protected approve() {
    const p = this.preview();
    const cleanReason = normalizeReviewReason(this.reason());
    if (!p?.canApprove || !cleanReason || !this.confirmed() || this.pending() || this.busy()) return;
    const command: ApproveCoverageExceptionCommand = {
      operationKey: crypto.randomUUID(),
      expectedProposalRevision: p.proposalRevision,
      expectedSourceHash: p.sourceHash,
      expectedTermsDigest: p.termsDigest,
      expectedGrantVersion: p.grantScopeVersion,
      disposition: p.overlapDisposition,
      reason: cleanReason,
      acknowledgedPermitsAndUnknownQueues: true,
      confirmed: true,
    };
    void this.run(p.accountId, p.exceptionId, async scope => {
      const draft = await this.drafts.claim(this.draftScope(scope), this.revision, command);
      if (!scope.current()) return;
      this.revision = draft.revision;
      this.pending.set(draft.pending ?? null);
      if (draft.pending?.operationKey === command.operationKey)
        await this.dispatch(scope, command, false);
    });
  }

  protected resolvePending(lookupOnly: boolean) {
    const command = this.pending();
    const acc = this.account();
    const id = this.selectedId();
    if (!command || !acc || !id) return;
    void this.run(acc, id, scope => this.dispatch(scope, command, lookupOnly));
  }

  protected activationLabel(value: string): string {
    switch (value) {
      case 'Acknowledged': return 'Acknowledged by merchant service';
      case 'Applying': return 'Applying (awaiting signed merchant acknowledgement)';
      case 'Waiting': return 'Waiting for cutover boundary';
      case 'TermEnded': return 'Window ended';
      case 'NotApplicable': return 'Not applicable';
      case 'NeedsReview': return 'Needs review';
      default: return value;
    }
  }

  private init(rawAccount: string, rawException: string) {
    this.generation++;
    this.items.set([]);
    this.clearDetail();
    this.error.set('');
    this.reauthenticate.set(false);
    const cleanAcc = rawAccount.trim().toLowerCase();
    const cleanExc = rawException.trim().toLowerCase();
    if (UUID_RE.test(cleanAcc)) {
      this.accountInput.set(cleanAcc);
      this.account.set(cleanAcc);
      const exc = UUID_RE.test(cleanExc) ? cleanExc : '';
      this.selectedId.set(exc);
      if (this.allowed()) {
        void this.run(cleanAcc, exc, async scope => {
          await this.loadList(scope);
          if (exc && scope.current()) await this.loadPreview(scope);
        });
      }
    }
  }

  private clearDetail() {
    this.preview.set(null);
    this.pending.set(null);
    this.receipt.set(null);
    this.reason.set('');
    this.confirmed.set(false);
  }

  private async loadList(scope: RunScope) {
    const listed = await this.api.list(scope.account, scope.session);
    if (!scope.current()) return;
    this.items.set(listed.items);
  }

  private async loadPreview(scope: RunScope) {
    const draft = await this.drafts.read(this.draftScope(scope));
    if (!scope.current()) return;
    this.revision = draft.revision;
    this.pending.set(draft.pending ?? null);
    const prev = await this.api.preview(scope.account, scope.exceptionId, scope.session);
    if (!scope.current()) return;
    this.preview.set(prev);
    if (prev.approval) this.receipt.set(prev.approval);
  }

  private async dispatch(scope: RunScope, command: ApproveCoverageExceptionCommand, lookupOnly: boolean) {
    let saved: CoverageExceptionApproval;
    try {
      saved = lookupOnly
        ? await this.api.recover(scope.account, scope.exceptionId, command.operationKey, scope.session)
        : await this.api.approve(scope.account, scope.exceptionId, scope.session, command);
    } catch (err) {
      if (scope.current()) await this.handleError(scope, err, !lookupOnly);
      return;
    }
    if (!scope.current()) return;
    if (saved.operationKey !== command.operationKey || saved.adminId !== scope.admin
      || saved.disposition !== command.disposition || saved.reason !== command.reason) {
      throw new Error('Approval response identity does not match frozen command');
    }
    await this.drafts.settle(this.draftScope(scope), command.operationKey);
    if (!scope.current()) return;
    this.revision++;
    this.pending.set(null);
    this.receipt.set(saved);
    this.confirmed.set(false);
    await this.loadPreview(scope);
    if (scope.current()) await this.loadList(scope);
  }

  private async run(account: string, exceptionId: string, work: (scope: RunScope) => Promise<void>) {
    const admin = this.session.adminId();
    const session = this.session.sessionId();
    if (!admin || !session || !this.allowed() || this.busy()) return;
    const gen = this.generation;
    const scope: RunScope = {
      admin, session, account, exceptionId,
      current: () => gen === this.generation && this.allowed() && this.account() === account
        && this.session.adminId() === admin && this.session.sessionId() === session,
    };
    this.busy.set(true);
    this.error.set('');
    this.reauthenticate.set(false);
    try {
      await work(scope);
    } catch (err) {
      if (scope.current()) await this.handleError(scope, err, false);
    } finally {
      if (scope.current()) this.busy.set(false);
    }
  }

  private async handleError(scope: RunScope, err: unknown, wasApprovePost: boolean) {
    const code = err instanceof HttpErrorResponse ? problemCode(err.error) : null;
    const pending = this.pending();
    if (wasApprovePost && pending && code && RELEASING_CODES.has(code)) {
      await this.drafts.settle(this.draftScope(scope), pending.operationKey);
      if (!scope.current()) return;
      this.revision++;
      this.pending.set(null);
      this.confirmed.set(false);
    }
    if (!scope.current()) return;
    this.reauthenticate.set(code === 'Coverage.ExceptionRecentMfaRequired');
    this.error.set(this.messageFor(code, !!this.pending()));
  }

  private messageFor(code: string | null, hasPending: boolean): string {
    switch (code) {
      case 'Coverage.ExceptionRecentMfaRequired':
        return 'Recent MFA verification is required before approving or checking this exception. Your original operation key is preserved.';
      case 'Coverage.ExceptionAccessDenied':
        return 'Only a Licensing Manager can review or approve early coverage-replacement exceptions.';
      case 'Coverage.ExceptionChanged':
        return 'The exception, consents, or account state changed since preview. Refresh and review again.';
      case 'Coverage.ExceptionConsentsRequired':
        return 'This exception is no longer ready for approval. Review the current consent and permit status.';
      case 'Coverage.ExceptionPayloadMismatch':
        return 'This operation key was already used with different approval details.';
      case 'Coverage.ExceptionNotFound':
        return hasPending
          ? 'No saved approval was found for this key yet. Your original key is kept so you can retry safely.'
          : 'No matching coverage-replacement exception was found for this account.';
      default:
        return hasPending
          ? 'The approval outcome is unknown. Check the saved approval or retry using the preserved original key.'
          : 'Coverage-replacement review is temporarily unavailable. Try again.';
    }
  }

  private draftScope(scope: RunScope) {
    return { admin: scope.admin, account: scope.account, order: scope.exceptionId };
  }
}
