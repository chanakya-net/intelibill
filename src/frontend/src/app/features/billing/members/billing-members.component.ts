import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { problemCode } from '@intelibill/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { map } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { BillingAccountAuthority } from '../services/billing-account-authority.service';
import { BillingMemberRemovalDrafts, RemovalDraft } from './billing-member-removal-drafts';
import { BillingMember, BillingMembersService, BillingRoster, IssueInvitation } from './billing-members.service';

@Component({
  selector: 'app-billing-members',
  imports: [RouterLink, TranslocoPipe, ButtonModule, DialogModule, InputTextModule, TextareaModule],
  templateUrl: './billing-members.component.html',
  styleUrls: ['../pages/billing-accounts-page.component.scss', './billing-members.scss'],
  host: {
    '(window:focus)': 'revalidate()',
    '(document:visibilitychange)': 'revalidate()',
  },
})
export class BillingMembersComponent {
  private readonly api = inject(BillingMembersService);
  private readonly destroy = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly authority = inject(BillingAccountAuthority);
  private readonly drafts = inject(BillingMemberRemovalDrafts);
  private readonly accountId = toSignal(inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('accountId'))));
  private readonly user = computed(() => this.auth.session()?.user.id ?? '');
  private view = 0;

  protected readonly roster = signal<BillingRoster | null>(null);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly pending = signal<IssueInvitation | null>(null);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly kind = signal<IssueInvitation['recipientKind']>('email');
  protected readonly removing = signal<BillingMember | null>(null);
  protected readonly removalDraft = signal<RemovalDraft | null>(null);

  constructor() {
    effect(() => {
      const account = this.accountId();
      const user = this.user();
      untracked(() => {
        this.view++;
        this.clearAccount();
        this.error.set('');
        this.kind.set('email');
        if (account && user) this.load();
      });
    });
    effect(() => {
      if (this.authority.state(this.user(), this.accountId() ?? '').denied) {
        this.clearAccount();
        this.error.set('billing.denied');
      }
    });
  }

  protected revalidate(): void {
    if (!document.hidden && !this.loading() && !this.submitting()) this.load();
  }

  protected load(after?: string): void {
    const account = this.accountId();
    const user = this.user();
    if (!account || !user || (after && this.loading())) return;
    const view = ++this.view;
    const current = this.currentView(user, account);
    this.loading.set(true);
    this.error.set('');
    this.api.roster(account, after).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (page) => {
        if (view !== this.view || !current()) return;
        this.authority.confirm(user, account);
        this.roster.update((existing) => after && existing
          ? { ...page, members: [...existing.members, ...page.members] } : page);
        this.loading.set(false);
        if (!page.ownerRevision) this.clearOwnerActions();
        if (page.ownerRevision && !after && !this.submitting()) this.restoreDraft(user, account);
      },
      error: (failure: unknown) => {
        if (view !== this.view || !current()) return;
        this.loading.set(false);
        if (!this.deny(user, account, failure)) {
          this.roster.set(null);
          this.removing.set(null);
          this.error.set('billing.invitations.membersUnavailable');
        }
      },
    });
  }

  protected submit(event: SubmitEvent, recipient: HTMLInputElement): void {
    event.preventDefault();
    const account = this.accountId();
    const revision = this.roster()?.ownerRevision;
    if (!account || !revision || this.submitting() || this.loading() || !recipient.value.trim()) return;

    const invitation = this.pending() ?? {
      operationKey: crypto.randomUUID(),
      expectedOwnerRevision: revision,
      recipientKind: this.kind(),
      recipient: recipient.value.trim(),
    };
    this.pending.set(invitation);
    this.submitting.set(true);
    this.error.set('');
    this.notice.set('');
    this.sendInvitation(account, invitation, recipient);
  }

  protected confirmRemoval(event: SubmitEvent, reasonValue: string): void {
    event.preventDefault();
    const member = this.removing();
    const account = this.accountId();
    const user = this.user();
    const reason = reasonValue.trim();
    if (!member || !account || !user || !this.roster()?.ownerRevision || this.submitting() || this.loading() ||
        !reason || reason.length > 500) return;
    try {
      const existing = this.drafts.load(user, account);
      if (existing) {
        this.removalDraft.set(existing);
        this.error.set('billing.removal.retry');
      } else {
        const draft = { memberId: member.memberId, expectedVersion: member.revision, reason, operationKey: crypto.randomUUID() };
        this.drafts.save(user, account, draft);
        this.removalDraft.set(draft);
        this.sendRemoval(user, account, draft, false);
      }
      this.removing.set(null);
    } catch {
      this.error.set('billing.removal.storageUnavailable');
    }
  }

  protected retryRemoval(): void {
    const account = this.accountId();
    const user = this.user();
    const draft = this.removalDraft();
    if (account && user && draft && this.roster()?.ownerRevision && !this.submitting() && !this.loading()) {
      this.sendRemoval(user, account, draft, true);
    }
  }

  private sendRemoval(user: string, account: string, draft: RemovalDraft, retry: boolean): void {
    const current = this.currentView(user, account);
    this.submitting.set(true);
    this.error.set('');
    this.notice.set('');
    const { memberId, expectedVersion, operationKey, reason } = draft;
    this.api.remove(account, memberId, { expectedVersion, operationKey, reason })
      .pipe(takeUntilDestroyed(this.destroy)).subscribe({
        next: () => {
          if (!current()) return;
          this.submitting.set(false);
          this.settleRemoval(user, account, draft);
          this.notice.set('billing.removal.removed');
          this.load();
        },
        error: (failure: unknown) => {
          if (current()) this.removalFailed(user, account, draft, retry, failure);
        },
      });
  }

  private removalFailed(user: string, account: string, draft: RemovalDraft, retry: boolean, failure: unknown): void {
    this.submitting.set(false);
    if (this.deny(user, account, failure)) return;
    const code = failure instanceof HttpErrorResponse ? problemCode(failure.error) : null;
    if (code === 'Billing.MemberVersionConflict' || code === 'Billing.OwnerRemovalForbidden' ||
        code === 'Billing.MemberRemovalInvalid' || code === 'Billing.OperationPayloadMismatch' ||
        (!retry && code === 'Billing.MemberUnavailable')) {
      if (!this.settleRemoval(user, account, draft)) return;
      this.load();
      this.error.set('billing.removal.rejected');
    } else {
      this.error.set('billing.removal.retry');
    }
  }

  private settleRemoval(user: string, account: string, draft: RemovalDraft): boolean {
    try {
      this.drafts.settle(user, account, draft.operationKey);
      this.removalDraft.set(null);
      return true;
    } catch {
      this.error.set('billing.removal.storageUnavailable');
      return false;
    }
  }

  private restoreDraft(user: string, account: string): void {
    try {
      this.removalDraft.set(this.drafts.load(user, account));
    } catch {
      this.error.set('billing.removal.storageUnavailable');
    }
  }

  private sendInvitation(account: string, invitation: IssueInvitation, recipient: HTMLInputElement): void {
    const user = this.user();
    const current = this.currentView(user, account);
    this.api.issue(account, invitation).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => {
        if (!current() || this.pending() !== invitation) return;
        this.pending.set(null);
        this.submitting.set(false);
        recipient.value = '';
        this.notice.set('billing.invitations.issued');
      },
      error: (failure: unknown) => {
        if (!current() || this.pending() !== invitation) return;
        this.submitting.set(false);
        if (this.deny(user, account, failure)) return;
        if (failure instanceof HttpErrorResponse && [400, 409].includes(failure.status)) {
          this.pending.set(null);
          if (failure.status === 409) this.load();
          this.error.set('billing.invitations.issueRejected');
        } else {
          this.error.set('billing.invitations.issueUnknown');
        }
      },
    });
  }

  private currentView(user: string, account: string): () => boolean {
    const revision = this.authority.state(user, account).revision;
    return () => user === this.user() && account === this.accountId() &&
      revision === this.authority.state(user, account).revision;
  }

  private deny(user: string, account: string, failure: unknown): boolean {
    if (!this.authority.deny(user, account, failure)) return false;
    this.clearAccount();
    this.error.set('billing.denied');
    return true;
  }

  private clearAccount(): void {
    this.roster.set(null);
    this.loading.set(false);
    this.notice.set('');
    this.clearOwnerActions();
  }

  private clearOwnerActions(): void {
    this.pending.set(null);
    this.removing.set(null);
    this.removalDraft.set(null);
    this.submitting.set(false);
  }
}
