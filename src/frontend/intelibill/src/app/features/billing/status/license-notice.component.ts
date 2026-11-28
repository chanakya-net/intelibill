import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { TranslocoPipe } from '@ngneat/transloco';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { ShopContextService } from '../../../core/context/shop-context.service';
import { LocalizationService } from '../../../core/i18n/localization.service';
import { LocaleMoneyPipe } from '../../../shared/pipes/locale-money.pipe';
import { formatBillingInstant } from './subscription-status.models';
import type {
  AccountLicenseNoticeItem,
  AccountLicenseNotices,
  LicenseNoticeFailure,
  ShopLicenseNoticeItem,
  ShopLicenseNotices,
} from './license-notice.models';
import { LicenseNoticeReadError, LicenseNoticeService } from './license-notice.service';

type NoticeView =
  | { readonly kind: 'idle' | 'loading' }
  | { readonly kind: 'failed'; readonly failure: LicenseNoticeFailure }
  | { readonly kind: 'shop'; readonly notices: readonly ShopLicenseNoticeItem[]; readonly nextDueAt: string | null }
  | {
      readonly kind: 'account';
      readonly evaluatedAt: string;
      readonly notices: readonly AccountLicenseNoticeItem[];
      readonly nextDueAt: string | null;
    };

type NoticeScope = {
  readonly mode: 'shop' | 'account';
  readonly userId: string;
  readonly targetId: string;
};

const MIN_REFRESH_MS = 1_000;
const MAX_STEP_MS = 6 * 60 * 60 * 1_000;
const DEADLINE_SETTLE_MS = 250;

/**
 * Displays due operational shop notices (shell banner) or due commercial account notices (selected account).
 * Clears immediately on scope/account/session transitions, drops late epoch replies, and revalidates on
 * focus, visibility, reconnect, and bounded server-relative timers across multi-day deadlines.
 */
@Component({
  selector: 'app-license-notice',
  standalone: true,
  imports: [TranslocoPipe, LocaleMoneyPipe],
  templateUrl: './license-notice.component.html',
  styleUrl: './license-notice.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(window:focus)': 'revalidate()',
    '(window:online)': 'revalidate()',
    '(document:visibilitychange)': 'revalidate()',
  },
})
export class LicenseNoticeComponent {
  private readonly api = inject(LicenseNoticeService);
  private readonly auth = inject(AuthService);
  private readonly shopContext = inject(ShopContextService);
  private readonly localization = inject(LocalizationService);

  readonly mode = input<'shop' | 'account'>('shop');
  readonly accountId = input<string | null>(null);

  protected readonly view = signal<NoticeView>({ kind: 'idle' });

  private readonly scope = computed<NoticeScope | null>(() => {
    const session = this.auth.session();
    if (!session || this.shopContext.transitioning()) {
      return null;
    }

    const mode = this.mode();
    if (mode === 'account') {
      const account = this.accountId()?.trim().toLowerCase() || null;
      return account ? { mode, userId: session.user.id, targetId: account } : null;
    }

    if (this.shopContext.dataRestricted()) {
      return null;
    }

    const shopId = session.activeShopId;
    return shopId ? { mode, userId: session.user.id, targetId: shopId } : null;
  });

  private generation = 0;
  private inFlight = false;
  private refreshPendingAfterInFlight = false;
  private nextTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    effect(() => {
      const current = this.scope();
      untracked(() => this.resetAndLoad(current));
    });

    inject(DestroyRef).onDestroy(() => {
      this.generation++;
      this.clearTimer();
    });
  }

  protected revalidate(): void {
    if (document.visibilityState !== 'visible') {
      return;
    }

    if (this.inFlight) {
      this.refreshPendingAfterInFlight = true;
      return;
    }

    const current = this.scope();
    if (current) {
      void this.fetchNotices(current, this.generation);
    }
  }

  protected instant(value: string | null): string | null {
    return formatBillingInstant(value, this.localization.currentLanguage(), true);
  }

  protected rupees(paise: number | null): number | null {
    return paise === null ? null : paise / 100;
  }

  protected operationalGuidanceKey(code: string): string {
    return `billing.notices.operationalGuidance.${code}`;
  }

  protected guidanceKey(code: string): string {
    return `billing.notices.guidance.${code}`;
  }

  protected purposeKey(purpose: string): string {
    return `billing.notices.purposes.${purpose}`;
  }

  protected thresholdKey(threshold: string): string {
    return `billing.notices.thresholds.${threshold}`;
  }

  private resetAndLoad(current: NoticeScope | null): void {
    this.generation++;
    this.inFlight = false;
    this.refreshPendingAfterInFlight = false;
    this.clearTimer();
    this.view.set({ kind: 'idle' });
    if (!current) {
      return;
    }

    if (current.mode === 'account') {
      this.view.set({ kind: 'loading' });
    }

    void this.fetchNotices(current, this.generation);
  }

  private async fetchNotices(target: NoticeScope, generation: number): Promise<void> {
    this.inFlight = true;
    try {
      if (target.mode === 'shop') {
        const result = await firstValueFrom(this.api.shop(target.targetId));
        this.applyShopResult(result, target.targetId, generation);
        return;
      }

      const result = await firstValueFrom(this.api.account(target.targetId));
      this.applyAccountResult(result, target.targetId, generation);
    } catch (error: unknown) {
      if (generation !== this.generation) {
        return;
      }

      const failure: LicenseNoticeFailure = error instanceof LicenseNoticeReadError ? error.failure : 'unavailable';
      this.view.set(target.mode === 'account' ? { kind: 'failed', failure } : { kind: 'idle' });
    } finally {
      this.finishRequest(generation);
    }
  }

  private applyShopResult(result: ShopLicenseNotices, shopId: string, generation: number): void {
    if (generation !== this.generation || result.shopId !== shopId) {
      return;
    }

    this.view.set({ kind: 'shop', notices: result.notices, nextDueAt: result.nextDueAt });
    this.scheduleNextRefresh(result.evaluatedAt, result.nextDueAt, generation);
  }

  private applyAccountResult(result: AccountLicenseNotices, accountId: string, generation: number): void {
    if (generation !== this.generation || result.accountId.toLowerCase() !== accountId) {
      return;
    }

    this.view.set({
      kind: 'account',
      evaluatedAt: result.evaluatedAt,
      notices: result.notices,
      nextDueAt: result.nextDueAt,
    });
    this.scheduleNextRefresh(result.evaluatedAt, result.nextDueAt, generation);
  }

  private finishRequest(generation: number): void {
    if (generation !== this.generation) {
      return;
    }

    this.inFlight = false;
    if (!this.refreshPendingAfterInFlight) {
      return;
    }

    this.refreshPendingAfterInFlight = false;
    const current = this.scope();
    if (current) {
      void this.fetchNotices(current, generation);
    }
  }

  private scheduleNextRefresh(evaluatedAt: string, nextDueAt: string | null, generation: number): void {
    this.clearTimer();
    if (!nextDueAt || generation !== this.generation) {
      return;
    }

    const dueMs = Date.parse(nextDueAt);
    if (!Number.isFinite(dueMs)) {
      return;
    }

    // Use server evaluation time to compute relative delay so client clock skew cannot miss or shift updates.
    const evaluatedMs = Date.parse(evaluatedAt);
    const serverRemainingMs = Number.isFinite(evaluatedMs)
      ? Math.max(0, dueMs - evaluatedMs)
      : Math.max(0, dueMs - Date.now());
    const targetLocalMs = Date.now() + serverRemainingMs + DEADLINE_SETTLE_MS;
    this.armBoundedTimer(targetLocalMs, generation);
  }

  private armBoundedTimer(targetLocalMs: number, generation: number): void {
    this.clearTimer();
    if (generation !== this.generation) {
      return;
    }

    const remainingMs = targetLocalMs - Date.now();
    if (remainingMs <= MAX_STEP_MS) {
      this.nextTimer = setTimeout(() => {
        this.nextTimer = null;
        this.onDeadlineReached(generation);
      }, Math.max(MIN_REFRESH_MS, remainingMs));
      return;
    }

    this.nextTimer = setTimeout(() => {
      this.nextTimer = null;
      this.armBoundedTimer(targetLocalMs, generation);
    }, MAX_STEP_MS);
  }

  private onDeadlineReached(generation: number): void {
    if (generation !== this.generation) {
      return;
    }

    if (this.inFlight) {
      this.refreshPendingAfterInFlight = true;
      return;
    }

    const current = this.scope();
    if (current) {
      void this.fetchNotices(current, generation);
    }
  }

  private clearTimer(): void {
    if (this.nextTimer !== null) {
      clearTimeout(this.nextTimer);
      this.nextTimer = null;
    }
  }
}
