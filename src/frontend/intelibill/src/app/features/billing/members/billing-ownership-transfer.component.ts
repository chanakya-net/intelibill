import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { problemCode } from '@intelibill/api';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { finalize, type Observable } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { BillingMembersService, type BillingRoster } from './billing-members.service';
import { BillingOwnershipTransferDrafts } from './billing-ownership-transfer-drafts';
import {
  BillingOwnershipTransfers, type OwnershipDraft, type OwnershipIssue, type OwnershipOutcome, type OwnershipProposal,
} from './billing-ownership-transfer.service';

@Component({
  selector: 'app-billing-ownership-transfer',
  imports: [RouterLink, TranslocoPipe, DatePipe, NgTemplateOutlet, ButtonModule, InputTextModule, TextareaModule],
  templateUrl: './billing-ownership-transfer.component.html',
  styleUrls: ['../pages/billing-accounts-page.component.scss', './billing-members.scss'],
  host: { '(window:focus)': 'refresh()' },
})
export class BillingOwnershipTransferComponent {
  private readonly api = inject(BillingOwnershipTransfers);
  private readonly members = inject(BillingMembersService);
  private readonly drafts = inject(BillingOwnershipTransferDrafts);
  private readonly destroy = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly params = toSignal(this.route.paramMap);
  private readonly query = toSignal(this.route.queryParamMap);
  private readonly attempt = computed(() => this.query()?.get('attempt') ?? null);
  private readonly actor = computed(() => this.auth.session()?.user.id ?? '');
  protected readonly account = computed(() => this.params()?.get('accountId') ?? '');
  protected readonly transfer = computed(() => this.params()?.get('transferId') ?? '');
  protected readonly roster = signal<BillingRoster | null>(null);
  protected readonly proposal = signal<OwnershipProposal | null>(null);
  protected readonly preview = signal<Omit<OwnershipIssue, 'operationKey' | 'confirmTransfer'> | null>(null);
  protected readonly pending = signal<OwnershipDraft | null>(null);
  protected readonly outcome = signal<OwnershipOutcome | null>(null);
  protected readonly result = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);
  protected readonly refreshing = signal(false);
  protected readonly kind = signal<OwnershipIssue['recipientKind']>('email');
  private revision = 0;

  constructor() {
    effect(() => {
      const actor = this.actor();
      const target = this.account() || this.transfer();
      const key = this.attempt();
      untracked(() => this.load(actor, target, key));
    });
  }

  protected refresh(): void {
    const actor = this.actor();
    const target = this.account() || this.transfer();
    if (!actor || !target || document.hidden || this.busy() || this.refreshing() || this.result() ||
        this.pending() || this.error() === 'billing.transfer.noLongerAvailable') return;
    this.fetch(actor, target, true);
  }

  protected review(event: SubmitEvent, recipient: string, reason: string): void {
    event.preventDefault();
    const expectedVersion = this.roster()?.ownerRevision;
    if (!expectedVersion || this.busy() || this.pending() || !recipient.trim() || !reason.trim()) return;
    this.preview.set({ expectedVersion, recipientKind: this.kind(), recipient: recipient.trim(), reason: reason.trim() });
  }

  protected confirm(event: SubmitEvent, consent: boolean): void {
    event.preventDefault();
    const actor = this.actor();
    const target = this.account() || this.transfer();
    if (!actor || !target || !consent || this.busy() || this.refreshing() || this.pending()) return;
    const draft = this.createDraft(target);
    if (!draft) return;
    try {
      const existing = this.findDraft(actor, target);
      if (existing) {
        this.pending.set(existing);
        return;
      }
      this.drafts.save(actor, draft);
      this.pending.set(draft);
      this.exchange(draft, false, true);
    } catch {
      this.error.set('billing.transfer.storage');
    }
  }

  protected retry(lookup: boolean): void {
    const draft = this.pending();
    if (draft && !this.busy()) this.exchange(draft, lookup);
  }

  private createDraft(target: string): OwnershipDraft | null {
    const preview = this.preview();
    if (this.account() && preview && this.roster()?.ownerRevision === preview.expectedVersion) {
      return { action: 'issue', target, accountId: target,
        body: { ...preview, operationKey: crypto.randomUUID(), confirmTransfer: true } };
    }
    const proposal = this.proposal();
    if (proposal?.recipientActorId !== this.actor() || proposal.transferId !== target) return null;
    return { action: 'accept', target, accountId: proposal.accountId, body: {
      operationKey: crypto.randomUUID(), expectedVersion: proposal.revision,
      responsibilityPolicyVersion: proposal.responsibilityPolicyVersion, acceptResponsibility: true,
    } };
  }

  private findDraft(actor: string, target: string, key: string | null = null): OwnershipDraft | null {
    const action = this.account() ? 'issue' : 'accept';
    const drafts = this.drafts.list(actor).filter(draft => draft.target === target && draft.action === action);
    return drafts.find(draft => draft.body.operationKey === key) ?? drafts[0] ?? null;
  }

  private load(actor: string, target: string, key: string | null): void {
    this.revision++;
    this.busy.set(false);
    this.refreshing.set(false);
    this.preview.set(null);
    this.roster.set(null);
    this.proposal.set(null);
    this.pending.set(null);
    this.outcome.set(null);
    this.result.set('');
    this.error.set('');
    if (!actor || !target) return;
    try {
      this.pending.set(this.findDraft(actor, target, key));
    } catch {
      this.error.set('billing.transfer.storage');
      return;
    }
    if (!this.pending()) this.fetch(actor, target);
  }

  private fetch(actor: string, target: string, background = false): void {
    const current = this.current(actor, target, this.revision);
    const loading = background ? this.refreshing : this.busy;
    loading.set(true);
    const request: Observable<BillingRoster | OwnershipProposal> = this.account()
      ? this.members.roster(target) : this.api.preview(target);
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => {
      if (current()) loading.set(false);
    })).subscribe({
      next: view => {
        if (!current()) return;
        if (this.error() === 'billing.denied' || this.error() === 'billing.transfer.unavailable') this.error.set('');
        if ('members' in view) {
          this.roster.set(view);
          if (this.preview()?.expectedVersion !== view.ownerRevision) this.preview.set(null);
          if (!view.ownerRevision) this.error.set('billing.denied');
        } else if (view.recipientActorId === actor) {
          this.proposal.set(view);
        } else {
          this.unavailable();
        }
      },
      error: (failure: unknown) => {
        if (current() && (!background || (!this.roster() && !this.proposal()) ||
            (failure instanceof HttpErrorResponse && [401, 403, 404].includes(failure.status)))) this.unavailable();
      },
    });
  }

  private unavailable(): void {
    this.roster.set(null);
    this.proposal.set(null);
    this.preview.set(null);
    this.error.set('billing.transfer.unavailable');
  }

  private exchange(draft: OwnershipDraft, lookup: boolean, firstSubmission = false): void {
    const actor = this.actor();
    const current = this.current(actor, draft.target, this.revision);
    this.busy.set(true);
    this.error.set('');
    const call = lookup ? this.api.recover(draft) : this.api.send(draft);
    call.pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (result) => {
        if (!current()) return;
        if (result.operationKey !== draft.body.operationKey || (draft.action === 'accept' &&
            (!('transferId' in result) || result.transferId !== draft.target || result.accountId !== draft.accountId))) {
          this.unknown(draft);
          return;
        }
        this.busy.set(false);
        this.proposal.set(null);
        this.preview.set(null);
        try {
          this.drafts.settle(actor, draft);
          this.pending.set(this.findDraft(actor, draft.target));
        } catch {
          this.error.set('billing.transfer.storage');
        }
        this.result.set(draft.action === 'issue' ? 'billing.transfer.sent' : 'billing.transfer.accepted');
        if ('transferId' in result) this.outcome.set(result);
      },
      error: (failure: unknown) => {
        if (current()) this.refuse(actor, draft, lookup, firstSubmission, failure);
      },
    });
  }

  private refuse(actor: string, draft: OwnershipDraft, lookup: boolean, firstSubmission: boolean, failure: unknown): void {
    this.unknown(draft);
    if (lookup || !(failure instanceof HttpErrorResponse)) return;
    const code = problemCode(failure.error);
    const conflict = failure.status === 409 && code === 'Billing.OwnershipTransferConflict';
    if (draft.action === 'accept' && (conflict || (failure.status === 404 && code === 'Billing.OwnershipTransferMissing'))) {
      this.proposal.set(null);
      this.settleRefusal(actor, draft, 'billing.transfer.noLongerAvailable');
      return;
    }
    if (draft.action === 'issue' && firstSubmission && conflict) {
      this.preview.set(null);
      this.roster.set(null);
      this.settleRefusal(actor, draft, 'billing.transfer.reviewAgain');
      this.fetch(actor, draft.target);
      return;
    }
    if (failure.status === 403) {
      this.roster.set(null);
      this.error.set('billing.denied');
    }
    if (firstSubmission && failure.status === 400 && code === 'Billing.OwnershipTransferInvalid') {
      this.settleRefusal(actor, draft, 'billing.transfer.invalid');
    }
  }

  private settleRefusal(actor: string, draft: OwnershipDraft, message: string): void {
    try {
      this.drafts.settle(actor, draft);
      this.pending.set(this.findDraft(actor, draft.target));
      this.error.set(message);
    } catch {
      this.error.set('billing.transfer.storage');
    }
  }

  private unknown(draft: OwnershipDraft): void {
    this.busy.set(false);
    this.pending.set(draft);
    this.error.set('billing.transfer.unknown');
  }

  private current(actor: string, target: string, revision: number): () => boolean {
    return () => actor === this.actor() && target === (this.account() || this.transfer()) && revision === this.revision;
  }
}
