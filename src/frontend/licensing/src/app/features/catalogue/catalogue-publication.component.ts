import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { type Observable, finalize } from 'rxjs';
import { CatalogueApi, type PlanItem, type TrialDraft } from './catalogue-api.service';
import type { PreparedPublication, PublicationPreparation, PublicationTarget } from './catalogue-publication.models';
import { CatalogueSnapshotComponent } from './catalogue-snapshot.component';

@Component({
  selector: 'adm-catalogue-publication',
  imports: [ButtonDirective, RouterLink, CatalogueSnapshotComponent],
  templateUrl: './catalogue-publication.component.html',
  styleUrls: ['../form-page.css', './catalogue-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CataloguePublicationComponent {
  private readonly api = inject(CatalogueApi);
  private readonly destroy = inject(DestroyRef);
  private readonly location = inject(Location);
  private readonly route = inject(ActivatedRoute);

  readonly plan = input<PlanItem | null>(null);
  readonly trial = input<TrialDraft | null>(null);
  readonly currentTrialVersionId = input<string | null>(null);
  readonly published = output<PreparedPublication>();
  readonly denied = output<void>();
  protected readonly busy = signal(false);
  protected readonly key = signal<string | null>(this.route.snapshot.queryParamMap.get('publication'));
  protected readonly operation = signal<PreparedPublication | null>(null);
  protected readonly error = signal('');
  protected readonly acknowledged = signal(false);
  protected readonly mfaNeeded = signal(false);
  protected readonly stale = signal(false);
  protected readonly target = signal<PublicationTarget | null>(null);
  protected readonly planReady = computed(() => this.plan()?.draft.complete === true && !!this.currentTrialVersionId());

  constructor() {
    if (this.key()) this.recover();
  }

  protected choosePlan() {
    const item = this.plan();
    if (!item?.draft.complete || !this.currentTrialVersionId() || this.key()) return;
    this.target.set({ planId: item.planId, versionId: item.draft.versionId, expectedVersion: item.draft.version });
  }

  protected chooseTrial() {
    const trial = this.trial();
    if (!trial?.complete || this.key()) return;
    this.target.set({ planId: null, versionId: trial.versionId, expectedVersion: trial.version });
  }

  protected prepare(event: SubmitEvent, form: HTMLFormElement) {
    event.preventDefault();
    const target = this.target();
    const reason = String(new FormData(form).get('publicationReason') ?? '').trim();
    if (!target || !reason || this.busy() || this.key()) return;
    const key = crypto.randomUUID();
    const preparation: PublicationPreparation = { operationKey: key, reason,
      trialPolicyVersionId: target.planId ? this.currentTrialVersionId() : null };
    this.storeKey(key);
    this.run(this.api.preparePublication(target, preparation), 'prepare');
  }

  protected confirm() {
    const prepared = this.operation();
    if (!prepared || prepared.publishedAt || !this.acknowledged() || this.busy() || this.mfaNeeded() || this.stale()) return;
    this.acknowledged.set(false);
    this.run(this.api.confirmPublication(prepared), 'confirm');
  }

  protected recover() {
    const key = this.key();
    if (key && !this.busy()) this.run(this.api.recoverPublication(key), 'recover');
  }

  protected close() {
    this.storeKey(null);
    this.operation.set(null);
    this.target.set(null);
    this.error.set('');
    this.stale.set(false);
  }

  private run(request: Observable<PreparedPublication>, action: 'prepare' | 'confirm' | 'recover') {
    this.busy.set(true);
    this.error.set('');
    this.mfaNeeded.set(false);
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: (operation) => {
        this.operation.set(operation);
        this.target.set(null);
        this.stale.set(false);
        if (operation.publishedAt && action !== 'prepare') this.published.emit(operation);
      },
      error: (error: HttpErrorResponse) => this.refuse(error, action),
    });
  }

  private refuse(error: HttpErrorResponse, action: 'prepare' | 'confirm' | 'recover') {
    const code = problemCode(error.error);
    if (code === 'Catalogue.RecentMfaRequired') {
      if (action === 'prepare') this.storeKey(null);
      this.mfaNeeded.set(true);
      this.error.set('Sign in again with MFA, then return to this page and reload.');
    } else if (error.status === 401 || error.status === 403 && code !== 'Catalogue.PublicationNotApproved') {
      this.operation.set(null);
      this.target.set(null);
      this.error.set('Catalogue access could not be confirmed.');
      this.denied.emit();
    } else if (error.status === 409 && action === 'confirm') {
      this.stale.set(true);
      this.error.set('The prepared terms changed. Close this preview and reload the catalogue before preparing a new one.');
    } else if (action === 'prepare' && [400, 403, 409].includes(error.status)) {
      this.storeKey(null);
      this.error.set(code === 'Catalogue.PublicationNotApproved' ? 'Publication is unavailable for this configuration.'
        : 'The draft could not be prepared. Reload the catalogue and review its terms.');
    } else {
      this.error.set('The publication outcome is unconfirmed. Check the original operation.');
    }
  }

  private storeKey(key: string | null) {
    const url = new URL(this.location.path() || '/catalogue', 'https://catalogue.invalid');
    if (key) url.searchParams.set('publication', key);
    else url.searchParams.delete('publication');
    this.location.replaceState(url.pathname + url.search);
    this.key.set(key);
  }
}
