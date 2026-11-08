import { CommonModule } from '@angular/common';
import { Component, ViewChild, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { AuthService } from '../../../core/auth/auth.service';
import { CustomerService, PaymentReceiptDetail } from '../../customers/services/customer.service';
import { TillPostingFieldsComponent } from '../components/till-posting-fields.component';
import { TillPendingActionsComponent } from '../components/till-pending-actions.component';
@Component({
  selector: 'app-till-payment-receipt-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    TranslocoPipe,
    TillPostingFieldsComponent,
    TillPendingActionsComponent,
  ],
  template: `<main>
    <a routerLink="/tills">{{ 'tills.title' | transloco }}</a>
    <h1>{{ 'tills.paymentReceipt' | transloco }}</h1>
    @if (error()) {
      <p role="alert">{{ error() | transloco }}</p>
    }
    @if (receipt(); as r) {
      <h2>{{ r.partyName }}</h2>
      <p>
        {{ r.businessDate }} · {{ r.amount | currency: 'INR' }} ·
        {{ 'tills.methods.' + r.method | transloco }}
      </p>
      <code>{{ r.receiptId }}</code>
      <p>{{ r.externalReference }} · {{ r.notes }}</p>
      @if (r.reversal) {
        <h3>{{ 'tills.reversed' | transloco }}</h3>
        <p>{{ r.reversal.reason }} · {{ r.reversal.businessDate }}</p>
        @if (r.reversal.settlementEvidence; as evidence) {
          <p>
            {{
              (evidence.disposition === 'NoPhysicalMovement'
                ? 'tills.noMovement'
                : 'tills.' + evidence.disposition
              ) | transloco
            }}
            · {{ evidence.reason }}
          </p>
          @if (evidence.tender; as tender) {
            <p>
              {{ 'tills.methods.' + tender.method | transloco }} ·
              {{ tender.appliedAmount | currency: 'INR' }}
            </p>
          }
        }
      } @else if (owner()) {
        <app-till-pending-actions
          action="payment-reversal"
          [target]="r.receiptId"
          (confirmed)="load()"
        />
        <app-till-posting-fields
          [evidence]="true"
          [direction]="r.partyType === 'Customer' ? 'MoneyPaid' : 'MoneyReceived'"
        />
        <label
          >{{ 'tills.reason' | transloco
          }}<textarea [(ngModel)]="reason" maxlength="255"></textarea></label
        ><button (click)="reverse()" [disabled]="busy()">
          {{ 'tills.reversePayment' | transloco }}
        </button>
      }
    }
  </main>`,
  styles: [
    `
      main {
        max-width: 760px;
        margin: 24px auto;
        padding: 20px;
      }
      label {
        display: grid;
        gap: 8px;
      }
      textarea {
        padding: 12px;
      }
      button {
        margin-top: 14px;
        padding: 12px;
      }
      code {
        overflow-wrap: anywhere;
      }
    `,
  ],
})
export class TillPaymentReceiptPageComponent {
  private readonly api = inject(CustomerService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  @ViewChild(TillPostingFieldsComponent) fields!: TillPostingFieldsComponent;
  readonly receipt = signal<PaymentReceiptDetail | null>(null);
  readonly error = signal('');
  readonly busy = signal(false);
  readonly owner = computed(
    () =>
      this.auth
        .session()
        ?.shops.find((x) => x.shopId === this.auth.session()?.activeShopId)
        ?.role.toLowerCase() === 'owner',
  );
  reason = '';
  private readonly operationKey = crypto.randomUUID();
  constructor() {
    this.load();
  }
  load() {
    const id = this.route.snapshot.paramMap.get('receiptId');
    if (id)
      this.api.getPaymentReceipt(id).subscribe({
        next: (r) => this.receipt.set(r),
        error: () => this.error.set('tills.loadError'),
      });
  }
  reverse() {
    const r = this.receipt();
    if (!r || !this.owner() || this.reason.trim().length < 3) return;
    let evidence;
    try {
      evidence = this.fields.fields(r.amount);
    } catch {
      this.error.set('tills.evidenceRequired');
      return;
    }
    this.busy.set(true);
    this.api
      .reversePayment(r.receiptId, {
        reason: this.reason.trim(),
        operationKey: this.operationKey,
        ...evidence,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.load();
        },
        error: () => {
          this.busy.set(false);
          this.error.set('tills.unknown');
        },
      });
  }
}
