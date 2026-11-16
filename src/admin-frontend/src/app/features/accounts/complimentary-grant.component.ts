import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import type { Observable, Subscription } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import { basisLabels, formatIstExact, stateLabels } from './account-status-api.service';
import {
  ComplimentaryGrantApi, type GrantCommand, type GrantDraft, type GrantPreview, type GrantResult, istInstant,
} from './complimentary-grant-api.service';
import { type PendingGrant, grantDrafts } from './complimentary-grant-drafts';

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
type Phase = 'edit' | 'previewing' | 'sending' | 'unknown' | 'done';

/**
 * Licensing Manager editor for a dated complimentary grant on the selected account. Preview is read-only; any
 * edit invalidates it. The exact confirmation is stored for this administrator and account before it is sent, and
 * an unconfirmed outcome can only be checked or retried with that original operation, never a new key.
 */
@Component({
  selector: 'adm-complimentary-grant',
  imports: [ButtonDirective],
  templateUrl: './complimentary-grant.component.html',
  styleUrls: ['../form-page.css', './account-status-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComplimentaryGrantComponent {
  private readonly api = inject(ComplimentaryGrantApi);
  private readonly session = inject(AdminSessionService);
  readonly accountId = input.required<string>();
  readonly granted = output<void>();

  protected readonly shopIds = signal('');
  protected readonly startsAt = signal('');
  protected readonly endsAt = signal('');
  protected readonly memberAllowance = signal('');
  protected readonly reason = signal('');
  protected readonly confirmed = signal(false);
  protected readonly preview = signal<GrantPreview | null>(null);
  protected readonly pending = signal<PendingGrant | null>(null);
  protected readonly result = signal<GrantResult | null>(null);
  protected readonly phase = signal<Phase>('edit');
  protected readonly error = signal('');
  protected readonly states = stateLabels;
  protected readonly bases = basisLabels;
  protected readonly busy = computed(() => this.phase() === 'previewing' || this.phase() === 'sending');
  private request?: Subscription;
  private generation = 0;

  constructor() {
    effect(() => {
      const account = this.accountId();
      const admin = this.session.adminId();
      this.session.sessionId();
      untracked(() => this.restart(account, admin));
    });
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  protected edit(field: 'shopIds' | 'startsAt' | 'endsAt' | 'memberAllowance' | 'reason', value: string) {
    this[field].set(value);
    // A preview still in flight describes the old values: drop it too, so its late answer is never confirmable.
    if (this.phase() === 'edit' || this.phase() === 'previewing') this.invalidate();
  }

  protected requestPreview(event: Event) {
    event.preventDefault();
    const draft = this.draft();
    const sessionId = this.session.sessionId();
    if (!draft || !sessionId || this.busy() || this.pending()) {
      if (!draft) this.error.set('Enter shop IDs, an IST start before an IST end, members (1 to 100000) and a reason.');
      return;
    }
    this.invalidate();
    this.phase.set('previewing');
    this.run(this.api.preview(this.accountId(), draft, sessionId), preview => {
      this.preview.set(preview);
      this.phase.set('edit');
    }, error => this.refuse(error, 'Preview'));
  }

  protected confirm() {
    const preview = this.preview();
    const adminId = this.session.adminId();
    const sessionId = this.session.sessionId();
    if (!preview || !adminId || !sessionId || !this.confirmed() || this.busy() || this.pending()) return;
    const command: GrantCommand = {
      operationKey: crypto.randomUUID(), shopIds: preview.shopIds, startsAt: preview.startsAt, endsAt: preview.endsAt,
      memberAllowance: preview.memberAllowance, reason: preview.reason, expectedVersion: preview.expectedVersion,
      sourceHash: preview.sourceHash, confirmed: true,
    };
    const pending: PendingGrant = { adminId, accountId: this.accountId(), command, createdAt: Date.now() };
    const owner = grantDrafts.acquire(pending);
    if (!owner) {
      this.error.set('This browser could not keep a copy of the grant request, so it was not sent. Free storage and try again.');
      return;
    }
    if (owner.command.operationKey !== command.operationKey) {
      this.adoptOther(owner);
      return;
    }
    this.pending.set(pending);
    this.send(this.api.confirm(pending.accountId, command, sessionId), true);
  }

  protected checkOutcome() {
    const pending = this.pending();
    const sessionId = this.session.sessionId();
    if (pending && sessionId && !this.busy())
      this.send(this.api.recover(pending.accountId, pending.command.operationKey, sessionId), false);
  }

  protected retryOriginal() {
    const pending = this.pending();
    const sessionId = this.session.sessionId();
    if (!pending || !sessionId || this.busy()) return;
    // Resend only an operation this browser still holds; another tab may have resolved or withdrawn it.
    if (!grantDrafts.holds(pending)) {
      this.restart(pending.accountId, pending.adminId);
      return;
    }
    this.send(this.api.confirm(pending.accountId, pending.command, sessionId), false);
  }

  protected instant(value: string | null) {
    return formatIstExact(value);
  }

  private send(request: Observable<GrantResult>, initial: boolean) {
    const pending = this.pending()!;
    this.phase.set('sending');
    this.error.set('');
    this.run(request, result => {
      grantDrafts.clear(pending);
      this.pending.set(null);
      this.preview.set(null);
      this.confirmed.set(false);
      this.result.set(result);
      this.phase.set('done');
      this.granted.emit();
    }, error => {
      const code = problemCode(error.error);
      // Authority and validation are checked before any write, so a 4xx to the first send is definitive.
      const definitive = initial && error.status >= 400 && error.status < 500;
      if (definitive) {
        grantDrafts.clear(pending);
        this.pending.set(null);
        this.invalidate();
        this.refuse(error, 'Grant');
        return;
      }
      this.phase.set('unknown');
      this.error.set(code === 'Grant.RecentMfaRequired'
        ? 'Sign in again with MFA, then check the original grant operation.'
        : 'The grant outcome is unconfirmed. Check the original operation; no new grant will be sent.');
    });
  }

  private run<T>(request: Observable<T>, next: (value: T) => void, failed: (error: HttpErrorResponse) => void) {
    this.request?.unsubscribe();
    const generation = ++this.generation;
    this.request = request.subscribe({
      next: value => { if (generation === this.generation) next(value); },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        failed(error instanceof HttpErrorResponse ? error : new HttpErrorResponse({ status: 0 }));
      },
    });
  }

  private refuse(error: HttpErrorResponse, action: string) {
    this.phase.set('edit');
    const code = problemCode(error.error);
    this.error.set(code === 'Grant.Overlap' ? 'A named shop already has courtesy access in part of this window.'
      : code === 'Grant.ShopNotAffiliated' ? 'Every named shop must already belong to this billing account.'
      : code === 'Grant.AlreadyEnded' ? 'A new grant must end in the future.'
      : code === 'Grant.VersionConflict' ? 'The account changed since the preview. Preview again.'
      : code === 'Grant.RecentMfaRequired' ? 'Sign in again with MFA to continue.'
      : error.status === 403 ? 'Only a Licensing Manager can grant complimentary access.'
      : error.status === 400 ? 'Check the shops, dates, member allowance and reason.'
      : `${action} could not be completed right now.`);
  }

  private draft(): GrantDraft | null {
    const shopIds = this.shopIds().split(/[\s,]+/).map(item => item.trim().toLowerCase()).filter(Boolean);
    const startsAt = istInstant(this.startsAt());
    const endsAt = istInstant(this.endsAt());
    const members = Number(this.memberAllowance());
    const reason = this.reason().trim();
    if (!shopIds.length || shopIds.length > 100 || !shopIds.every(id => uuid.test(id)) || new Set(shopIds).size !== shopIds.length
      || !startsAt || !endsAt || !Number.isInteger(members) || members < 1 || members > 100000 || !reason || reason.length > 500)
      return null;
    return { shopIds, startsAt, endsAt, memberAllowance: members, reason };
  }

  /** Another tab already sent a grant for this account: show that original operation instead of sending a second. */
  private adoptOther(other: PendingGrant) {
    this.invalidate();
    this.pending.set(other);
    this.phase.set('unknown');
    this.error.set('Another grant request for this account was already sent from another tab and is unconfirmed. '
      + 'Check that original operation; this request was not sent.');
  }

  private invalidate() {
    this.request?.unsubscribe();
    this.generation++;
    this.preview.set(null);
    this.confirmed.set(false);
    this.error.set('');
    if (this.phase() !== 'unknown') this.phase.set('edit');
  }

  private restart(account: string, admin: string | null) {
    this.request?.unsubscribe();
    this.generation++;
    for (const field of [this.shopIds, this.startsAt, this.endsAt, this.memberAllowance, this.reason]) field.set('');
    this.preview.set(null);
    this.result.set(null);
    this.confirmed.set(false);
    this.error.set('');
    const pending = admin ? grantDrafts.read(admin, account) : null;
    this.pending.set(pending);
    this.phase.set(pending ? 'unknown' : 'edit');
    if (pending) this.error.set('A grant request for this account has an unconfirmed outcome. Check the original operation.');
  }
}
