import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { firstValueFrom } from 'rxjs';
import { formatIstExact } from '../accounts/account-status-api.service';
import { exactIstInstant } from '../accounts/trial-extension-api.service';
import { AdminSessionService } from '../auth/admin-session.service';
import { SuspensionApi, SuspensionRefusal, type ReleaseImpact, type SuspensionCommand, type SuspensionImpact, type SuspensionSelection, type SuspensionStatus } from './suspension-api.service';
import { SuspensionDrafts, type PendingSuspension } from './suspension-drafts';

const uuid = /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i;

@Component({
  selector: 'adm-suspension-page',
  imports: [ReactiveFormsModule, RouterLink, ButtonDirective],
  templateUrl: './suspension-page.component.html',
  styleUrls: ['../form-page.css', './suspension-page.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SuspensionPageComponent {
  private readonly admin = inject(AdminSessionService);
  private readonly api = inject(SuspensionApi);
  private readonly drafts = inject(SuspensionDrafts);
  private readonly destroy = inject(DestroyRef);
  protected readonly canSuspend = computed(() => this.admin.capabilities().includes('security-administrator'));
  protected readonly canRead = computed(() => this.canSuspend() || this.admin.capabilities().includes('support'));
  protected readonly impact = signal<SuspensionImpact | null>(null);
  private generation = 0;
  private requestGeneration = 0;
  protected readonly releaseImpact = signal<ReleaseImpact | null>(null);
  protected readonly releaseReason = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(1000)] });
  protected readonly saved = signal<readonly SuspensionStatus[]>([]);
  protected readonly pending = signal<PendingSuspension | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly format = formatIstExact;
  protected readonly labels = {
    Pending: 'Pending merchant acknowledgement', Acknowledged: 'Acknowledged by merchant service',
    NeedsReview: 'Delivery needs review', NoShops: 'Saved for future affiliated shops',
  };
  protected readonly form = inject(FormBuilder).nonNullable.group({
    accountId: ['', [Validators.required, Validators.pattern(uuid)]],
    scope: [''],
    shopId: [''],
    mode: [''],
    effectiveAt: [''],
    customerExplanation: ['', [Validators.required, Validators.maxLength(500)]],
    internalReason: ['', [Validators.required, Validators.maxLength(1000)]],
    reviewReference: ['', [Validators.required, Validators.maxLength(120)]],
  });

  constructor() {
    effect(() => {
      this.admin.sessionId();
      this.admin.adminId();
      this.form.reset();
      this.clearResults();
    });
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => this.error.set(null));
    const controls = this.form.controls;
    for (const control of [controls.accountId, controls.scope, controls.shopId]) {
      control.valueChanges.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => this.impact.set(null));
    }
    controls.accountId.valueChanges.pipe(takeUntilDestroyed(this.destroy)).subscribe(() => {
      this.form.patchValue({ shopId: '', customerExplanation: '', internalReason: '', reviewReference: '' });
      this.clearResults();
    });
  }

  protected preview(): void {
    const value = this.form.getRawValue();
    const selection: SuspensionSelection | null = value.scope === 'Account' ? { scope: 'Account', shopId: null }
      : value.scope === 'Shop' && uuid.test(value.shopId) ? { scope: 'Shop', shopId: value.shopId.toLowerCase() } : null;
    if (!selection) {
      this.error.set('Select account scope or enter the shop ID for shop scope.');
      return;
    }
    void this.run(async (account, admin, session) => {
      const pending = await this.drafts.read(admin, account);
      if (!this.current(account, admin, session)) return;
      this.pending.set(pending);
      if (pending) return;
      const impact = await firstValueFrom(this.api.preview(account, selection, session));
      if (this.current(account, admin, session) && this.form.controls.scope.value === selection.scope &&
        (selection.scope === 'Account' || this.form.controls.shopId.value.toLowerCase() === selection.shopId)) {
        this.clearRelease();
        this.impact.set(impact);
      }
    });
  }

  protected suspend(): void {
    const impact = this.impact();
    if (!impact || impact.alreadySuspended || !this.canSuspend()) return;
    const value = this.form.getRawValue();
    const effectiveAt = value.effectiveAt.trim() ? exactIstInstant(value.effectiveAt) : null;
    const customerExplanation = value.customerExplanation.trim();
    const internalReason = value.internalReason.trim();
    const reviewReference = value.reviewReference.trim();
    if (this.form.invalid || !customerExplanation || !internalReason || !reviewReference ||
      (value.mode !== 'Security' && value.mode !== 'Trading') || (value.effectiveAt.trim() && !effectiveAt)) {
      this.error.set('Complete the mode, customer explanation, internal reason and review reference. Check the effective time.');
      return;
    }
    const command: SuspensionCommand = { scope: impact.selection.scope, shopId: impact.selection.shopId,
      operationKey: crypto.randomUUID(), mode: value.mode,
      effectiveAt, customerExplanation, internalReason, reviewReference, expectedVersion: impact.version,
      impactDigest: impact.impactDigest };
    void this.run((account, admin, session) => this.claimAndSend({ adminId: admin, accountId: account, command }, session));
  }

  protected previewRelease(restriction: SuspensionStatus): void {
    if (!this.canSuspend() || restriction.release || this.busy()) return;
    this.clearRelease();
    this.impact.set(null);
    void this.run(async (account, admin, session) => {
      const pending = await this.drafts.read(admin, account);
      if (!this.current(account, admin, session)) return;
      this.pending.set(pending);
      if (pending) return;
      const impact = await firstValueFrom(this.api.previewRelease(account, restriction.id, session));
      if (this.current(account, admin, session) && impact.restrictionId === restriction.id) this.releaseImpact.set(impact);
    });
  }

  protected confirmRelease(): void {
    const impact = this.releaseImpact();
    const reason = this.releaseReason.value.trim();
    if (!impact || !reason || this.releaseReason.invalid || !this.canSuspend() || this.pending()) return;
    const command = { reason, expectedVersion: impact.version, impactDigest: impact.impactDigest, operationKey: crypto.randomUUID() };
    void this.run((account, admin, session) => this.claimAndSend({
      adminId: admin, accountId: account, restrictionId: impact.restrictionId, command,
    }, session));
  }

  private async claimAndSend(draft: PendingSuspension, session: string): Promise<void> {
    const pending = await this.drafts.claim(draft);
    if (!this.current(draft.accountId, draft.adminId, session)) return;
    this.pending.set(pending);
    if (pending.command.operationKey === draft.command.operationKey) await this.send(pending, session);
  }

  protected retry(): void {
    const pending = this.pending();
    if (pending && this.canSuspend()) void this.run(async (_, __, session) => this.send(pending, session));
  }

  protected refresh(): void {
    void this.run(async (account, admin, session) => {
      const pending = this.canSuspend() ? await this.drafts.read(admin, account) : null;
      const saved = await firstValueFrom(this.api.list(account, session));
      if (this.current(account, admin, session)) {
        this.saved.set(saved);
        this.pending.set(pending);
      }
    });
  }

  private async send(pending: PendingSuspension, session: string): Promise<void> {
    const { accountId, adminId } = pending;
    try {
      const operation = pending.restrictionId !== undefined
        ? this.api.release(accountId, pending.restrictionId, pending.command, session)
        : this.api.suspend(accountId, pending.command, session);
      const saved = await firstValueFrom(operation);
      await this.drafts.clear(pending);
      if (this.current(accountId, adminId, session)) {
        this.pending.set(null);
        this.impact.set(null);
        this.clearRelease();
        this.saved.update(items => [saved, ...items.filter(item => item.id !== saved.id)]);
        this.form.patchValue({ customerExplanation: '', internalReason: '', reviewReference: '' });
      }
    } catch (error) {
      if (error instanceof SuspensionRefusal) {
        await this.drafts.clear(pending);
        if (this.current(accountId, adminId, session)) {
          this.pending.set(null);
          this.impact.set(null);
          this.clearRelease();
        }
      }
      throw error;
    }
  }

  private async run(work: (account: string, admin: string, session: string) => Promise<void>): Promise<void> {
    const account = this.form.controls.accountId.value.toLowerCase();
    const admin = this.admin.adminId();
    const session = this.admin.sessionId();
    if (this.busy() || !this.canRead() || !admin || !session) return;
    if (!uuid.test(account)) {
      this.error.set('Enter a valid billing account ID.');
      return;
    }
    this.error.set(null);
    this.requestGeneration = this.generation;
    this.busy.set(true);
    try {
      await work(account, admin, session);
    } catch (error) {
      if (this.current(account, admin, session)) this.error.set(this.errorText(error));
    } finally {
      this.busy.set(false);
    }
  }

  private current(account: string, admin: string, session: string): boolean {
    return !this.destroy.destroyed && this.requestGeneration === this.generation && this.form.controls.accountId.value.toLowerCase() === account &&
      this.admin.adminId() === admin && this.admin.sessionId() === session;
  }

  private clearRelease(): void {
    this.releaseImpact.set(null);
    this.releaseReason.reset();
  }

  private clearResults(): void {
    this.generation++;
    this.clearRelease();
    this.impact.set(null);
    this.saved.set([]);
    this.pending.set(null);
    this.error.set(null);
  }

  private errorText(error: unknown): string {
    const response = error instanceof SuspensionRefusal ? error.response : error;
    const code = response instanceof HttpErrorResponse ? response.error?.code : null;
    switch (code) {
      case 'Suspension.RecentMfaRequired': return 'Sign in again with MFA to change a suspension.';
      case 'Suspension.VersionConflict': return 'The scope or its impact changed. Preview it again.';
      case 'Suspension.NotFound': return 'The account or selected shop could not be found.';
      case 'Suspension.InvalidRelease': return 'Enter a reason and confirm a current release preview.';
      case 'Suspension.Invalid': return 'Check the details and choose an effective time in the future or apply immediately.';
      default: return this.pending() ? 'The save outcome is unconfirmed. Retry the saved request.'
        : 'The request could not be completed. Try again.';
    }
  }
}
