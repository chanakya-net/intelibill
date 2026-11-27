import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ExpenseService, ExpenseDto } from '../../expenses/services/expense.service';
@Component({
  selector: 'app-till-expense-reference',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslocoPipe],
  template: `<main>
    <a routerLink="/tills">{{ 'tills.title' | transloco }}</a>
    <h1>{{ 'tills.expenseReference' | transloco }}</h1>
    @if (error()) {
      <p role="alert">{{ 'tills.loadError' | transloco }}</p>
    }
    @if (expense(); as e) {
      <h2>{{ e.categoryName }}</h2>
      <p>{{ e.amount | currency: 'INR' }} · {{ e.expenseDate }} · {{ e.paidTo }}</p>
      <p>{{ e.description }}</p>
      <code>{{ e.id }}</code>
      @if (e.settlementEvidence; as evidence) {
        <p>
          {{
            (evidence.disposition === 'NoPhysicalMovement'
              ? 'tills.noMovement'
              : 'tills.' + evidence.disposition
            ) | transloco
          }}
        </p>
        <p>{{ evidence.reason }}</p>
        @if (evidence.tender; as tender) {
          <p>
            {{ 'tills.methods.' + tender.method | transloco }} ·
            {{ tender.appliedAmount | currency: 'INR' }} · {{ tender.externalReference }}
          </p>
        }
      } @else {
        <p>{{ 'tills.methods.UnknownRecordedMethod' | transloco }}</p>
      }
    }
  </main>`,
  styles: [
    `
      main {
        max-width: 760px;
        margin: auto;
        padding: 24px;
      }
      code,
      pre {
        overflow-wrap: anywhere;
        white-space: pre-wrap;
      }
    `,
  ],
})
export class TillExpenseReferencePageComponent {
  private readonly api = inject(ExpenseService);
  private readonly route = inject(ActivatedRoute);
  readonly expense = signal<ExpenseDto | null>(null);
  readonly error = signal(false);
  constructor() {
    const id = this.route.snapshot.paramMap.get('expenseId');
    if (id)
      this.api
        .getExpenseById(id)
        .subscribe({ next: (e) => this.expense.set(e), error: () => this.error.set(true) });
  }
}
