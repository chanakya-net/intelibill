import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import type { CheckoutScope } from '../checkout/checkout.models';
import { CheckoutService } from '../checkout/checkout.service';
import type { PublishedPlan } from '../plans/billing-plans.service';
import { BillingAccountService } from '../services/billing-account.service';
import { formatBillingInstant, type SubscriptionTermStatus } from '../status/subscription-status.models';
import { CapacityReductionDrafts, InvalidCapacityReductionDraft } from './capacity-reduction-drafts';
import {
  CapacityReductionService, isValidSchedule, matchesPreviewRequest, type CapacityReductionPreview,
  type CapacityReductionShop, type CreateCapacityReductionPreview, type ScheduleCapacityReduction,
  type ScheduledCapacityReduction,
} from './capacity-reduction.service';

type ReductionView =
  | { readonly kind: 'hidden' | 'closed' | 'loading' }
  | { readonly kind: 'form'; readonly plans: readonly PublishedPlan[] }
  | { readonly kind: 'preview'; readonly preview: CapacityReductionPreview; readonly shops: readonly CapacityReductionShop[] }
  | { readonly kind: 'scheduled'; readonly schedule: ScheduledCapacityReduction; readonly shops: readonly CapacityReductionShop[]; readonly errorCode?: string }
  | { readonly kind: 'failed'; readonly code: string };

const knownCodes: Readonly<Record<string, string>> = {
  'Billing.CapacityReductionInvalid': 'invalid',
  'Billing.CapacityReductionNotFound': 'notFound',
  'Billing.CapacityReductionSourceChanged': 'changed',
  'Billing.CapacityReductionExpired': 'expired',
  'Billing.CapacityReductionMemberConflict': 'memberConflict',
  'Billing.CapacityReductionAlreadyScheduled': 'alreadyScheduled',
  'Billing.CapacityReductionAlreadyFunded': 'alreadyFunded',
  'Billing.CapacityReductionNeedsReview': 'review',
  'Billing.OperationPayloadMismatch': 'mismatch',
};

/**
 * Billing-owner flow for scheduling an owner-selected capacity reduction at renewal. Shops are never
 * preselected; live aggregate active-member conflicts must be resolved before scheduling or applying.
 */
@Component({
  selector: 'app-capacity-reduction-preview',
  imports: [FormsModule, RouterLink, TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './capacity-reduction-preview.component.html',
  styles: [`
    :host { display: block; }
    section { display: grid; gap: 0.5rem; }
    h3, p { margin: 0; }
    dl { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 0.25rem 1rem; margin: 0; }
    dd { margin: 0; overflow-wrap: anywhere; }
    form { display: grid; gap: 0.5rem; max-width: 36rem; }
    ul.shop-list { list-style: none; padding: 0; margin: 0; display: grid; gap: 0.35rem; }
    .shop-row { display: flex; flex-wrap: wrap; align-items: center; gap: 0.5rem; }
    .conflict-badge { font-weight: 600; }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CapacityReductionPreviewComponent {
  readonly account = input.required<string>();
  readonly term = input.required<SubscriptionTermStatus>();
  readonly changed = output<void>();

  private readonly router = inject(Router);
  private readonly context = inject(ShopContextService);
  private readonly accounts = inject(BillingAccountService);
  private readonly checkout = inject(CheckoutService);
  private readonly api = inject(CapacityReductionService);
  private readonly drafts = inject(CapacityReductionDrafts);
  private readonly locale = inject(LocalizationService);

  protected readonly view = signal<ReductionView>({ kind: 'hidden' });
  protected readonly busy = signal(false);
  protected readonly planId = signal<string | null>(null);
  protected readonly capacity = signal(1);
  protected readonly selectedShops = signal<ReadonlySet<string>>(new Set());
  protected readonly confirmed = signal(false);
  protected readonly selectedPlan = computed(() => {
    const current = this.view();
    return current.kind === 'form' ? current.plans.find(p => p.versionId === this.planId()) : undefined;
  });
  protected readonly canSchedule = computed(() => {
    const current = this.view();
    if (current.kind !== 'preview' || !this.confirmed() || this.expired(current.preview)) return false;
    const chosen = this.selectedShops();
    if (chosen.size === 0 || chosen.size > current.preview.targetCapacity) return false;
    return !current.shops.some(shop => chosen.has(shop.shopId) && shop.exceedsAllowance);
  });

  private generation = 0;
  private readonly scope = computed<CheckoutScope | null>(() => {
    const owner = this.context.scope();
    return owner ? { userId: owner.userId, shopId: owner.shopId, account: this.account() } : null;
  });

  constructor() {
    effect(() => {
      const scope = this.scope();
      const term = this.term();
      untracked(() => void this.start(scope, term));
    });
  }

  private async start(scope: CheckoutScope | null, term: SubscriptionTermStatus) {
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    this.view.set({ kind: 'hidden' });
    this.selectedShops.set(new Set());
    this.confirmed.set(false);
    if (!scope || term.basis !== 'Paid' || term.memberAllowance === null) return;
    try {
      const listed = await firstValueFrom(this.accounts.list());
      if (!current() || listed.find(item => item.accountId === scope.account)?.role !== 'Owner') return;

      const existing = await this.readExistingSchedule(scope, term.termId);
      if (!current()) return;
      if (existing) {
        await this.showSchedule(scope, existing, current);
        return;
      }

      const draft = this.drafts.read(scope, term.termId);
      if (draft?.schedule) {
        await this.recoverOrSendSchedule(scope, term.termId, draft.schedule, current);
      } else if (draft?.preview) {
        await this.sendPreview(scope, term, draft.preview, current);
      } else {
        this.view.set({ kind: 'closed' });
      }
    } catch (error) {
      if (current()) this.fail(error);
    }
  }

  protected async open() {
    const scope = this.scope();
    const term = this.term();
    if (!scope || term.memberAllowance === null) return;
    const generation = ++this.generation;
    this.view.set({ kind: 'loading' });
    try {
      const plans = (await this.checkout.plans(() => generation === this.generation))
        .filter(plan => this.isReductionTarget(plan, term));
      if (generation !== this.generation) return;
      this.view.set({ kind: 'form', plans });
      this.selectPlan(plans[0]?.versionId ?? null);
    } catch (error) {
      if (generation === this.generation) this.fail(error);
    }
  }

  protected selectPlan(id: string | null) {
    this.planId.set(id);
    const plan = this.selectedPlan();
    if (!plan) return;
    const max = this.maxCapacity(plan);
    this.capacity.set(plan.extraShopsOffered ? Math.min(Math.max(plan.includedShops, 1), max) : plan.includedShops);
  }

  protected maxCapacity(plan: PublishedPlan | undefined): number {
    const term = this.term();
    if (!plan) return 1;
    if (!plan.extraShopsOffered) return plan.includedShops;
    const seatsLower = plan.membersPerShop < (term.memberAllowance ?? 0);
    return Math.max(plan.includedShops, seatsLower ? term.purchasedCapacity : term.purchasedCapacity - 1);
  }

  protected async preview() {
    const scope = this.scope();
    const term = this.term();
    const plan = this.selectedPlan();
    const capacity = this.capacity();
    if (!scope || !plan || term.memberAllowance === null || !Number.isSafeInteger(capacity)) return;
    if (capacity < plan.includedShops || capacity > this.maxCapacity(plan)) return;
    const generation = ++this.generation;
    const command: CreateCapacityReductionPreview = {
      operationKey: crypto.randomUUID(), targetPlanVersionId: plan.versionId, targetCapacity: capacity,
      observedCapacity: term.purchasedCapacity, observedMemberAllowance: term.memberAllowance,
      observedPaidThrough: term.paidThrough,
    };
    this.view.set({ kind: 'loading' });
    try {
      const saved = await this.drafts.savePreview(scope, term.termId, command);
      await this.sendPreview(scope, term, saved, () => generation === this.generation);
    } catch (error) {
      if (generation === this.generation) this.fail(error);
    }
  }

  protected toggleShop(shopId: string, checked: boolean) {
    const current = this.view();
    if (current.kind !== 'preview') return;
    const next = new Set(this.selectedShops());
    if (checked) {
      if (next.size < current.preview.targetCapacity) next.add(shopId);
    } else {
      next.delete(shopId);
    }
    this.selectedShops.set(next);
  }

  protected shopDisabled(shop: CapacityReductionShop, preview: CapacityReductionPreview): boolean {
    if (shop.exceedsAllowance) return true;
    const chosen = this.selectedShops();
    return !chosen.has(shop.shopId) && chosen.size >= preview.targetCapacity;
  }

  protected async confirmSchedule() {
    const scope = this.scope();
    const currentView = this.view();
    if (!scope || currentView.kind !== 'preview' || !this.canSchedule()) return;
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    const preview = currentView.preview;
    const command: ScheduleCapacityReduction = {
      operationKey: crypto.randomUUID(), previewId: preview.previewId, previewSnapshotHash: preview.snapshotHash,
      retainedShopIds: [...this.selectedShops()].sort(), customerTermsVersion: preview.customerTermsVersion,
      refundPolicyVersion: preview.refundPolicyVersion, confirmed: true,
    };
    this.busy.set(true);
    try {
      const saved = await this.drafts.saveSchedule(scope, preview.termId, command);
      const schedule = await this.submitSchedule(scope, preview.termId, saved);
      if (!current()) return;
      await this.drafts.clearSection(scope, preview.termId, 'preview');
      await this.drafts.clearSection(scope, preview.termId, 'schedule');
      await this.showSchedule(scope, schedule, current);
      this.changed.emit();
    } catch (error) {
      if (current()) this.fail(error);
    } finally {
      if (current()) this.busy.set(false);
    }
  }

  protected async startOrder() {
    const scope = this.scope();
    const currentView = this.view();
    if (!scope || currentView.kind !== 'scheduled' || currentView.schedule.state !== 'Scheduled') return;
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    const schedule = currentView.schedule;
    this.busy.set(true);
    try {
      const command = await this.drafts.saveOrder(scope, schedule.termId, schedule.scheduleId, schedule.snapshotHash);
      const order = await this.api.createOrder(scope, schedule.termId, schedule.scheduleId, command);
      if (!current()) return;
      await this.drafts.clearSection(scope, schedule.termId, 'order');
      await this.router.navigate(['/billing/accounts', scope.account, 'checkout'], { queryParams: { order: order.orderId } });
    } catch (error) {
      if (current()) this.showScheduleError(currentView, error);
    } finally {
      if (current()) this.busy.set(false);
    }
  }

  protected async resolve() {
    const scope = this.scope();
    const currentView = this.view();
    if (!scope || currentView.kind !== 'scheduled' || currentView.schedule.state !== 'NeedsResolution') return;
    const generation = ++this.generation;
    const current = () => generation === this.generation;
    const schedule = currentView.schedule;
    this.busy.set(true);
    try {
      const command = await this.drafts.saveResolve(
        scope, schedule.termId, schedule.scheduleId, schedule.resolutionGeneration, schedule.snapshotHash);
      const updated = await this.api.resolve(scope, schedule.termId, schedule.scheduleId, command).catch(async error => {
        if (error instanceof HttpErrorResponse && [400, 403, 404, 409].includes(error.status))
          await this.drafts.clearSection(scope, schedule.termId, 'resolve');
        throw error;
      });
      if (!current()) return;
      if (!isValidSchedule(updated, scope, schedule.termId)) throw new Error('Invalid resolved reduction');
      await this.drafts.clearSection(scope, schedule.termId, 'resolve');
      await this.showSchedule(scope, updated, current);
      this.changed.emit();
    } catch (error) {
      if (current()) this.showScheduleError(currentView, error);
    } finally {
      if (current()) this.busy.set(false);
    }
  }

  protected async startOver() {
    const scope = this.scope();
    if (!scope) return;
    const generation = ++this.generation;
    await this.drafts.discardAll(scope, this.term().termId);
    if (generation === this.generation) await this.open();
  }

  private async readExistingSchedule(scope: CheckoutScope, termId: string): Promise<ScheduledCapacityReduction | null> {
    try {
      const schedule = await this.api.readSchedule(scope, termId);
      return isValidSchedule(schedule, scope, termId) ? schedule : null;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) return null;
      throw error;
    }
  }

  private async recoverOrSendSchedule(scope: CheckoutScope, termId: string, command: ScheduleCapacityReduction,
    current: () => boolean) {
    this.view.set({ kind: 'loading' });
    try {
      const recovered = await this.api.recoverSchedule(scope, termId, command.operationKey);
      if (!current()) return;
      if (!isValidSchedule(recovered, scope, termId)) throw new Error('Invalid scheduled reduction');
      await this.drafts.clearSection(scope, termId, 'schedule');
      await this.drafts.clearSection(scope, termId, 'preview');
      await this.showSchedule(scope, recovered, current);
      return;
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 404)) throw error;
    }
    if (!current()) return;
    const schedule = await this.submitSchedule(scope, termId, command);
    if (!current()) return;
    await this.drafts.clearSection(scope, termId, 'schedule');
    await this.drafts.clearSection(scope, termId, 'preview');
    await this.showSchedule(scope, schedule, current);
  }

  private async submitSchedule(scope: CheckoutScope, termId: string, command: ScheduleCapacityReduction) {
    const schedule = await this.api.schedule(scope, termId, command).catch(async error => {
      if (error instanceof HttpErrorResponse && [400, 403, 404, 409].includes(error.status))
        await this.drafts.clearSection(scope, termId, 'schedule');
      throw error;
    });
    if (!isValidSchedule(schedule, scope, termId)) throw new Error('Invalid scheduled reduction');
    return schedule;
  }

  private async sendPreview(scope: CheckoutScope, term: SubscriptionTermStatus, command: CreateCapacityReductionPreview,
    current: () => boolean) {
    this.view.set({ kind: 'loading' });
    const preview = await this.api.createPreview(scope, term.termId, command).catch(async error => {
      if (error instanceof HttpErrorResponse && [400, 403, 404, 409].includes(error.status))
        await this.drafts.clearSection(scope, term.termId, 'preview');
      throw error;
    });
    if (!current()) return;
    if (!matchesPreviewRequest(preview, scope, term.termId, command)) throw new Error('Reduction preview mismatch');
    const shops = await this.api.previewShops(scope, preview, current);
    if (!current()) return;
    // Never preselect any shop; the owner chooses retained shops explicitly.
    this.selectedShops.set(new Set());
    this.confirmed.set(false);
    this.view.set({ kind: 'preview', preview, shops });
  }

  private async showSchedule(scope: CheckoutScope, schedule: ScheduledCapacityReduction, current: () => boolean) {
    const shops = await this.api.scheduleShops(scope, schedule, current);
    if (current()) this.view.set({ kind: 'scheduled', schedule, shops });
  }

  private showScheduleError(currentView: Extract<ReductionView, { kind: 'scheduled' }>, error: unknown) {
    const code = this.mapError(error);
    if (code === 'denied') this.view.set({ kind: 'hidden' });
    else this.view.set({ ...currentView, errorCode: code });
  }

  private fail(error: unknown) {
    const code = this.mapError(error);
    if (code === 'denied') this.view.set({ kind: 'hidden' });
    else this.view.set({ kind: 'failed', code });
  }

  private mapError(error: unknown): string {
    if (error instanceof InvalidCapacityReductionDraft) return 'invalid';
    const http = error instanceof HttpErrorResponse ? error : null;
    if (typeof http?.error?.code === 'string' && knownCodes[http.error.code]) return knownCodes[http.error.code];
    return http?.status === 403 ? 'denied' : 'unavailable';
  }

  private isReductionTarget(plan: PublishedPlan, term: SubscriptionTermStatus): boolean {
    const allowance = term.memberAllowance ?? 0;
    if (plan.membersPerShop > allowance || plan.includedShops > term.purchasedCapacity) return false;
    return plan.includedShops < term.purchasedCapacity || plan.membersPerShop < allowance;
  }

  protected expired(preview: CapacityReductionPreview): boolean {
    return Date.now() >= Date.parse(preview.expiresAt);
  }

  protected instant(value: string | null) {
    return formatBillingInstant(value, this.locale.currentLanguage(), true);
  }

  protected rupees(paise: number) {
    return paise / 100;
  }
}
