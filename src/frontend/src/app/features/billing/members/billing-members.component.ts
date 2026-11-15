import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { map } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { BillingMembersService, BillingRoster, IssueInvitation } from './billing-members.service';

@Component({
  selector: 'app-billing-members',
  imports: [RouterLink, TranslocoPipe, ButtonModule, InputTextModule],
  templateUrl: './billing-members.component.html',
  styleUrls: ['../pages/billing-accounts-page.component.scss', './billing-members.scss'],
})
export class BillingMembersComponent {
  private readonly api = inject(BillingMembersService);
  private readonly destroy = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  private readonly accountId = toSignal(inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('accountId'))));
  private view = 0;

  protected readonly roster = signal<BillingRoster | null>(null);
  protected readonly loading = signal(false);
  protected readonly submitting = signal(false);
  protected readonly pending = signal<IssueInvitation | null>(null);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly kind = signal<IssueInvitation['recipientKind']>('email');

  constructor() {
    effect(() => {
      const accountId = this.accountId();
      const user = this.auth.session()?.user.id;
      untracked(() => {
        this.view++;
        this.roster.set(null);
        this.pending.set(null);
        this.submitting.set(false);
        this.notice.set('');
        this.error.set('');
        this.kind.set('email');
        this.loading.set(false);
        if (accountId && user) this.load();
      });
    });
  }

  protected load(after?: string): void {
    const account = this.accountId();
    if (!account || (after && this.loading())) return;
    const view = ++this.view;
    this.loading.set(true);
    this.error.set('');
    this.api.roster(account, after).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (page) => {
        if (view !== this.view) return;
        this.roster.update((current) => after && current
          ? { ...page, members: [...current.members, ...page.members] }
          : page);
        this.loading.set(false);
      },
      error: () => {
        if (view !== this.view) return;
        this.roster.set(null);
        this.loading.set(false);
        this.error.set('billing.invitations.membersUnavailable');
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
    this.send(account, invitation, recipient);
  }

  private send(account: string, invitation: IssueInvitation, recipient: HTMLInputElement): void {
    const user = this.auth.session()?.user.id;
    const isCurrent = () => account === this.accountId() && user === this.auth.session()?.user.id &&
      this.pending() === invitation;
    this.api.issue(account, invitation).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: () => {
        if (!isCurrent()) return;
        this.pending.set(null);
        this.submitting.set(false);
        recipient.value = '';
        this.notice.set('billing.invitations.issued');
      },
      error: (failure: unknown) => {
        if (!isCurrent()) return;
        this.submitting.set(false);
        if (failure instanceof HttpErrorResponse && [400, 403, 409].includes(failure.status)) {
          this.pending.set(null);
          if (failure.status !== 400) this.load();
          this.error.set('billing.invitations.issueRejected');
        } else {
          this.error.set('billing.invitations.issueUnknown');
        }
      },
    });
  }
}
