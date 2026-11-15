import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';

import { BillingAccount } from '../services/billing-account.models';
import { BillingInvitation, BillingMembersService } from './billing-members.service';

@Component({
  selector: 'app-billing-inbox',
  imports: [RouterLink, TranslocoPipe, DatePipe, ButtonModule],
  templateUrl: './billing-inbox.component.html',
  styleUrls: ['../pages/billing-accounts-page.component.scss', './billing-members.scss'],
})
export class BillingInboxComponent implements OnInit {
  private readonly api = inject(BillingMembersService);
  private readonly destroy = inject(DestroyRef);

  protected readonly invitations = signal<readonly BillingInvitation[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly accepting = signal<string | null>(null);
  protected readonly accepted = signal<BillingAccount | null>(null);
  protected readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected load(after?: string): void {
    if (this.loading() || this.accepting()) return;
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api.inbox(after).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: ({ invitations, nextCursor }) => {
        this.invitations.update((items) => after ? [...items, ...invitations] : invitations);
        this.nextCursor.set(nextCursor);
        this.loading.set(false);
      },
      error: () => {
        this.loadFailed.set(true);
        this.loading.set(false);
      },
    });
  }

  protected accept(invitation: BillingInvitation): void {
    if (this.loading() || this.accepting()) return;
    this.accepting.set(invitation.invitationId);
    this.error.set('');
    this.api.accept(invitation).pipe(takeUntilDestroyed(this.destroy)).subscribe({
      next: (account) => {
        this.accepted.set(account);
        this.accepting.set(null);
        this.load();
      },
      error: (failure: unknown) => {
        this.accepting.set(null);
        this.error.set(failure instanceof HttpErrorResponse && failure.status === 404
          ? 'billing.invitations.invitationUnavailable' : 'billing.invitations.acceptUnknown');
        this.load();
      },
    });
  }
}
