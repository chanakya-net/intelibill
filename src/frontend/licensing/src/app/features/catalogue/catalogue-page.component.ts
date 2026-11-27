import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { finalize, forkJoin } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import { CatalogueApi, licensingManager, planKinds, type PlanItem, type TrialDraft } from './catalogue-api.service';
import { CataloguePublicationComponent } from './catalogue-publication.component';
import { CatalogueRetirementComponent } from './catalogue-retirement.component';
import { CatalogueSnapshotComponent } from './catalogue-snapshot.component';
import type { PreparedPublication, PublishedVersion } from './catalogue-publication.models';
import { formatInr } from './catalogue-money';
import { PlanDraftEditorComponent } from './plan-draft-editor.component';
import { TrialPolicyEditorComponent } from './trial-policy-editor.component';

@Component({
  selector: 'adm-catalogue-page',
  imports: [ButtonDirective, RouterLink, PlanDraftEditorComponent, TrialPolicyEditorComponent, CataloguePublicationComponent, CatalogueSnapshotComponent, CatalogueRetirementComponent],
  templateUrl: './catalogue-page.component.html',
  styleUrls: ['../form-page.css', './catalogue-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CataloguePageComponent {
  private readonly api = inject(CatalogueApi);
  private readonly session = inject(AdminSessionService);
  private readonly destroy = inject(DestroyRef);
  private editors = 0;
  protected readonly access = computed(() => this.session.capabilities().includes(licensingManager));
  protected readonly notice = this.session.notice;
  protected readonly plans = signal<readonly PlanItem[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly trial = signal<TrialDraft | null>(null);
  protected readonly selected = signal<PlanItem | null>(null);
  protected readonly editorKey = signal<string | null>(null);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly reloadRequired = signal(false);
  protected readonly history = signal<readonly PublishedVersion[]>([]);
  protected readonly retirementTarget = signal<PublishedVersion | null>(null);
  protected readonly historyCursor = signal<string | null>(null);
  protected readonly currentTrial = signal<string | null>(null);
  protected readonly historyBusy = signal(false);
  protected readonly historyError = signal('');
  protected readonly kinds = planKinds;
  protected readonly inr = formatInr;

  constructor() {
    effect(() => {
      if (this.access()) untracked(() => this.load());
      else untracked(() => this.reset());
    });
  }

  protected load() {
    if (!this.access() || this.busy()) return;
    this.reset();
    this.busy.set(true);
    this.loadHistory();
    forkJoin([this.api.listPlans(null), this.api.trialDraft()])
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({
        next: ([page, trial]) => {
          if (!this.access()) return;
          this.plans.set(page.items);
          this.nextCursor.set(page.nextCursor);
          this.trial.set(trial.draft);
          this.loaded.set(true);
        },
        error: (error: HttpErrorResponse) => { if (this.access()) this.refuse(error); },
      });
  }

  protected more() {
    const cursor = this.nextCursor();
    if (!cursor || this.busy()) return;
    this.busy.set(true);
    this.api.listPlans(cursor).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: (page) => {
        if (!this.access()) return;
        this.plans.update((items) => [...items, ...page.items]);
        this.nextCursor.set(page.nextCursor);
      },
      error: (error: HttpErrorResponse) => { if (this.access()) this.refuse(error); },
    });
  }

  protected edit(item: PlanItem | null) {
    if (this.reloadRequired()) return;
    this.selected.set(item);
    this.editorKey.set(item ? item.planId : 'new-' + ++this.editors);
  }

  protected planSaved(item: PlanItem) {
    this.selected.set(item);
    this.plans.update((items) => items.some((row) => row.planId === item.planId)
      ? items.map((row) => row.planId === item.planId ? item : row) : [...items, item]);
  }

  protected loadHistory(after: string | null = null) {
    if (!this.access() || this.historyBusy()) return;
    this.historyBusy.set(true);
    this.historyError.set('');
    this.api.history(after).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.historyBusy.set(false))).subscribe({
      next: (page) => {
        if (!this.access()) return;
        this.history.update((items) => after ? [...items, ...page.items] : page.items);
        this.historyCursor.set(page.nextCursor);
        this.currentTrial.set(page.currentTrialVersionId);
      },
      error: (error: HttpErrorResponse) => this.refuseHistory(error),
    });
  }

  protected publicationSaved(operation: PreparedPublication) {
    if (operation.planId) {
      this.plans.update((rows) => rows.filter((row) => row.draft.versionId !== operation.versionId));
      if (this.selected()?.draft.versionId === operation.versionId) {
        this.selected.set(null);
        this.editorKey.set(null);
      }
    } else if (this.trial()?.versionId === operation.versionId) {
      this.trial.set(null);
    }
    this.loadHistory();
  }

  protected createSuccessor(row: PublishedVersion) {
    if (!this.access() || this.historyBusy()) return;
    const source = { expectedVersion: 0, sourceVersionId: row.versionId, sourceExpectedVersion: row.version };
    if (row.planId && 'plan' in row.snapshot) {
      const { kind, name, monthlyPricePaise, annualPricePaise, includedShops, membersPerShop, extraShopsOffered,
        extraShopMonthlyPricePaise, extraShopAnnualPricePaise, customerTermsVersion, refundPolicyVersion } = row.snapshot.plan;
      this.historyBusy.set(true);
      this.api.savePlan(row.planId, { ...source, kind, name, monthlyPricePaise, annualPricePaise, includedShops,
        membersPerShop, extraShopsOffered, extraShopMonthlyPricePaise, extraShopAnnualPricePaise, customerTermsVersion,
        refundPolicyVersion, reason: row.snapshot.negotiationReason,
      }).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.historyBusy.set(false))).subscribe({
        next: (item) => { if (this.access()) { this.planSaved(item); this.edit(item); } },
        error: (error: HttpErrorResponse) => this.refuseHistory(error),
      });
    } else if (!row.planId && !('plan' in row.snapshot)) {
      const { activeMemberCap, maxConcurrentTrialShops, trialTermsVersion } = row.snapshot;
      this.historyBusy.set(true);
      this.api.saveTrial({ ...source, activeMemberCap, maxConcurrentTrialShops, trialTermsVersion })
        .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.historyBusy.set(false))).subscribe({
          next: ({ draft }) => { if (this.access()) this.trial.set(draft); },
          error: (error: HttpErrorResponse) => this.refuseHistory(error),
        });
    }
  }

  protected publishedName(row: PublishedVersion) {
    return 'plan' in row.snapshot ? row.snapshot.plan.name : 'Trial policy';
  }

  protected publishedNumber(row: PublishedVersion) {
    return 'plan' in row.snapshot ? row.snapshot.plan.versionNumber : row.snapshot.versionNumber;
  }

  protected publicationDenied() {
    this.reset();
    this.error.set('Catalogue access could not be confirmed.');
    this.reconfirm();
  }

  protected conflicted() {
    this.reloadRequired.set(true);
  }

  protected reconfirm() {
    this.session.check(true).pipe(takeUntilDestroyed(this.destroy)).subscribe();
  }

  private reset() {
    this.plans.set([]);
    this.trial.set(null);
    this.selected.set(null);
    this.editorKey.set(null);
    this.loaded.set(false);
    this.error.set('');
    this.reloadRequired.set(false);
    this.history.set([]);
    this.retirementTarget.set(null);
    this.historyCursor.set(null);
    this.currentTrial.set(null);
    this.historyError.set('');
  }

  private refuseHistory(error: HttpErrorResponse) {
    if (error.status === 401 || error.status === 403) {
      this.refuse(error);
      return;
    }
    this.historyError.set(error.status === 409
      ? 'A successor draft already exists or the version changed. Reload the catalogue.'
      : 'Published catalogue history is unavailable. Try again.');
  }

  private refuse(error: HttpErrorResponse) {
    this.reset();
    this.error.set(error.status === 401 || error.status === 403
      ? 'Catalogue access could not be confirmed.' : 'The catalogue could not be loaded. Try again.');
    if (error.status === 401 || error.status === 403) this.reconfirm();
  }
}
