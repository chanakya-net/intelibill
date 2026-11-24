import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { formatIstExact } from '../../accounts/account-status-api.service';
import { AdminSessionService } from '../../auth/admin-session.service';
import {
  canonicalAccountId, REFUND_INTAKE_STATUS_META, RefundIntakeApi, type RefundIntakeItem, type RefundIntakeStatus,
} from './refund-intake-api.service';

@Component({
  selector: 'adm-refund-intake-page',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './refund-intake-page.component.html',
  styleUrl: '../../form-page.css',
  styles: `section { overflow-wrap: anywhere; }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RefundIntakePageComponent {
  private readonly api = inject(RefundIntakeApi);
  private readonly session = inject(AdminSessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly params = toSignal(this.route.queryParamMap);

  protected readonly account = computed(() => canonicalAccountId(this.params()?.get('account') ?? '') ?? '');
  protected readonly allowed = computed(() => this.session.capabilities().includes('billing'));
  protected readonly items = signal<readonly RefundIntakeItem[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly instant = formatIstExact;
  protected readonly money = (paise: number) =>
    new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' }).format(paise / 100);
  private generation = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => { this.generation++; });
    effect(() => {
      this.account();
      this.session.adminId();
      this.session.sessionId();
      this.allowed();
      untracked(() => this.restore());
    });
  }

  protected statusLabel(status: RefundIntakeStatus): string {
    return REFUND_INTAKE_STATUS_META[status].label;
  }

  protected statusDetail(status: RefundIntakeStatus): string {
    return REFUND_INTAKE_STATUS_META[status].detail;
  }

  protected search(event: Event, account: string) {
    event.preventDefault();
    const canonical = canonicalAccountId(account);
    if (!canonical) {
      this.error.set('Enter a valid billing account ID.');
      return;
    }
    this.error.set('');
    void this.router.navigate([], { relativeTo: this.route, queryParams: { account: canonical } });
  }

  protected loadMore() {
    const cursor = this.nextCursor();
    if (!cursor || this.busy()) return;
    void this.fetchPage(cursor, true);
  }

  protected refresh() {
    this.restore();
  }

  private restore() {
    this.generation++;
    this.items.set([]);
    this.nextCursor.set(null);
    this.loaded.set(false);
    this.busy.set(false);
    this.error.set('');
    if (!this.allowed() || !this.account()) return;
    void this.fetchPage(undefined, false);
  }

  private async fetchPage(after: string | undefined, append: boolean) {
    const admin = this.session.adminId();
    const session = this.session.sessionId();
    const account = this.account();
    if (!admin || !session || !this.allowed() || !account) return;
    const generation = this.generation;
    const current = () => generation === this.generation && this.allowed() &&
      this.account() === account && this.session.adminId() === admin && this.session.sessionId() === session;
    this.busy.set(true);
    this.error.set('');
    try {
      const page = await this.api.list(account, session, after);
      if (!current()) return;
      this.items.set(append ? [...this.items(), ...page.items] : page.items);
      this.nextCursor.set(page.nextCursor);
      this.loaded.set(true);
    } catch {
      if (current()) this.error.set('Could not load refund requests for this billing account.');
    } finally {
      if (current()) this.busy.set(false);
    }
  }
}
