import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import { type Observable, finalize } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import { CatalogueApi } from './catalogue-api.service';
import type { PlanRetirement, PlanRetirementTarget, PublishedVersion } from './catalogue-publication.models';
import { CatalogueSnapshotComponent } from './catalogue-snapshot.component';

const operationKeyPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

interface PendingRetirement {
  sessionId: string;
  target: PlanRetirementTarget;
  reason: string;
}

@Component({
  selector: 'adm-catalogue-retirement',
  imports: [ButtonDirective, RouterLink, CatalogueSnapshotComponent],
  templateUrl: './catalogue-retirement.component.html',
  styleUrls: ['../form-page.css', './catalogue-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogueRetirementComponent {
  private readonly api = inject(CatalogueApi);
  private readonly destroy = inject(DestroyRef);
  private readonly location = inject(Location);
  private readonly session = inject(AdminSessionService);
  private readonly pending = signal<PendingRetirement | null>(null);

  readonly target = input<PublishedVersion | null>(null);
  readonly saved = output<void>();
  readonly dismissed = output<void>();
  readonly denied = output<void>();
  readonly key = signal<string | null>(null);
  protected readonly result = signal<PlanRetirement | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly mfaNeeded = signal(false);
  protected readonly reason = signal('');
  protected readonly confirmed = signal(false);
  protected readonly canRetry = computed(() => !!this.pending() && this.pending()?.sessionId === this.session.sessionId());

  constructor() {
    effect(() => {
      this.target();
      this.reason.set('');
      this.confirmed.set(false);
    });
    const key = this.url().searchParams.get('retirement');
    if (!key || !operationKeyPattern.test(key)) return;
    this.key.set(key);
    this.pending.set(this.readPending(key));
    this.recover();
  }

  protected submit(event: SubmitEvent) {
    event.preventDefault();
    const target = this.target();
    const sessionId = this.session.sessionId();
    const reason = this.reason().trim();
    if (!target?.planId || target.status !== 'published' || !sessionId || !reason || reason.length > 500
      || !this.confirmed() || this.busy() || this.key()) return;
    const key = crypto.randomUUID();
    const pending = { sessionId, reason, target: { planId: target.planId, versionId: target.versionId, expectedVersion: target.version } };
    this.pending.set(pending);
    this.writePending(key, pending);
    this.setKey(key);
    this.run(this.api.retirePlan(pending.target, { operationKey: key, reason }), true);
  }

  protected recover() {
    const key = this.key();
    if (key && !this.busy()) this.run(this.api.recoverRetirement(key));
  }

  protected retry() {
    const key = this.key();
    const pending = this.pending();
    if (!key || !pending || !this.canRetry() || this.busy() || this.mfaNeeded()) return;
    this.run(this.api.retirePlan(pending.target, { operationKey: key, reason: pending.reason }));
  }

  protected close() {
    if (this.busy() || this.key() && !this.result()) return;
    this.setKey(null);
    this.result.set(null);
    this.error.set('');
    this.mfaNeeded.set(false);
    this.reason.set('');
    this.confirmed.set(false);
    this.dismissed.emit();
  }

  private run(request: Observable<PlanRetirement>, initial = false) {
    const sessionId = this.session.sessionId();
    this.busy.set(true);
    this.error.set('');
    this.mfaNeeded.set(false);
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: (result) => {
        if (sessionId !== this.session.sessionId()) return;
        this.result.set(result);
        this.clearPending();
        this.saved.emit();
      },
      error: (error: HttpErrorResponse) => {
        if (sessionId === this.session.sessionId()) this.refuse(error, initial);
      },
    });
  }

  private refuse(error: HttpErrorResponse, initial: boolean) {
    const mfa = problemCode(error.error) === 'Catalogue.RecentMfaRequired';
    if (initial && (mfa || error.status === 400 || error.status === 409)) {
      this.clearPending();
      this.setKey(null);
      this.confirmed.set(false);
    }
    if (mfa) {
      this.mfaNeeded.set(true);
      this.error.set('Sign in again with MFA, then return to this page and reload.');
    } else if (error.status === 401 || error.status === 403) {
      this.result.set(null);
      this.pending.set(null);
      this.denied.emit();
    } else if (initial && (error.status === 400 || error.status === 409)) {
      this.error.set('Retirement was refused. Review the reason and reload the version if it changed.');
    } else {
      this.error.set('The retirement outcome is unconfirmed. Check the original operation.');
    }
  }

  private url() {
    return new URL(this.location.path() || '/catalogue', 'https://catalogue.invalid');
  }

  private setKey(key: string | null) {
    const url = this.url();
    if (key) url.searchParams.set('retirement', key);
    else url.searchParams.delete('retirement');
    this.location.replaceState(url.pathname + url.search);
    this.key.set(key);
  }

  private readPending(key: string): PendingRetirement | null {
    try {
      const raw = sessionStorage.getItem('catalogue.retirement.' + key);
      const pending = raw ? JSON.parse(raw) as PendingRetirement : null;
      return pending?.sessionId && pending.sessionId === this.session.sessionId() ? pending : null;
    } catch {
      return null;
    }
  }

  private writePending(key: string, pending: PendingRetirement | null) {
    try {
      if (pending) sessionStorage.setItem('catalogue.retirement.' + key, JSON.stringify(pending));
      else sessionStorage.removeItem('catalogue.retirement.' + key);
    } catch {
      // The operation URL still supports recovery when tab storage is unavailable.
    }
  }

  private clearPending() {
    const key = this.key();
    if (key) this.writePending(key, null);
    this.pending.set(null);
  }
}
