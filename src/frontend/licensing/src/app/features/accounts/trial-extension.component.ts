import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { problemCode } from '@intelibill/api';
import { ButtonDirective } from 'primeng/button';
import type { Observable, Subscription } from 'rxjs';
import { AdminSessionService } from '../auth/admin-session.service';
import { formatIstExact, type AccountTerm } from './account-status-api.service';
import { exactIstInstant, TrialExtensionApi, type TrialExtensionPreview, type TrialExtensionResult } from './trial-extension-api.service';
import { TrialExtensionDrafts, type PendingTrialExtension } from './trial-extension-drafts';

type Phase = 'loading' | 'edit' | 'previewing' | 'sending' | 'unknown' | 'done' | 'storage';

@Component({
  selector: 'adm-trial-extension',
  imports: [ButtonDirective],
  templateUrl: './trial-extension.component.html',
  styleUrls: ['../form-page.css', './account-status-page.component.css'],
  styles: `
    .acknowledgement {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .acknowledgement input {
      flex: 0 0 auto;
      inline-size: 1.125rem;
      block-size: 1.125rem;
      min-height: 0;
      margin: 0;
      padding: 0;
      accent-color: var(--p-primary-color);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrialExtensionComponent {
  private readonly api = inject(TrialExtensionApi);
  private readonly drafts = inject(TrialExtensionDrafts);
  private readonly session = inject(AdminSessionService);
  readonly accountId = input.required<string>();
  readonly trials = input.required<AccountTerm['coveredShops']>();
  readonly extended = output<void>();

  protected readonly shop = signal('');
  protected readonly endsAt = signal('');
  protected readonly reason = signal('');
  protected readonly reviewReference = signal('');
  protected readonly confirmed = signal(false);
  protected readonly error = signal('');
  protected readonly preview = signal<TrialExtensionPreview | null>(null);
  protected readonly pending = signal<PendingTrialExtension | null>(null);
  protected readonly result = signal<TrialExtensionResult | null>(null);
  protected readonly phase = signal<Phase>('loading');
  protected readonly busy = computed(() => ['loading', 'previewing', 'sending'].includes(this.phase()));
  protected readonly instant = formatIstExact;
  private request?: Subscription;
  private generation = 0;

  constructor() {
    effect(() => {
      const account = this.accountId();
      const admin = this.session.adminId();
      this.session.sessionId();
      untracked(() => void this.restore(account, admin));
    });
    inject(DestroyRef).onDestroy(() => this.invalidate());
  }

  protected edit(field: 'shop' | 'endsAt' | 'reason' | 'reviewReference', value: string) {
    this.invalidate();
    this[field].set(value);
    this.phase.set('edit');
  }

  protected requestPreview(event: Event) {
    event.preventDefault();
    const sessionId = this.session.sessionId();
    const shop = this.shop();
    const endsAt = exactIstInstant(this.endsAt());
    const reason = this.reason().trim();
    const reviewReference = this.reviewReference().trim();
    if (this.busy() || this.pending() || !sessionId) return;
    if (!this.trials().some(item => item.shopId === shop) || !endsAt || !reason || !reviewReference) {
      this.error.set('Choose a shop and enter an IST deadline, reason and review reference.');
      return;
    }
    this.invalidate();
    this.phase.set('previewing');
    this.run(this.api.preview(this.accountId(), shop, { endsAt, reason, reviewReference }, sessionId), preview => {
      this.preview.set(preview);
      this.phase.set('edit');
    }, error => this.refuse(error));
  }

  protected async approve() {
    const preview = this.preview();
    const adminId = this.session.adminId();
    const sessionId = this.session.sessionId();
    const accountId = this.accountId();
    if (!preview || !adminId || !sessionId || !this.confirmed() || this.busy() || this.pending()
      || !this.trials().some(item => item.shopId === preview.shopId)) return;
    const pending: PendingTrialExtension = {
      adminId, accountId, shopId: preview.shopId,
      command: {
        operationKey: crypto.randomUUID(), endsAt: preview.endsAt, reason: preview.reason,
        reviewReference: preview.reviewReference, currentVersion: preview.currentVersion,
        sourceHash: preview.sourceHash, confirmed: true,
      },
    };
    this.phase.set('sending');
    const generation = this.generation;
    try {
      const owner = await this.drafts.claim(pending);
      if (!this.isCurrent(generation, accountId, sessionId)) return;
      this.pending.set(owner);
      if (owner.command.operationKey !== pending.command.operationKey) {
        this.unknown('Another tab has an unconfirmed extension. Check that original operation.');
        return;
      }
      this.send(this.api.confirm(accountId, pending.shopId, owner.command, sessionId), owner, true);
    } catch {
      if (this.isCurrent(generation, accountId, sessionId)) this.storageError();
    }
  }

  protected recover() {
    const pending = this.pending();
    const session = this.session.sessionId();
    if (pending && session && !this.busy()) {
      this.send(this.api.recover(pending.accountId, pending.shopId, pending.command.operationKey, session), pending, false);
    }
  }

  protected async retry() {
    const pending = this.pending();
    const session = this.session.sessionId();
    if (!pending || !session || this.busy()) return;
    this.phase.set('sending');
    const generation = this.generation;
    try {
      const current = await this.drafts.read(pending.adminId, pending.accountId);
      if (!this.isCurrent(generation, pending.accountId, session)) return;
      if (current?.command.operationKey !== pending.command.operationKey) {
        await this.restore(pending.accountId, pending.adminId);
        return;
      }
      this.send(this.api.confirm(current.accountId, current.shopId, current.command, session), current, false);
    } catch {
      if (this.isCurrent(generation, pending.accountId, session)) this.unknown('The original extension could not be read. Try again.');
    }
  }

  protected reset() {
    void this.restore(this.accountId(), this.session.adminId());
  }

  private send(request: Observable<TrialExtensionResult>, pending: PendingTrialExtension, initial: boolean) {
    this.phase.set('sending');
    this.error.set('');
    const generation = this.generation;
    this.run(request, result => {
      void this.clear(pending, generation, () => {
        this.result.set(result);
        this.phase.set('done');
        this.extended.emit();
      });
    }, error => {
      if (initial && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
        void this.clear(pending, generation, () => this.refuse(error));
        return;
      }
      this.unknown(problemCode(error.error) === 'TrialExtension.RecentMfaRequired'
        ? 'Sign in again with MFA, then check the original extension.'
        : 'The extension outcome is unconfirmed. Check or retry the original operation.');
    });
  }

  private async clear(pending: PendingTrialExtension, generation: number, next: () => void) {
    try {
      await this.drafts.clear(pending);
      if (generation !== this.generation) return;
      this.pending.set(null);
      this.invalidate();
      next();
    } catch {
      if (generation === this.generation) this.unknown('This browser could not clear the saved extension. Check the original operation.');
    }
  }

  private run<T>(request: Observable<T>, next: (value: T) => void, failed: (error: HttpErrorResponse) => void) {
    const generation = this.generation;
    const account = this.accountId();
    const session = this.session.sessionId();
    this.request = request.subscribe({
      next: value => { if (this.isCurrent(generation, account, session)) next(value); },
      error: (error: unknown) => {
        if (this.isCurrent(generation, account, session))
          failed(error instanceof HttpErrorResponse ? error : new HttpErrorResponse({ status: 0 }));
      },
    });
  }

  private isCurrent(generation: number, account: string, session: string | null) {
    return generation === this.generation && account === this.accountId() && session === this.session.sessionId();
  }

  private invalidate() {
    this.request?.unsubscribe();
    this.generation++;
    this.preview.set(null);
    this.confirmed.set(false);
    this.error.set('');
  }

  private async restore(account: string, admin: string | null) {
    this.invalidate();
    const generation = this.generation;
    this.result.set(null);
    this.pending.set(null);
    this.phase.set('loading');
    for (const field of [this.shop, this.endsAt, this.reason, this.reviewReference]) field.set('');
    if (!admin) return;
    try {
      const pending = await this.drafts.read(admin, account);
      if (generation !== this.generation) return;
      this.pending.set(pending);
      this.phase.set(pending ? 'unknown' : 'edit');
    } catch {
      if (generation === this.generation) this.storageError();
    }
  }

  private unknown(message: string) {
    this.preview.set(null);
    this.phase.set('unknown');
    this.error.set(message);
  }

  private storageError() {
    this.phase.set('storage');
    this.error.set('This browser could not access saved extension requests. Try again when browser storage is available.');
  }

  private refuse(error: HttpErrorResponse) {
    this.phase.set('edit');
    const code = problemCode(error.error);
    const messages: Record<string, string> = {
      'TrialExtension.Invalid': 'Check the deadline, reason and review reference.',
      'TrialExtension.EndNotLater': 'The deadline must be after the current trial end and in the future.',
      'TrialExtension.TrialNotFound': 'The shop no longer has an eligible trial in this account.',
      'TrialExtension.NotEligible': 'The shop’s current trial coverage is ineligible for extension.',
      'TrialExtension.VersionConflict': 'The trial or account changed. Preview the extension again.',
      'TrialExtension.RecentMfaRequired': 'Sign in again with MFA to approve an extension.',
    };
    this.error.set(messages[code ?? ''] ?? 'The extension request could not be completed. Try again.');
  }
}
