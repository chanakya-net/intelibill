import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SESSION_PORT } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import type { Subscription } from 'rxjs';
import { AccountStatusApi, type AccountStatus, type ProviderClosure, basisLabels, formatIst, formatIstExact, stateLabels, statusReaders } from './account-status-api.service';
import { AccountCancellationDetailComponent } from './account-cancellation-detail.component';
import { ComplimentaryGrantComponent } from './complimentary-grant.component';
import { DevicePermitPanelComponent } from './device-permit-panel.component';
import { TrialExtensionComponent } from './trial-extension.component';

type Outcome = 'idle' | 'loading' | 'denied' | 'missing' | 'unavailable' | 'session';
const accountPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Read-only account access status for Support and commercial operators. The server decides redaction and
 * re-checks the live grant; this page only clears its view when grants change and drops late answers.
 */
@Component({
  selector: 'adm-account-status-page',
  imports: [ButtonDirective, RouterLink, ComplimentaryGrantComponent, TrialExtensionComponent, AccountCancellationDetailComponent,
    DevicePermitPanelComponent],
  templateUrl: './account-status-page.component.html',
  styleUrls: ['../form-page.css', './account-status-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:focus)': 'revalidate()', '(document:visibilitychange)': 'revalidate()' },
})
export class AccountStatusPageComponent {
  private readonly api = inject(AccountStatusApi);
  private readonly session = inject(SESSION_PORT);
  protected readonly mayRead = computed(() => this.session.state() === 'authenticated'
    && this.session.capabilities().some(grant => (statusReaders as readonly string[]).includes(grant)));
  /** Only a Licensing Manager is offered the grant editor; the server re-checks the live grant and recent MFA. */
  protected readonly mayGrant = computed(() => this.mayRead() && this.session.capabilities().includes('licensing-manager'));
  protected readonly accountId = signal('');
  protected readonly selected = signal<string | null>(null);
  protected readonly status = signal<AccountStatus | null>(null);
  protected readonly trialShops = computed(() => this.status()?.items
    .filter(item => item.status.basis === 'Trial')
    .flatMap(item => item.coveredShops) ?? []);
  /** Shops named by the loaded status, offered as the device panel's filter. */
  protected readonly accountShops = computed(() => [...new Map((this.status()?.items ?? [])
    .flatMap(item => item.coveredShops).map(shop => [shop.shopId, shop] as const)).values()]);
  protected readonly closures = signal<readonly ProviderClosure[] | null>(null);
  protected readonly closureUnavailable = signal(false);
  protected readonly closureStates = {
    Superseded: 'Superseded without merchant acknowledgement',
    Pending: 'Pending merchant acknowledgement', Applied: 'Acknowledged by merchant service', NeedsReview: 'Needs review',
  };
  protected readonly outcome = signal<Outcome>('idle');
  protected readonly states = stateLabels;
  protected readonly bases = basisLabels;
  protected readonly syncStates = { Pending: 'Saved, waiting for confirmation', Applied: 'Applied', NeedsReview: 'Needs review' };
  protected readonly deliveries = {
    Queued: 'Queued', Retrying: 'Retrying', AwaitingAcknowledgement: 'Awaiting acknowledgement',
    Acknowledged: 'Acknowledged', DeadLetter: 'Delivery attempts exhausted',
  };
  protected readonly hasSynchronization = computed(() => this.status()?.items
    .some(item => item.coveredShops.some(shop => shop.synchronization)));
  private request?: Subscription;
  private closureRequest?: Subscription;
  private selectedAccount?: string;
  private generation = 0;

  constructor() {
    effect(() => {
      this.session.capabilities();
      this.session.state();
      untracked(() => {
        this.selectedAccount = undefined;
        this.selected.set(null);
        this.reset();
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.request?.unsubscribe();
      this.closureRequest?.unsubscribe();
    });
  }

  protected lookup(event: Event) {
    event.preventDefault();
    const account = this.accountId().trim().toLowerCase();
    if (!accountPattern.test(account) || !this.mayRead()) return;
    this.selectedAccount = account;
    this.selected.set(account);
    this.read(account);
  }

  /** Refresh the evaluated account terms after an access change. */
  protected reload() {
    const account = this.selectedAccount;
    if (account && this.mayRead()) this.read(account);
  }

  protected revalidate() {
    const account = this.selectedAccount;
    if (!account || account !== this.accountId().trim().toLowerCase() || !this.mayRead()) return;
    if (document.visibilityState !== 'visible' || (this.request && !this.request.closed)) return;
    this.read(account);
  }

  private read(account: string) {
    this.reset();
    const generation = this.generation;
    this.outcome.set('loading');
    this.closureRequest = this.api.closures(account).subscribe({
      next: rows => {
        if (generation === this.generation) this.closures.set(rows);
      },
      error: () => {
        if (generation === this.generation) this.closureUnavailable.set(true);
      },
    });
    this.request = this.api.read(account).subscribe({
      next: status => {
        if (generation !== this.generation) return;
        this.status.set(status);
        this.outcome.set('idle');
      },
      error: (error: unknown) => {
        if (generation !== this.generation) return;
        const code = error instanceof HttpErrorResponse ? error.status : 0;
        this.outcome.set(code === 401 ? 'session' : code === 403 ? 'denied' : code === 404 ? 'missing' : 'unavailable');
      },
    });
  }

  protected instant(value: string | null) {
    return formatIst(value);
  }

  /** Courtesy (complimentary) deadlines are shown to the exact recorded second and fraction. */
  protected exact(value: string | null) {
    return formatIstExact(value);
  }

  protected rupees(paise: number | null) {
    return paise === null ? 'Not recorded'
      : new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(paise / 100);
  }

  private reset() {
    this.request?.unsubscribe();
    this.closureRequest?.unsubscribe();
    this.generation++;
    this.closures.set(null);
    this.closureUnavailable.set(false);
    this.status.set(null);
    this.outcome.set('idle');
  }
}
