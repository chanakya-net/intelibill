import { TillPendingActionsComponent } from '../components/till-pending-actions.component';
import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { sameOwner } from '../../../core/context/shop-scope';
import { TillContextService } from '../services/till-context.service';
import { TillService } from '../services/till.service';
import { TillDraftService } from '../services/till-draft.service';
import { Till, TillSession, TillSessionDetail } from '../services/till.models';
@Component({
  selector: 'app-till-session-page',
  standalone: true,
  imports: [TillPendingActionsComponent, CommonModule, FormsModule, RouterLink, TranslocoPipe],
  templateUrl: './till-session-page.component.html',
  styleUrl: './till-session-page.component.scss',
})
export class TillSessionPageComponent {
  readonly context = inject(TillContextService);
  private readonly api = inject(TillService);
  private readonly auth = inject(AuthService);
  private readonly drafts = inject(TillDraftService);
  readonly manager = computed(() =>
    ['owner', 'manager'].includes(
      (
        this.auth.session()?.shops.find((x) => x.shopId === this.auth.session()?.activeShopId)
          ?.role ?? ''
      ).toLowerCase(),
    ),
  );
  readonly members = signal<
    { userId: string; firstName: string; lastName: string; status: string }[]
  >([]);
  readonly tills = signal<Till[]>([]);
  readonly history = signal<TillSession[]>([]);
  readonly detail = signal<TillSessionDetail | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly pending = signal(0);
  readonly page = signal(1);
  readonly total = signal(0);
  name = '';
  tillId = '';
  cashierId = '';
  openingFloat: number | null = null;
  amount = 0;
  reason = '';
  countedCash: number | null = null;
  overrideReason = '';
  finalizeOverdue = false;
  constructor() {
    void this.reload();
  }
  async reload() {
    const owner = this.context.owner();
    if (!owner) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.context.refresh();
      const [tills, sessions, actions, members] = await Promise.all([
        firstValueFrom(this.api.tills()),
        firstValueFrom(this.api.sessions(this.page())),
        this.drafts.list(owner),
        this.manager() ? firstValueFrom(this.api.members()) : Promise.resolve([]),
      ]);
      if (!sameOwner(owner, this.context.owner())) return;
      this.members.set(members.filter((x) => x.status === 'Active'));
      this.tills.set(tills);
      this.history.set(sessions.items);
      this.total.set(sessions.totalCount);
      this.pending.set(actions.filter((x) => x.pending).length);
      this.cashierId ||= owner.userId;
    } catch {
      if (sameOwner(owner, this.context.owner())) this.error.set('tills.loadError');
    } finally {
      this.busy.set(false);
    }
  }
  async inspect(id: string, page = 1) {
    if (this.detail()?.session.id !== id) {
      this.countedCash = null;
      this.reason = '';
      this.finalizeOverdue = false;
    }
    const owner = this.context.owner();
    this.error.set('');
    try {
      const result = await firstValueFrom(this.api.detail(id, page));
      if (sameOwner(owner, this.context.owner())) this.detail.set(result);
    } catch {
      this.error.set('tills.loadError');
    }
  }
  async act(
    action: string,
    target: string,
    cycle: string,
    payload: Record<string, unknown>,
    path: string,
  ) {
    const owner = this.context.owner();
    if (!owner || this.busy()) return;
    if (!navigator.onLine) {
      this.error.set('tills.onlineLifecycleRequired');
      return;
    }
    if (!this.context.enabled()) {
      this.error.set('tills.disabled');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.api.execute(owner, action, target, cycle, payload, path));
      if (!sameOwner(owner, this.context.owner())) return;
      if (action === 'open') this.openingFloat = null;
      if (action === 'cash-movement') {
        this.amount = 0;
        this.reason = '';
      }
      await this.reload();
      const id = this.detail()?.session.id;
      if (id) await this.inspect(id);
    } catch {
      if (sameOwner(owner, this.context.owner())) this.error.set('tills.unknown');
    } finally {
      this.busy.set(false);
    }
  }
  create() {
    if (!this.manager() || !this.name.trim()) return;
    void this.act(
      'create',
      this.context.owner()!.shopId,
      this.name.trim().toLowerCase(),
      { name: this.name.trim() },
      '/tills',
    );
  }
  open() {
    if (!this.tillId || !this.cashierId || this.openingFloat === null || this.openingFloat < 0)
      return;
    const previous = this.history().find((x) => x.tillId === this.tillId);
    void this.act(
      'open',
      this.tillId,
      previous?.id ?? crypto.randomUUID(),
      {
        cashierId: this.cashierId,
        openingFloat: this.openingFloat,
        overrideReason: this.overrideReason || null,
      },
      `/tills/${this.tillId}/sessions`,
    );
  }
  move(direction: 1 | -1) {
    const s = this.detail()?.session;
    if (!s || this.overdue(s) || !this.canOperate(s) || this.amount <= 0 || !this.reason.trim())
      return;
    void this.act(
      'cash-movement',
      s.id,
      String(s.version),
      {
        direction,
        amount: this.amount,
        reason: this.reason.trim(),
        expectedVersion: s.version,
        overrideReason: this.overrideReason || null,
      },
      `/tills/sessions/${s.id}/cash-movements`,
    );
  }
  close() {
    const s = this.detail()?.session;
    if (s && this.overdue(s) && (!this.manager() || !this.finalizeOverdue || !this.reason.trim())) {
      this.error.set('tills.overdueWarning');
      return;
    }
    if (
      !s ||
      this.countedCash === null ||
      this.countedCash < 0 ||
      (this.countedCash !== s.expectedCash && !this.reason.trim())
    ) {
      this.error.set('tills.varianceReason');
      return;
    }
    void this.act(
      'close',
      s.id,
      'close',
      {
        countedCash: this.countedCash,
        reason: this.reason.trim() || null,
        expectedVersion: s.version,
        overrideReason: this.overrideReason || null,
        finalizeOverdue: this.finalizeOverdue,
      },
      `/tills/sessions/${s.id}/close`,
    );
  }
  review() {
    const s = this.detail()?.session;
    if (!s || !this.manager() || !this.reason.trim()) return;
    void this.act(
      'review',
      s.id,
      'review',
      { reason: this.reason.trim(), expectedVersion: s.version },
      `/tills/sessions/${s.id}/review`,
    );
  }
  overdue(s: TillSession) {
    const capability = this.context.capability();
    return !!capability && s.state === 'Open' && s.businessDate !== capability.businessDate;
  }
  canOperate(s: TillSession) {
    return s.cashierId === this.context.owner()?.userId || this.manager();
  }
  changePage(delta: number) {
    this.page.update((x) => x + delta);
    void this.reload();
  }
}
