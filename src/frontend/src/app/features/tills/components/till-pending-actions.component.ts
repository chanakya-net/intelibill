import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { Component, Input, Output, EventEmitter, inject, signal, OnInit } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { TillContextService } from '../services/till-context.service';
import { TillDraftService } from '../services/till-draft.service';
import { TillService } from '../services/till.service';
import { TillDraftRecord } from '../services/till.models';
import { sameOwner } from '../../../core/context/shop-scope';
@Component({
  selector: 'app-till-pending-actions',
  standalone: true,
  imports: [CommonModule, TranslocoPipe],
  template: `@if (records().length) {
      <aside role="status">
        <h3>{{ 'tills.pending' | transloco }}</h3>
        <p>{{ 'tills.pendingRecovery' | transloco }}</p>
        @for (r of records(); track r.targetId) {
          @let p = r.pending!.payload;
          <section>
            <h4>{{ actionLabel(r) | transloco }}</h4>
            <dl>
              <dt>{{ 'tills.amount' | transloco }}</dt>
              <dd>
                {{
                  amount(r) === null
                    ? ('tills.unknownAmount' | transloco)
                    : (amount(r) | currency: 'INR')
                }}
              </dd>
              <dt>{{ 'tills.session' | transloco }}</dt>
              <dd>{{ sessionLabel(r) }}</dd>
              <dt>{{ 'tills.reason' | transloco }}</dt>
              <dd>{{ p['reason'] || p['description'] || '—' }}</dd>
            </dl>
            <small
              >{{ 'tills.operationReference' | transloco }}: {{ r.pending!.operationKey }}</small
            >
            <details>
              <summary>{{ 'tills.preservedDetails' | transloco }}</summary>
              <pre>{{ p | json }}</pre>
            </details>
            <button type="button" [disabled]="busy()" (click)="retry(r)">
              {{ 'tills.retryFrozen' | transloco }}
            </button>
          </section>
        }
      </aside>
    }
    @if (recovered()) {
      <p role="status">{{ 'tills.frozenConfirmed' | transloco }} · {{ recovered() }}</p>
    }
    @if (error()) {
      <p role="alert">{{ 'tills.unknown' | transloco }}</p>
    }`,
  styles: [
    `
      aside {
        padding: 14px;
        border: 1px solid #f59e0b;
        border-radius: 10px;
        margin: 12px 0;
        background: #fffbeb;
        color: #78350f;
      }
      dl {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 8px;
      }
      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
      small {
        display: block;
        overflow-wrap: anywhere;
      }
      pre {
        white-space: pre-wrap;
        overflow-wrap: anywhere;
        font-size: 0.8rem;
      }
      summary {
        overflow-wrap: anywhere;
      }
      button {
        padding: 9px;
        margin: 8px 0;
      }
    `,
  ],
})
export class TillPendingActionsComponent implements OnInit {
  @Input() action = '';
  @Input() target = '';
  @Output() readonly lockedChange = new EventEmitter<boolean>();
  @Output() readonly confirmed = new EventEmitter<void>();
  readonly records = signal<TillDraftRecord[]>([]);
  readonly busy = signal(false);
  readonly error = signal(false);
  readonly recovered = signal('');
  private readonly drafts = inject(TillDraftService);
  private readonly api = inject(TillService);
  private readonly context = inject(TillContextService);
  constructor() {
    this.drafts
      .watch()
      .pipe(takeUntilDestroyed())
      .subscribe(() => void this.refresh());
  }
  actionLabel(r: TillDraftRecord) {
    const action = r.targetId.split(':')[0];
    if (action === 'cash-movement')
      return r.pending!.payload['direction'] === 1 ? 'tills.cashIn' : 'tills.cashOut';
    return (
      (
        {
          create: 'tills.create',
          open: 'tills.open',
          close: 'tills.countedClose',
          review: 'tills.review',
        } as Record<string, string>
      )[action] ?? 'tills.sourceActions.' + action
    );
  }
  amount(r: TillDraftRecord): number | null {
    const p = r.pending!.payload;
    const refund = p['refundTender'] as { appliedAmount?: number } | undefined;
    const settlement = p['settlementEvidence'] as
      | { tender?: { appliedAmount?: number } }
      | undefined;
    const amount =
      p['amount'] ??
      p['countedCash'] ??
      p['openingFloat'] ??
      refund?.appliedAmount ??
      settlement?.tender?.appliedAmount;
    return typeof amount === 'number' ? amount : null;
  }
  sessionLabel(r: TillDraftRecord) {
    const till = r.pending!.payload['till'] as { sessionId?: string } | undefined;
    const id = till?.sessionId ?? r.targetId.split(':')[1];
    const session = this.context.sessions().find((x) => x.id === id);
    return session ? `${session.tillName} · ${session.cashierName} · ${session.businessDate}` : id;
  }
  ngOnInit() {
    void this.refresh();
  }
  async refresh() {
    const owner = this.context.owner();
    if (!owner) return;
    try {
      const all = await this.drafts.list(owner);
      if (!sameOwner(owner, this.context.owner())) return;
      const records = all.filter(
        (x) =>
          x.pending &&
          (!this.action ||
            x.targetId.startsWith(
              this.target ? `${this.action}:${this.target}:` : `${this.action}:`,
            )),
      );
      this.records.set(records);
      this.lockedChange.emit(records.length > 0);
    } catch {
      this.error.set(true);
      this.lockedChange.emit(true);
    }
  }
  async retry(record: TillDraftRecord) {
    const owner = { userId: record.userId, shopId: record.shopId };
    if (!sameOwner(owner, this.context.owner()) || !record.pending) return;
    const [action, target, ...cycle] = record.targetId.split(':');
    const path = record.pending.payload['__requestPath'];
    if (typeof path !== 'string') return;
    this.busy.set(true);
    try {
      await firstValueFrom(
        this.api.execute(
          owner,
          action,
          target,
          cycle.join(':'),
          record.pending.payload,
          path,
          !['create', 'open', 'cash-movement', 'close', 'review'].includes(action),
          undefined,
          true,
        ),
      );
      if (sameOwner(owner, this.context.owner())) {
        await this.refresh();
        this.recovered.set(record.pending.operationKey);
        this.confirmed.emit();
      }
    } catch {
      this.error.set(true);
    } finally {
      this.busy.set(false);
    }
  }
}
