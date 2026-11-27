import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators, type AbstractControl, type ValidationErrors } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { firstValueFrom } from 'rxjs';
import { basisLabels, formatIstExact, stateLabels, statusReaders, type ProviderClosure } from '../accounts/account-status-api.service';
import { exactIstInstant } from '../accounts/trial-extension-api.service';
import { AdminSessionService } from '../auth/admin-session.service';
import { ReopeningApi, reopeningRefusal, type ReopeningImpact, type ReopeningTarget } from './reopening-api.service';
import { InvalidReopeningDraft, InvalidReopeningInput, ReopeningDrafts, reopeningId, supportedReopeningText,
  validReopeningInstant, validateReopeningDraft, type PendingReopening } from './reopening-drafts';

const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

// The form applies the rules a saved draft must meet, so unsupported input stays editable in the review.
const supportedText = (control: AbstractControl<string>): ValidationErrors | null =>
  supportedReopeningText(control.value.trim()) ? null : { unsupportedText: true };
const consentInstant = (control: AbstractControl<string>): ValidationErrors | null => {
  const ist = exactIstInstant(control.value);
  const at = ist ? new Date(ist) : null;
  return at && Number.isFinite(at.valueOf()) && validReopeningInstant(at.toISOString()) ? null : { consentInstant: true };
};
const text = (max: number) => [Validators.required, Validators.pattern(/\S/), Validators.maxLength(max), supportedText];

interface ReviewScope {
  readonly accountId: string;
  readonly adminId: string;
  readonly sessionId: string;
}

@Component({
  selector: 'adm-reopening-review',
  imports: [ReactiveFormsModule, RouterLink, ButtonDirective],
  templateUrl: './reopening-review.component.html',
  styleUrls: ['../form-page.css', './reopening-review.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReopeningReviewComponent {
  private readonly admin = inject(AdminSessionService);
  private readonly api = inject(ReopeningApi);
  private readonly drafts = inject(ReopeningDrafts);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroy = inject(DestroyRef);
  private readonly scope = signal<ReviewScope | null>(null);
  protected readonly canReview = computed(() => this.admin.state() === 'authenticated' &&
    this.admin.capabilities().includes('licensing-manager'));
  protected readonly canRead = computed(() => this.admin.state() === 'authenticated' &&
    statusReaders.some(capability => this.admin.capabilities().includes(capability)));
  protected readonly account = signal(this.route.snapshot.queryParamMap.get('account') ?? '');
  protected readonly rows = signal<readonly ProviderClosure[] | null>(null);
  protected readonly impact = signal<ReopeningImpact | null>(null);
  protected readonly pending = signal<PendingReopening | null>(null);
  protected readonly busy = signal(false);
  protected readonly invalidDraft = signal(false);
  protected readonly saved = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly format = formatIstExact;
  protected readonly states = stateLabels;
  protected readonly bases: Readonly<Partial<Record<string, string>>> = basisLabels;
  protected readonly statusLabels = {
    Pending: 'Pending merchant acknowledgement',
    Acknowledged: 'Acknowledged by merchant service',
    NeedsReview: 'Delivery needs review',
    Superseded: 'Superseded without merchant acknowledgement',
  };
  protected readonly form = inject(FormBuilder).nonNullable.group({
    reason: ['', text(1000)],
    ownerConsentUserId: ['', [Validators.required, Validators.pattern(reopeningId)]],
    ownerConsentAt: ['', [Validators.required, consentInstant]],
    ownerConsentReference: ['', text(120)],
    ownerConsentEvidence: ['', text(2000)],
    ownerConsentConfirmed: [false, Validators.requiredTrue],
  });

  constructor() {
    effect(() => {
      const adminId = this.admin.adminId();
      const sessionId = this.admin.sessionId();
      const canRead = this.canRead();
      this.canReview();
      untracked(() => {
        this.reset();
        if (canRead && adminId && sessionId && uuid.test(this.account())) this.lookup();
      });
    });
  }

  protected changeAccount(value: string): void {
    this.reset();
    this.account.set(value);
  }

  protected lookup(event?: Event): void {
    event?.preventDefault();
    const accountId = this.account().trim().toLowerCase();
    const adminId = this.admin.adminId();
    const sessionId = this.admin.sessionId();
    if (this.busy() || !this.canRead() || !uuid.test(accountId) || !adminId || !sessionId) return;
    this.reset();
    const scope = { accountId, adminId, sessionId };
    this.scope.set(scope);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { account: accountId }, replaceUrl: true });
    void this.run(scope, () => this.read(scope));
  }

  protected preview(closureId: string, shopId: string): void {
    const scope = this.scope();
    if (!scope || !this.canReview() || this.pending() || this.invalidDraft() || this.busy()) return;
    this.form.reset();
    this.impact.set(null);
    this.saved.set(false);
    void this.run(scope, async () => {
      const impact = await firstValueFrom(this.api.preview({ accountId: scope.accountId, closureId, shopId }, scope.sessionId));
      if (this.current(scope) && impact.closureId === closureId && impact.shopId === shopId) this.impact.set(impact);
    });
  }

  protected confirm(): void {
    const impact = this.impact();
    const scope = this.scope();
    const values = this.form.getRawValue();
    const ist = exactIstInstant(values.ownerConsentAt);
    const at = ist ? new Date(ist) : null;
    if (this.busy() || !scope || !impact?.coverageAdequate || !this.canReview() || this.pending() || this.invalidDraft() ||
      this.form.invalid || !at || !Number.isFinite(at.valueOf())) return;
    const reason = values.reason.trim();
    const ownerConsentReference = values.ownerConsentReference.trim();
    const ownerConsentEvidence = values.ownerConsentEvidence.trim();
    if (!reason || !ownerConsentReference || !ownerConsentEvidence) return;
    const draft: PendingReopening = {
      accountId: scope.accountId, adminId: scope.adminId, closureId: impact.closureId, shopId: impact.shopId,
      command: {
        ...values, reason, ownerConsentReference, ownerConsentEvidence, ownerConsentAt: at.toISOString(),
        operationKey: crypto.randomUUID(), expectedVersion: impact.version, impactDigest: impact.impactDigest,
      },
    };
    void this.run(scope, async () => {
      const pending = await this.drafts.claim(draft);
      if (!this.current(scope) || !this.canReview()) return;
      this.pending.set(this.requireDraftTarget(scope, pending));
      if (pending.command.operationKey === draft.command.operationKey) await this.send(scope, pending);
    });
  }

  protected retry(): void {
    const scope = this.scope();
    const pending = this.pending();
    if (scope && pending && this.canReview() && !this.busy()) {
      void this.run(scope, () => this.send(scope, pending));
    }
  }

  protected refresh(): void {
    const scope = this.scope();
    if (scope && !this.busy()) void this.run(scope, () => this.read(scope));
  }

  private async read(scope: ReviewScope): Promise<void> {
    const closures = await firstValueFrom(this.api.list(scope.accountId, scope.sessionId));
    if (!this.current(scope)) return;
    this.rows.set(closures);
    if (this.canReview()) {
      const pending = await this.drafts.read(scope.adminId, scope.accountId);
      if (!this.current(scope)) return;
      this.pending.set(pending && this.requireDraftTarget(scope, pending));
      this.invalidDraft.set(false);
    }
  }

  private requireDraftTarget(scope: ReviewScope, draft: PendingReopening): PendingReopening {
    const validated = validateReopeningDraft(draft, scope.adminId, scope.accountId);
    const listed = this.rows()?.some(closure => closure.id === validated.closureId &&
      closure.shops.some(shop => shop.shopId === validated.shopId));
    if (!listed) throw new InvalidReopeningDraft();
    return validated;
  }

  private async send(scope: ReviewScope, draft: PendingReopening): Promise<void> {
    if (!this.current(scope) || !this.canReview()) return;
    this.requireDraftTarget(scope, draft);
    try {
      const decision = await firstValueFrom(this.api.save(draft, draft.command, scope.sessionId));
      if (decision.closureId !== draft.closureId || decision.shopId !== draft.shopId) {
        throw new Error('The reopening receipt did not match the reviewed shop.');
      }
    } catch (error) {
      if (reopeningRefusal(error)) {
        await this.drafts.clear(draft);
        if (this.current(scope)) {
          this.pending.set(null);
          this.impact.set(null);
          this.form.reset();
        }
      }
      throw error;
    }
    await this.drafts.clear(draft);
    if (!this.current(scope)) return;
    this.pending.set(null);
    this.impact.set(null);
    this.form.reset();
    this.saved.set(true);
    await this.read(scope);
  }

  private current(scope: ReviewScope): boolean {
    return !this.destroy.destroyed && this.scope() === scope && this.admin.adminId() === scope.adminId &&
      this.admin.sessionId() === scope.sessionId && this.canRead();
  }

  private async run(scope: ReviewScope, work: () => Promise<void>): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      await work();
    } catch (error) {
      if (this.current(scope)) {
        if (error instanceof InvalidReopeningDraft) {
          this.pending.set(null);
          this.invalidDraft.set(true);
        }
        this.error.set(this.errorText(error));
      }
    } finally {
      if (this.current(scope)) this.busy.set(false);
    }
  }

  private reset(): void {
    this.scope.set(null);
    this.rows.set(null);
    this.impact.set(null);
    this.pending.set(null);
    this.saved.set(false);
    this.invalidDraft.set(false);
    this.busy.set(false);
    this.error.set(null);
    this.form.reset();
  }

  private errorText(error: unknown): string {
    if (error instanceof InvalidReopeningDraft)
      return 'The saved draft could not be verified. Check the account history and contact support to recover the draft.';
    if (error instanceof InvalidReopeningInput) return 'Check the review fields, correct them and confirm again.';
    const code = error instanceof HttpErrorResponse ? error.error?.code : null;
    if (code === 'Reopening.RecentMfaRequired') return 'Sign in again with MFA to save the review.';
    if (this.pending()) return 'The save outcome is unconfirmed. Retry the same request.';
    if (reopeningRefusal(error)) return 'The review was refused. Check the current coverage and new owner consent before reviewing again.';
    return 'Reopening information is temporarily unavailable. Try again.';
  }
}
