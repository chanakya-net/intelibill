import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { finalize, forkJoin } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import { CatalogueApi, licensingManager, planKinds, type PlanItem, type TrialDraft } from './catalogue-api.service';
import { formatInr } from './catalogue-money';
import { PlanDraftEditorComponent } from './plan-draft-editor.component';
import { TrialPolicyEditorComponent } from './trial-policy-editor.component';

@Component({
  selector: 'adm-catalogue-page',
  imports: [ButtonDirective, RouterLink, PlanDraftEditorComponent, TrialPolicyEditorComponent],
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
    forkJoin([this.api.listPlans(null), this.api.trialDraft()])
      .pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false)))
      .subscribe({
        next: ([page, trial]) => {
          this.plans.set(page.items);
          this.nextCursor.set(page.nextCursor);
          this.trial.set(trial.draft);
          this.loaded.set(true);
        },
        error: (error: HttpErrorResponse) => this.refuse(error),
      });
  }

  protected more() {
    const cursor = this.nextCursor();
    if (!cursor || this.busy()) return;
    this.busy.set(true);
    this.api.listPlans(cursor).pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: (page) => {
        this.plans.update((items) => [...items, ...page.items]);
        this.nextCursor.set(page.nextCursor);
      },
      error: (error: HttpErrorResponse) => this.refuse(error),
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
  }

  private refuse(error: HttpErrorResponse) {
    this.reset();
    this.error.set(error.status === 401 || error.status === 403
      ? 'Catalogue access could not be confirmed.' : 'The catalogue could not be loaded. Try again.');
    if (error.status === 401 || error.status === 403) this.reconfirm();
  }
}
