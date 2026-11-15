import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { finalize } from 'rxjs';
import {
  CatalogueApi, factLabels, planKinds, type PlanDraftCommand, type PlanItem, type PlanKind,
} from './catalogue-api.service';
import { invalid, paiseToRupees, parseCount, parseText, rupeesToPaise } from './catalogue-money';

const refusals: Record<string, string> = {
  'Catalogue.AmountInvalid': 'Prices are rupees with up to two decimals, up to ₹10,000,000,000.00.',
  'Catalogue.CapacityInvalid': 'Capacities are whole numbers from 1 to 100,000. Unlimited is not allowed.',
  'Catalogue.ExtraShopTermsInvalid': 'Extra-shop prices need an extra-shop offer on a Multi-shop plan.',
  'Catalogue.PolicyVersionInvalid': 'Policy versions use letters, digits, dot, dash or underscore.',
  'Catalogue.NameInvalid': 'Enter a plan name of 1 to 80 characters.',
  'Catalogue.CustomReasonRequired': 'Negotiated Custom terms need a reason.',
};

@Component({
  selector: 'adm-plan-draft-editor',
  imports: [ButtonDirective],
  templateUrl: './plan-draft-editor.component.html',
  styleUrls: ['../form-page.css', './catalogue-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanDraftEditorComponent {
  private readonly api = inject(CatalogueApi);
  private readonly destroy = inject(DestroyRef);
  readonly plan = input<PlanItem | null>(null);
  readonly saved = output<PlanItem>();
  readonly conflict = output<void>();
  /** A new plan's identity is generated once and reused on retry, so a resent create cannot fork the plan. */
  private readonly newPlanId = crypto.randomUUID();
  protected readonly kinds = Object.entries(planKinds) as [PlanKind, string][];
  protected readonly chosenKind = signal<PlanKind>('multi-shop');
  protected readonly kind = computed(() => this.plan()?.kind ?? this.chosenKind());
  protected readonly draft = computed(() => this.plan()?.draft ?? null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly cleared = signal(false);
  protected readonly rupees = paiseToRupees;

  protected label(fact: string) {
    return factLabels[fact] ?? fact;
  }

  protected chooseKind(value: string) {
    if (value in planKinds) this.chosenKind.set(value as PlanKind);
  }

  protected submit(event: SubmitEvent, form: HTMLFormElement) {
    event.preventDefault();
    if (this.busy() || this.cleared()) return;
    this.notice.set('');
    const command = this.read(new FormData(form));
    if (!command) return;
    this.busy.set(true);
    this.error.set('');
    this.api.savePlan(this.plan()?.planId ?? this.newPlanId, command)
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({
        next: (item) => {
          this.notice.set('Draft saved. It is not published and no customer can see it.');
          this.saved.emit(item);
        },
        error: (error: HttpErrorResponse) => this.refuse(error),
      });
  }

  private read(data: FormData): PlanDraftCommand | null {
    const text = (name: string) => String(data.get(name) ?? '');
    const kind = this.kind();
    const prices = ['monthlyPricePaise', 'annualPricePaise', 'extraShopMonthlyPricePaise', 'extraShopAnnualPricePaise']
      .map((name) => rupeesToPaise(text(name)));
    const counts = [kind === 'single-shop' ? '' : text('includedShops'), text('membersPerShop')].map(parseCount);
    if (prices.includes(invalid) || counts.includes(invalid)) {
      this.error.set('Use rupees with up to two decimals and whole-number capacities from 1 to 100,000.');
      return null;
    }
    const reason = kind === 'custom' ? parseText(text('reason')) : null;
    if (kind === 'custom' && !reason) {
      this.error.set(refusals['Catalogue.CustomReasonRequired']);
      return null;
    }
    const offer = kind === 'multi-shop' ? text('extraShopsOffered') : '';
    const [monthly, annual, extraMonthly, extraAnnual] = prices as (number | null)[];
    if (offer !== 'yes' && (extraMonthly !== null || extraAnnual !== null)) {
      this.error.set(refusals['Catalogue.ExtraShopTermsInvalid']);
      return null;
    }
    return {
      expectedVersion: this.draft()?.version ?? 0, kind, name: text('name').trim(),
      monthlyPricePaise: monthly, annualPricePaise: annual,
      includedShops: counts[0] as number | null, membersPerShop: counts[1] as number | null,
      extraShopsOffered: offer === '' ? null : offer === 'yes',
      extraShopMonthlyPricePaise: extraMonthly, extraShopAnnualPricePaise: extraAnnual,
      customerTermsVersion: parseText(text('customerTermsVersion')),
      refundPolicyVersion: parseText(text('refundPolicyVersion')), reason,
    };
  }

  private refuse(error: HttpErrorResponse) {
    const code = problemCode(error.error) ?? '';
    if (error.status === 409) {
      this.cleared.set(true);
      this.error.set('This draft changed elsewhere. Reload the catalogue before editing.');
      this.conflict.emit();
    } else {
      this.error.set(refusals[code] ?? 'The draft was not saved. Check every field and try again.');
    }
  }
}
