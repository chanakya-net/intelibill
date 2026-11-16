import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@ngneat/transloco';
import { ButtonModule } from 'primeng/button';
import { firstValueFrom } from 'rxjs';
import { ShopContextService } from '../../../core/context/shop-context.service';
import type { ShopScope } from '../../../core/context/shop-scope';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { formatBillingInstant } from '../status/subscription-status.models';
import { ShopTrialDrafts } from './shop-trial-drafts';
import { ShopTrialService, type TrialAcceptance, type TrialOperation, type TrialPreview } from './shop-trial.service';

@Component({
  selector: 'app-shop-trial-page',
  imports: [RouterLink, TranslocoPipe, ButtonModule],
  templateUrl: './shop-trial-page.component.html',
  styleUrls: ['../status/subscription-status-page.component.scss', './shop-trial-page.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShopTrialPageComponent {
  private readonly api = inject(ShopTrialService);
  private readonly drafts = inject(ShopTrialDrafts);
  private readonly context = inject(ShopContextService);
  private readonly locale = inject(LocalizationService);
  private readonly destroy = inject(DestroyRef);
  private loadedScope: ShopScope | null = null;
  private poll?: ReturnType<typeof setTimeout>;

  protected readonly preview = signal<TrialPreview | null>(null);
  protected readonly draft = signal<TrialAcceptance | null>(null);
  protected readonly operation = signal<TrialOperation | null>(null);
  protected readonly checked = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() {
    effect((cleanup) => {
      const scope = this.context.scope();
      untracked(() => this.load(scope));
      cleanup(() => clearTimeout(this.poll));
    });
  }

  protected instant(value: string) {
    return formatBillingInstant(value, this.locale.currentLanguage(), true);
  }

  protected reload() {
    this.load(this.context.scope());
  }

  protected confirm(): void {
    const scope = this.context.scope();
    const preview = this.preview();
    if (!scope?.shopId || this.loadedScope?.epoch !== scope.epoch || preview?.shopId !== scope.shopId || !preview.eligible || !preview.policy || !this.checked() || this.draft() || this.busy()) return;
    try {
      const saved = this.drafts.freeze(scope, preview.policy);
      this.draft.set(saved.acceptance);
      this.checked.set(false);
      void this.send(scope, saved.acceptance, saved.created);
    } catch {
      this.error.set('billing.trial.storageUnavailable');
    }
  }

  protected retry(): void {
    const scope = this.context.scope();
    const draft = this.draft();
    if (scope?.shopId && this.loadedScope?.epoch === scope.epoch && draft && !this.busy()) void this.send(scope, draft, true);
  }

  private current(scope: ShopScope): boolean {
    return !this.destroy.destroyed && this.context.isCurrent(scope) && this.context.epoch() === scope.epoch;
  }

  private load(scope: ShopScope | null): void {
    this.loadedScope = scope;
    clearTimeout(this.poll);
    this.preview.set(null);
    this.draft.set(null);
    this.operation.set(null);
    this.checked.set(false);
    this.error.set('');
    this.busy.set(false);
    if (!scope?.shopId) {
      this.error.set('billing.trial.ownerRequired');
      return;
    }
    try {
      const draft = this.drafts.read(scope);
      this.draft.set(draft);
      if (draft) void this.send(scope, draft, false);
      else void this.readPreview(scope);
    } catch {
      this.error.set('billing.trial.storageUnavailable');
    }
  }

  private async readPreview(scope: ShopScope): Promise<void> {
    this.busy.set(true);
    try {
      const preview = await firstValueFrom(this.api.preview(scope).pipe(takeUntilDestroyed(this.destroy)));
      if (this.current(scope) && preview.shopId === scope.shopId) this.preview.set(preview);
    } catch (error) {
      if (this.current(scope)) this.error.set(this.requestError(error));
    } finally {
      if (this.current(scope)) this.busy.set(false);
    }
  }

  private async send(scope: ShopScope, draft: TrialAcceptance, post: boolean): Promise<void> {
    clearTimeout(this.poll);
    this.busy.set(true);
    this.error.set('');
    try {
      const request = post ? this.api.accept(scope, draft) : this.api.recover(scope, draft.operationKey);
      const result = await firstValueFrom(request.pipe(takeUntilDestroyed(this.destroy)));
      if (!this.current(scope)) return;
      if (result.operationKey !== draft.operationKey) throw new Error('Invalid trial operation');
      this.settle(scope, result);
    } catch (error) {
      if (this.current(scope)) this.error.set(this.requestError(error));
    } finally {
      if (this.current(scope)) this.busy.set(false);
    }
  }

  private settle(scope: ShopScope, result: TrialOperation): void {
    this.operation.set(result);
    if (result.status === 'Pending') {
      this.poll = setTimeout(() => {
        const draft = this.draft();
        if (draft && this.current(scope)) void this.send(scope, draft, false);
      }, 1500);
      return;
    }
    try {
      this.drafts.settle(scope, result.operationKey);
      this.draft.set(this.drafts.read(scope));
      this.preview.set(null);
    } catch {
      this.error.set('billing.trial.storageUnavailable');
    }
  }

  private requestError(error: unknown): string {
    return error instanceof HttpErrorResponse && error.status === 403
      ? 'billing.trial.ownerRequired' : this.draft() ? 'billing.trial.outcomeUnknown' : 'billing.unavailable';
  }
}
