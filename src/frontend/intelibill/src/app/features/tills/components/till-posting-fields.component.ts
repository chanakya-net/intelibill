import { CommonModule } from '@angular/common';
import { Component, Input, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TranslocoPipe } from '@ngneat/transloco';
import { TillContextService } from '../services/till-context.service';
import { TenderMethod, TillSettlementEvidence } from '../services/till.models';
@Component({
  selector: 'app-till-posting-fields',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslocoPipe],
  template: ` @if (context.error()) {
      <p role="alert">
        {{ 'tills.capabilityUnknown' | transloco }}
        <button type="button" (click)="context.refresh()">{{ 'tills.refresh' | transloco }}</button>
      </p>
    }
    @if (context.enabled()) {
      <fieldset class="till-fields">
        <legend>{{ 'tills.posting' | transloco }}</legend>
        <label
          >{{ 'tills.session' | transloco
          }}<select
            [disabled]="disabled"
            [ngModel]="context.selected()?.id ?? ''"
            (ngModelChange)="context.select($event)"
            [ngModelOptions]="{ standalone: true }"
          >
            <option value="">{{ 'tills.choose' | transloco }}</option>
            @for (s of context.sessions(); track s.id) {
              <option [value]="s.id">
                {{ s.tillName }} · {{ s.cashierName }} · {{ s.businessDate }}
              </option>
            }
          </select></label
        >
        @if (context.selected()?.cashierId !== context.owner()?.userId) {
          <label
            >{{ 'tills.overrideReason' | transloco
            }}<input
              [disabled]="disabled"
              maxlength="500"
              [ngModel]="context.overrideReason()"
              (ngModelChange)="context.overrideReason.set($event)"
              [ngModelOptions]="{ standalone: true }"
          /></label>
        }
        @if (evidence) {
          <label
            >{{ 'tills.disposition' | transloco
            }}<select
              [disabled]="disabled"
              [(ngModel)]="disposition"
              [ngModelOptions]="{ standalone: true }"
            >
              <option value="">{{ 'tills.choose' | transloco }}</option>
              <option value="NoPhysicalMovement">{{ 'tills.noMovement' | transloco }}</option>
              <option [value]="direction">{{ 'tills.' + direction | transloco }}</option>
            </select></label
          ><label
            >{{ 'tills.evidenceReason' | transloco
            }}<input
              [disabled]="disabled"
              [(ngModel)]="reason"
              maxlength="500"
              [ngModelOptions]="{ standalone: true }"
          /></label>
        }
        @if (!evidence || disposition !== 'NoPhysicalMovement') {
          @if (!sale) {
            <label
              >{{ 'tills.tender' | transloco
              }}<select
                [disabled]="disabled"
                [(ngModel)]="method"
                [ngModelOptions]="{ standalone: true }"
              >
                <option value="">{{ 'tills.choose' | transloco }}</option>
                @for (m of methods; track m) {
                  <option [value]="m">{{ 'tills.methods.' + m | transloco }}</option>
                }
              </select></label
            ><label
              >{{ 'tills.reference' | transloco
              }}<input
                [disabled]="disabled"
                [(ngModel)]="reference"
                maxlength="200"
                [ngModelOptions]="{ standalone: true }"
            /></label>
          }
          @if (sale) {
            <p>{{ 'tills.saleTender' | transloco }}</p>
          }
          <p>{{ 'tills.singleTender' | transloco }}</p>
          @if (refund) {
            <label
              ><input
                type="checkbox"
                [(ngModel)]="refundConfirmed"
                [ngModelOptions]="{ standalone: true }"
              />{{ 'tills.refundConfirmed' | transloco }}</label
            >
          }
        }
      </fieldset>
    }`,
  styles: [
    `
      :host {
        display: block;
      }
      fieldset {
        border: 1px solid var(--p-content-border-color, #d4d4d8);
        border-radius: 12px;
        padding: 12px;
        margin: 12px 0;
        display: grid;
        gap: 10px;
      }
      label {
        display: grid;
        gap: 4px;
      }
      select,
      input {
        width: 100%;
        padding: 9px;
        border: 1px solid #a1a1aa;
        border-radius: 6px;
        background: var(--p-content-background, #fff);
        color: inherit;
      }
      p {
        font-size: 0.85rem;
        margin: 0;
      }
    `,
  ],
})
export class TillPostingFieldsComponent {
  readonly context = inject(TillContextService);
  @Input() disabled = false;
  @Input() evidence = false;
  @Input() sale = false;
  @Input() refund = false;
  @Input() direction: 'MoneyPaid' | 'MoneyReceived' = 'MoneyPaid';
  readonly methods: TenderMethod[] = ['Cash', 'Upi', 'Card', 'BankTransfer', 'Cheque'];
  method: TenderMethod | '' = '';
  reference = '';
  reason = '';
  disposition: TillSettlementEvidence['disposition'] | '' = '';
  sessionOnly() {
    const posting = this.context.posting();
    return posting
      ? { tillOwner: this.context.owner()!, till: posting.till }
      : { tillOwner: this.context.owner()! };
  }
  refundFields(amount: number, external: boolean) {
    if (!external || amount === 0) return this.sessionOnly();
    const fields = this.fields(amount);
    if (this.context.enabled() && !this.refundConfirmed) throw new Error('tills.evidenceRequired');
    return this.context.enabled()
      ? {
          tillOwner: this.context.owner()!,
          till: fields['till'],
          refundTender: fields['tenderLine'],
          refundConfirmed: this.refundConfirmed,
        }
      : { tillOwner: this.context.owner()! };
  }
  refundConfirmed = false;
  fields(amount: number): Record<string, unknown> {
    if (this.context.error() || !this.context.capability())
      throw new Error('tills.capabilityUnknown');
    if (!this.context.enabled()) return { tillOwner: this.context.owner()! };
    if (this.evidence && (!this.disposition || !this.reason.trim()))
      throw new Error('tills.evidenceRequired');
    if (this.evidence && this.disposition === 'NoPhysicalMovement')
      return {
        tillOwner: this.context.owner()!,
        settlementEvidence: { disposition: this.disposition, reason: this.reason.trim() },
      };
    if (amount <= 0) throw new Error('tills.noMovementRequired');
    const posting = this.context.posting()!;
    if (!this.method) throw new Error('tills.tenderRequired');
    const tender = {
      method: this.method,
      appliedAmount: amount,
      externalReference: this.reference.trim() || null,
    };
    return this.evidence
      ? {
          tillOwner: this.context.owner()!,
          till: posting.till,
          settlementEvidence: { disposition: this.disposition, reason: this.reason.trim(), tender },
        }
      : {
          tillOwner: this.context.owner()!,
          till: posting.till,
          tenderLine: tender,
          method: this.method,
          externalReference: tender.externalReference,
        };
  }
}
