import { TillVerificationStore } from './till-verification-store';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { ownerOfSession, sameOwner, ScopeOwner } from '../../../core/context/shop-scope';
import { TillService } from './till.service';
import { TillCapabilities, TillSession, TenderLine, TillPostingContext } from './till.models';
@Injectable({ providedIn: 'root' })
export class TillContextService {
  private readonly auth = inject(AuthService);
  private readonly api = inject(TillService);
  private readonly verification = inject(TillVerificationStore);
  private verifiedSelection: Promise<boolean> = Promise.resolve(true);
  private restored = false;
  private loadedOwner: ScopeOwner | null = null;
  readonly capability = signal<TillCapabilities | null>(null);
  readonly selected = signal<TillSession | null>(null);
  readonly sessions = signal<TillSession[]>([]);
  readonly error = signal(false);
  readonly loading = signal(false);
  readonly owner = computed(() => ownerOfSession(this.auth.session()));
  readonly enabled = computed(() => this.capability()?.enabled === true);
  constructor() {
    effect(() => {
      const owner = this.owner();
      if (!sameOwner(owner, this.loadedOwner)) {
        this.loadedOwner = owner;
        this.capability.set(null);
        this.selected.set(null);
        this.sessions.set([]);
        this.error.set(false);
        this.restored = false;
        this.overrideReason.set('');
        if (owner?.shopId) void this.refresh();
      }
    });
  }
  async refresh() {
    const owner = this.owner();
    if (!owner?.shopId) return;
    this.loading.set(true);
    this.error.set(false);
    try {
      if (!this.restored) {
        try {
          const stored = await this.verification.load(owner);
          if (stored && sameOwner(owner, this.owner())) {
            this.capability.set(stored.capability);
            this.selected.set(stored.session);
            this.sessions.set(stored.session ? [stored.session] : []);
          }
        } catch {}
        this.restored = true;
      }
      const cap = await firstValueFrom(this.api.capabilities());
      if (!sameOwner(owner, this.owner())) return;
      this.capability.set(cap);
      if (cap.enabled) {
        let pageNumber = 1;
        let page = await firstValueFrom(this.api.sessions(pageNumber, 'Open'));
        const rows = [...page.items];
        while (rows.length < page.totalCount && page.items.length > 0) {
          if (!sameOwner(owner, this.owner())) return;
          page = await firstValueFrom(this.api.sessions(++pageNumber, 'Open'));
          rows.push(...page.items);
        }
        if (!sameOwner(owner, this.owner())) return;
        const sessions = [...new Map(rows.map((x) => [x.id, x])).values()];
        this.sessions.set(sessions);
        const old = this.selected();
        this.selected.set(sessions.find((x) => x.id === old?.id) ?? null);
      }
      await this.persistVerified(owner);
    } catch {
      if (sameOwner(owner, this.owner())) this.error.set(true);
    } finally {
      if (sameOwner(owner, this.owner())) this.loading.set(false);
    }
  }
  select(id: string) {
    this.selected.set(this.sessions().find((x) => x.id === id) ?? null);
    const owner = this.owner();
    if (owner)
      this.verifiedSelection = this.persistVerified(owner).then(
        () => true,
        () => false,
      );
  }
  private async persistVerified(owner: ScopeOwner) {
    const cap = this.capability();
    if (cap && sameOwner(owner, this.owner()))
      await this.verification.save({
        ...owner,
        capability: structuredClone(cap),
        session: structuredClone(this.selected()),
        verifiedAt: new Date().toISOString(),
      });
  }
  async offlineEvidence(method: number, paid: number) {
    const owner = this.owner();
    const frozen = structuredClone(this.saleEvidence(method, paid, true));
    const verification = this.verifiedSelection;
    const durable = await verification;
    if (!durable) throw new Error('tills.capabilityUnknown');
    if (!sameOwner(owner, this.owner())) throw new Error('tills.capabilityUnknown');
    return frozen;
  }
  posting(
    offline = false,
  ): { till: TillPostingContext; tillBusinessDate: string; tillCashierId: string } | null {
    if (!this.capability() || (!offline && this.error()))
      throw new Error('tills.capabilityUnknown');
    if (!this.enabled()) return null;
    const session = this.selected();
    const owner = this.owner();
    const today = new Intl.DateTimeFormat('en-CA', {
      timeZone: session?.businessTimeZoneId ?? 'Asia/Kolkata',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date());
    if (
      !session ||
      session.state !== 'Open' ||
      session.businessDate !== this.capability()!.businessDate ||
      session.businessDate !== today
    )
      throw new Error('tills.sessionRequired');
    if (offline && session.cashierId !== owner?.userId) throw new Error('tills.ownSessionRequired');
    if (session.cashierId !== owner?.userId && !this.overrideReason().trim())
      throw new Error('tills.overrideRequired');
    return {
      till: { sessionId: session.id, overrideReason: this.overrideReason().trim() || null },
      tillBusinessDate: session.businessDate,
      tillCashierId: session.cashierId,
    };
  }
  readonly overrideReason = signal('');
  saleEvidence(
    method: string | number,
    paid: number,
    offline = false,
  ): {
    till?: TillPostingContext;
    tenderLines?: TenderLine[];
    tillBusinessDate?: string;
    tillCashierId?: string;
  } {
    const posting = this.posting(offline);
    if (!posting) return {};
    const name = String(method).toLowerCase();
    const mapped =
      name === 'cash' || name === '1'
        ? 'Cash'
        : name === 'upi' || name === '2'
          ? 'Upi'
          : name === 'card' || name === '3'
            ? 'Card'
            : null;
    if (paid > 0 && !mapped) throw new Error('tills.tenderRequired');
    return {
      till: posting.till,
      ...(offline
        ? { tillBusinessDate: posting.tillBusinessDate, tillCashierId: posting.tillCashierId }
        : {}),
      tenderLines: paid > 0 ? [{ method: mapped!, appliedAmount: paid } as TenderLine] : [],
    };
  }
}
