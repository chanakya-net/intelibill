import { tillEvidenceLabel, tillPurposeLabel } from '../services/till-labels';
import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { TillService } from '../services/till.service';
import { TillSession } from '../services/till.models';
@Component({
  selector: 'app-till-close-print-page',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoPipe],
  template: ` <main>
    <nav>
      <a routerLink="/tills">{{ 'tills.title' | transloco }}</a
      ><button (click)="print()">{{ 'tills.print' | transloco }}</button>
    </nav>
    @if (error()) {
      <p role="alert">{{ 'tills.loadError' | transloco }}</p>
    }
    @if (session(); as s) {
      @if (s.close; as c) {
        <header>
          <p>{{ 'tills.frozenReference' | transloco }}</p>
          <h1>{{ c.tillName }}</h1>
          <p>{{ c.cashierName }} · {{ c.businessDate }}</p>
          <code>{{ c.id }}</code>
        </header>
        @if (c.isOverdue) {
          <p class="notice">{{ 'tills.overdueWarning' | transloco }}</p>
        }
        <dl>
          <dt>{{ 'tills.closed' | transloco }}</dt>
          <dd>{{ c.closedAt | date: 'medium' }}</dd>
          <dt>{{ 'tills.cashierId' | transloco }}</dt>
          <dd>{{ c.cashierId }}</dd>
          <dt>{{ 'tills.closedBy' | transloco }}</dt>
          <dd>{{ c.closedBy }}</dd>
          <dt>{{ 'tills.boundary' | transloco }}</dt>
          <dd>#{{ c.lastMovementSequence }} · {{ c.totals.movementCount }}</dd>
          <dt>{{ 'tills.openingFloat' | transloco }}</dt>
          <dd>{{ c.openingFloat | currency: 'INR' }}</dd>
          <dt>{{ 'tills.expected' | transloco }}</dt>
          <dd>{{ c.expectedCash | currency: 'INR' }}</dd>
          <dt>{{ 'tills.counted' | transloco }}</dt>
          <dd>{{ c.countedCash | currency: 'INR' }}</dd>
          <dt>{{ 'tills.variance' | transloco }}</dt>
          <dd>{{ c.variance | currency: 'INR' }}</dd>
        </dl>
        <h2>{{ 'tills.recordedTotals' | transloco }}</h2>
        @for (t of c.totals.recordedTenderTotals | keyvalue; track t.key) {
          <p>{{ 'tills.methods.' + t.key | transloco }}: {{ t.value | currency: 'INR' }}</p>
        }
        <h2>{{ 'tills.purposeTotals' | transloco }}</h2>
        @for (t of c.totals.purposeTotals | keyvalue; track t.key) {
          <p>
            {{ purposeLabel(t.key) | transloco }}: {{ t.value | currency: 'INR' }}
            <small
              ><code>{{ t.key }}</code></small
            >
          </p>
        }
        <p>
          {{ 'tills.creditIssued' | transloco }}: {{ c.totals.storeCreditIssued | currency: 'INR' }}
        </p>
        <p>
          {{ 'tills.creditRedeemed' | transloco }}:
          {{ c.totals.storeCreditRedeemed | currency: 'INR' }}
        </p>
        <p>
          {{ 'tills.receivable' | transloco }}: {{ c.totals.receivableChange | currency: 'INR' }}
        </p>
        <p>{{ c.reason }} · {{ c.overrideReason }}</p>
        @if (s.awaitingReview) {
          <p>{{ 'tills.awaitingReview' | transloco }}</p>
        }
        @if (s.review; as r) {
          <p>
            {{ 'tills.reviewed' | transloco }}: {{ r.reviewedAt | date: 'medium' }} ·
            {{ r.reviewedBy }} · {{ r.reason }}
          </p>
        }
        <h2>{{ 'tills.supplements' | transloco }}</h2>
        <p>{{ 'tills.excluded' | transloco }}</p>
        @for (late of s.latePostings; track late.id) {
          <section>
            <p>
              {{ late.sourceBusinessDate }} · {{ late.recordedAt | date: 'medium' }} ·
              {{ evidenceLabel(late.evidenceStatus) | transloco }}
            </p>
            <p>
              {{
                late.supplementalAmount === null
                  ? ('tills.unknownAmount' | transloco)
                  : (late.supplementalAmount | currency: 'INR')
              }}
            </p>
            <p>{{ late.managerReason }} · {{ late.evidenceReason }}</p>
            <code>{{ late.saleId }} · {{ late.caseId }}</code>
          </section>
        }
      } @else {
        <p>{{ 'tills.notClosed' | transloco }}</p>
      }
    }
  </main>`,
  styles: [
    `
      main {
        max-width: 800px;
        margin: 24px auto;
        padding: 32px;
        color: #182235;
        background: white;
      }
      nav {
        display: flex;
        justify-content: space-between;
      }
      header {
        border-bottom: 2px solid #182235;
        padding: 24px 0;
      }
      dl {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 10px;
      }
      dd {
        margin: 0;
        overflow-wrap: anywhere;
      }
      code {
        overflow-wrap: anywhere;
      }
      section {
        border-top: 1px solid #ccc;
      }
      .notice {
        background: #fff7ed;
        padding: 12px;
      }
      @media (max-width: 600px) {
        main {
          padding: 16px;
          margin: 0;
        }
        dl {
          grid-template-columns: 1fr;
        }
        dd {
          margin-bottom: 10px;
        }
      }
      @media print {
        nav {
          display: none;
        }
        main {
          margin: 0;
          padding: 0;
        }
      }
    `,
  ],
})
export class TillClosePrintPageComponent {
  readonly purposeLabel = tillPurposeLabel;
  readonly evidenceLabel = tillEvidenceLabel;
  readonly session = signal<TillSession | null>(null);
  readonly error = signal(false);
  private readonly api = inject(TillService);
  private readonly route = inject(ActivatedRoute);
  constructor() {
    const id = this.route.snapshot.paramMap.get('id');
    if (id)
      this.api
        .detail(id)
        .subscribe({ next: (x) => this.session.set(x.session), error: () => this.error.set(true) });
  }
  print() {
    window.print();
  }
}
